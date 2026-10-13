using Microsoft.Data.Sqlite;
using pbl4.Core;

namespace pbl4.Persistence;

public sealed class SqliteDownloadRepository : IDownloadRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public SqliteDownloadRepository(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };
        _connectionString = builder.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            """, cancellationToken);

        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE IF NOT EXISTS downloads (
                id TEXT PRIMARY KEY,
                url TEXT NOT NULL,
                output_path TEXT NOT NULL,
                total_bytes INTEGER NOT NULL,
                state INTEGER NOT NULL,
                requested_concurrency INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS segments (
                job_id TEXT NOT NULL,
                segment_index INTEGER NOT NULL,
                start_byte INTEGER NOT NULL,
                end_byte INTEGER NOT NULL,
                downloaded_bytes INTEGER NOT NULL,
                state INTEGER NOT NULL,
                PRIMARY KEY (job_id, segment_index),
                FOREIGN KEY (job_id) REFERENCES downloads(id) ON DELETE CASCADE
            );
            """, cancellationToken);
    }

    public async Task SaveAsync(DownloadJob job, CancellationToken cancellationToken = default)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            // Critical section database: job va segments phai duoc ghi trong cung transaction
            // de resume khong gap trang thai nua cu nua moi khi app bi tat dot ngot.
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO downloads (id, url, output_path, total_bytes, state, requested_concurrency, created_at, updated_at)
                    VALUES ($id, $url, $outputPath, $totalBytes, $state, $requestedConcurrency, $createdAt, $updatedAt)
                    ON CONFLICT(id) DO UPDATE SET
                        url = excluded.url,
                        output_path = excluded.output_path,
                        total_bytes = excluded.total_bytes,
                        state = excluded.state,
                        requested_concurrency = excluded.requested_concurrency,
                        updated_at = excluded.updated_at;
                    """;
                command.Parameters.AddWithValue("$id", job.Id.ToString());
                command.Parameters.AddWithValue("$url", job.Url.ToString());
                command.Parameters.AddWithValue("$outputPath", job.OutputPath);
                command.Parameters.AddWithValue("$totalBytes", job.TotalBytes);
                command.Parameters.AddWithValue("$state", (int)job.State);
                command.Parameters.AddWithValue("$requestedConcurrency", job.RequestedConcurrency);
                command.Parameters.AddWithValue("$createdAt", job.CreatedAt.ToString("O"));
                command.Parameters.AddWithValue("$updatedAt", job.UpdatedAt.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var segment in job.Segments)
            {
                await UpsertSegmentAsync(connection, (SqliteTransaction)transaction, job.Id, segment, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<DownloadJob?> FindAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var row = await ReadJobRowAsync(connection, jobId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var segments = await ReadSegmentsAsync(connection, jobId, cancellationToken);
        return ToJob(row.Value, segments);
    }

    public async Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var rows = new List<JobRow>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, url, output_path, total_bytes, state, requested_concurrency, created_at, updated_at
                FROM downloads
                ORDER BY updated_at DESC;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(ReadJobRow(reader));
            }
        }

        var jobs = new List<DownloadJob>(rows.Count);
        foreach (var row in rows)
        {
            var segments = await ReadSegmentsAsync(connection, row.Id, cancellationToken);
            jobs.Add(ToJob(row, segments));
        }

        return jobs;
    }

    public async Task UpdateJobStateAsync(Guid jobId, DownloadState state, CancellationToken cancellationToken = default)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE downloads
                SET state = $state, updated_at = $updatedAt
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", jobId.ToString());
            command.Parameters.AddWithValue("$state", (int)state);
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task UpdateSegmentAsync(Guid jobId, DownloadSegment segment, CancellationToken cancellationToken = default)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await UpsertSegmentAsync(connection, (SqliteTransaction)transaction, jobId, segment, cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "UPDATE downloads SET updated_at = $updatedAt WHERE id = $id;";
            command.Parameters.AddWithValue("$id", jobId.ToString());
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertSegmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid jobId,
        DownloadSegment segment,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO segments (job_id, segment_index, start_byte, end_byte, downloaded_bytes, state)
            VALUES ($jobId, $index, $start, $end, $downloaded, $state)
            ON CONFLICT(job_id, segment_index) DO UPDATE SET
                downloaded_bytes = excluded.downloaded_bytes,
                state = excluded.state;
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString());
        command.Parameters.AddWithValue("$index", segment.Index);
        command.Parameters.AddWithValue("$start", segment.Start);
        command.Parameters.AddWithValue("$end", segment.End);
        command.Parameters.AddWithValue("$downloaded", segment.DownloadedBytes);
        command.Parameters.AddWithValue("$state", (int)segment.State);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<JobRow?> ReadJobRowAsync(SqliteConnection connection, Guid jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, url, output_path, total_bytes, state, requested_concurrency, created_at, updated_at
            FROM downloads
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", jobId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadJobRow(reader);
    }

    private static async Task<IReadOnlyList<DownloadSegment>> ReadSegmentsAsync(
        SqliteConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var segments = new List<DownloadSegment>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT segment_index, start_byte, end_byte, downloaded_bytes, state
            FROM segments
            WHERE job_id = $jobId
            ORDER BY segment_index ASC;
            """;
        command.Parameters.AddWithValue("$jobId", jobId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            segments.Add(new DownloadSegment(
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                (SegmentState)reader.GetInt32(4)));
        }

        return segments;
    }

    private static JobRow ReadJobRow(SqliteDataReader reader)
        => new(
            Guid.Parse(reader.GetString(0)),
            new Uri(reader.GetString(1)),
            reader.GetString(2),
            reader.GetInt64(3),
            (DownloadState)reader.GetInt32(4),
            reader.GetInt32(5),
            DateTimeOffset.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7)));

    private static DownloadJob ToJob(JobRow row, IReadOnlyList<DownloadSegment> segments)
        => new(
            row.Id,
            row.Url,
            row.OutputPath,
            row.TotalBytes,
            segments,
            row.State,
            row.RequestedConcurrency,
            row.CreatedAt,
            row.UpdatedAt);

    private readonly record struct JobRow(
        Guid Id,
        Uri Url,
        string OutputPath,
        long TotalBytes,
        DownloadState State,
        int RequestedConcurrency,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
