using System.Data;
using Microsoft.Data.SqlClient;
using pbl4.Core;

namespace pbl4.Persistence;

public sealed class SqlServerDownloadRepository : IDownloadRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public SqlServerDownloadRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, """
            IF OBJECT_ID(N'dbo.downloads', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.downloads (
                    id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    url NVARCHAR(2048) NOT NULL,
                    output_path NVARCHAR(1024) NOT NULL,
                    total_bytes BIGINT NOT NULL CHECK (total_bytes >= 0),
                    state INT NOT NULL CHECK (state BETWEEN 0 AND 5),
                    requested_concurrency INT NOT NULL CHECK (requested_concurrency > 0),
                    created_at DATETIME2 NOT NULL,
                    updated_at DATETIME2 NOT NULL
                );
            END;

            IF OBJECT_ID(N'dbo.segments', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.segments (
                    job_id UNIQUEIDENTIFIER NOT NULL,
                    segment_index INT NOT NULL,
                    start_byte BIGINT NOT NULL CHECK (start_byte >= 0),
                    end_byte BIGINT NOT NULL CHECK (end_byte >= 0),
                    downloaded_bytes BIGINT NOT NULL DEFAULT 0 CHECK (downloaded_bytes >= 0),
                    state INT NOT NULL CHECK (state BETWEEN 0 AND 3),
                    CONSTRAINT pk_segments PRIMARY KEY (job_id, segment_index),
                    CONSTRAINT fk_segments_downloads
                        FOREIGN KEY (job_id)
                        REFERENCES dbo.downloads(id)
                        ON DELETE CASCADE,
                    CONSTRAINT ck_segments_range CHECK (end_byte >= start_byte),
                    CONSTRAINT ck_segments_downloaded CHECK (downloaded_bytes <= end_byte - start_byte + 1)
                );
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'ix_downloads_updated_at'
                    AND object_id = OBJECT_ID(N'dbo.downloads')
            )
            BEGIN
                CREATE INDEX ix_downloads_updated_at ON dbo.downloads(updated_at DESC);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'ix_segments_job_state'
                    AND object_id = OBJECT_ID(N'dbo.segments')
            )
            BEGIN
                CREATE INDEX ix_segments_job_state ON dbo.segments(job_id, state);
            END;
            """, cancellationToken);
    }

    public async Task SaveAsync(DownloadJob job, CancellationToken cancellationToken = default)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await UpsertJobAsync(connection, transaction, job, cancellationToken);
            foreach (var segment in job.Segments)
            {
                await UpsertSegmentAsync(connection, transaction, job.Id, segment, cancellationToken);
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
                FROM dbo.downloads
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
                UPDATE dbo.downloads
                SET state = @state, updated_at = @updatedAt
                WHERE id = @id;
                """;
            AddParameter(command, "@id", SqlDbType.UniqueIdentifier, jobId);
            AddParameter(command, "@state", SqlDbType.Int, (int)state);
            AddParameter(command, "@updatedAt", SqlDbType.DateTime2, DateTime.UtcNow);
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
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await UpsertSegmentAsync(connection, transaction, jobId, segment, cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE dbo.downloads SET updated_at = @updatedAt WHERE id = @id;";
            AddParameter(command, "@id", SqlDbType.UniqueIdentifier, jobId);
            AddParameter(command, "@updatedAt", SqlDbType.DateTime2, DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertJobAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DownloadJob job,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE dbo.downloads
            SET url = @url,
                output_path = @outputPath,
                total_bytes = @totalBytes,
                state = @state,
                requested_concurrency = @requestedConcurrency,
                updated_at = @updatedAt
            WHERE id = @id;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.downloads (
                    id,
                    url,
                    output_path,
                    total_bytes,
                    state,
                    requested_concurrency,
                    created_at,
                    updated_at)
                VALUES (
                    @id,
                    @url,
                    @outputPath,
                    @totalBytes,
                    @state,
                    @requestedConcurrency,
                    @createdAt,
                    @updatedAt);
            END;
            """;
        AddJobParameters(command, job);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertSegmentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid jobId,
        DownloadSegment segment,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE dbo.segments
            SET start_byte = @start,
                end_byte = @end,
                downloaded_bytes = @downloaded,
                state = @state
            WHERE job_id = @jobId
                AND segment_index = @index;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.segments (
                    job_id,
                    segment_index,
                    start_byte,
                    end_byte,
                    downloaded_bytes,
                    state)
                VALUES (
                    @jobId,
                    @index,
                    @start,
                    @end,
                    @downloaded,
                    @state);
            END;
            """;
        AddParameter(command, "@jobId", SqlDbType.UniqueIdentifier, jobId);
        AddParameter(command, "@index", SqlDbType.Int, segment.Index);
        AddParameter(command, "@start", SqlDbType.BigInt, segment.Start);
        AddParameter(command, "@end", SqlDbType.BigInt, segment.End);
        AddParameter(command, "@downloaded", SqlDbType.BigInt, segment.DownloadedBytes);
        AddParameter(command, "@state", SqlDbType.Int, (int)segment.State);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<JobRow?> ReadJobRowAsync(
        SqlConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, url, output_path, total_bytes, state, requested_concurrency, created_at, updated_at
            FROM dbo.downloads
            WHERE id = @id;
            """;
        AddParameter(command, "@id", SqlDbType.UniqueIdentifier, jobId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadJobRow(reader);
    }

    private static async Task<IReadOnlyList<DownloadSegment>> ReadSegmentsAsync(
        SqlConnection connection,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var segments = new List<DownloadSegment>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT segment_index, start_byte, end_byte, downloaded_bytes, state
            FROM dbo.segments
            WHERE job_id = @jobId
            ORDER BY segment_index ASC;
            """;
        AddParameter(command, "@jobId", SqlDbType.UniqueIdentifier, jobId);

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

    private static JobRow ReadJobRow(SqlDataReader reader)
        => new(
            reader.GetGuid(0),
            new Uri(reader.GetString(1)),
            reader.GetString(2),
            reader.GetInt64(3),
            (DownloadState)reader.GetInt32(4),
            reader.GetInt32(5),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)));

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

    private static void AddJobParameters(SqlCommand command, DownloadJob job)
    {
        AddParameter(command, "@id", SqlDbType.UniqueIdentifier, job.Id);
        AddParameter(command, "@url", SqlDbType.NVarChar, job.Url.ToString(), 2048);
        AddParameter(command, "@outputPath", SqlDbType.NVarChar, job.OutputPath, 1024);
        AddParameter(command, "@totalBytes", SqlDbType.BigInt, job.TotalBytes);
        AddParameter(command, "@state", SqlDbType.Int, (int)job.State);
        AddParameter(command, "@requestedConcurrency", SqlDbType.Int, job.RequestedConcurrency);
        AddParameter(command, "@createdAt", SqlDbType.DateTime2, job.CreatedAt.UtcDateTime);
        AddParameter(command, "@updatedAt", SqlDbType.DateTime2, job.UpdatedAt.UtcDateTime);
    }

    private static void AddParameter(SqlCommand command, string name, SqlDbType type, object value, int? size = null)
    {
        var parameter = command.Parameters.Add(name, type);
        if (size is not null)
        {
            parameter.Size = size.Value;
        }

        parameter.Value = value;
    }

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
