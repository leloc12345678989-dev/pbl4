using System.Diagnostics;
using pbl4.Core;
using pbl4.Networking;
using pbl4.OSIntegration;
using pbl4.Persistence;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dotnet run --project src/Benchmark -- <url> [outputDir]");
    return 1;
}

var url = new Uri(args[0]);
var outputDir = args.Length >= 2 ? args[1] : "benchmark-results";
Directory.CreateDirectory(outputDir);
Directory.CreateDirectory(Path.Combine(outputDir, "downloads"));

var csvPath = Path.Combine(outputDir, "benchmark.csv");
var chartPath = Path.Combine(outputDir, "benchmark-chart.html");
var dbPath = Path.Combine(outputDir, "benchmark.db");
var segments = new[] { 1, 4, 8 };
var rows = new List<BenchmarkRow>();

foreach (var segmentCount in segments)
{
    var repository = new SqliteDownloadRepository(dbPath);
    await repository.InitializeAsync();
    var progressReporter = new ProgressReporter();
    var engine = new DownloadEngine(
        new HttpRangeClient(new HttpRangeClientOptions { BufferSize = 1024 * 1024 }),
        new SegmentPlanner(),
        repository,
        progressReporter,
        new DownloadEngineOptions { MaxConcurrency = 8, BufferSize = 1024 * 1024 });

    var outputPath = Path.GetFullPath(Path.Combine(outputDir, "downloads", $"benchmark-{segmentCount}.bin"));
    DeleteIfExists(outputPath);
    DeleteIfExists(outputPath + ".part");

    Console.WriteLine($"Benchmark {segmentCount} segment(s): {url}");
    var stopwatch = Stopwatch.StartNew();
    var job = await engine.CreateAsync(new CreateDownloadRequest(url, outputPath, segmentCount));
    await engine.StartAsync(job.Id);
    stopwatch.Stop();

    var loaded = await engine.FindAsync(job.Id);
    var totalBytes = loaded?.TotalBytes ?? 0;
    var avgSpeed = stopwatch.Elapsed.TotalSeconds > 0 ? totalBytes / stopwatch.Elapsed.TotalSeconds : 0;
    rows.Add(new BenchmarkRow(DateTimeOffset.UtcNow, url.ToString(), segmentCount, loaded?.State.ToString() ?? "Unknown", totalBytes, stopwatch.Elapsed.TotalSeconds, avgSpeed, outputPath));
}

await File.WriteAllLinesAsync(csvPath, BuildCsv(rows));
await File.WriteAllTextAsync(chartPath, BuildChartHtml(url.ToString(), rows));

Console.WriteLine($"CSV: {Path.GetFullPath(csvPath)}");
Console.WriteLine($"Chart: {Path.GetFullPath(chartPath)}");
return 0;

static void DeleteIfExists(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

static IEnumerable<string> BuildCsv(IEnumerable<BenchmarkRow> rows)
{
    yield return "timestamp,url,segment_count,state,total_bytes,elapsed_seconds,avg_speed_bytes_per_second,output_path";
    foreach (var row in rows)
    {
        yield return string.Join(",", [
            row.Timestamp.ToString("O"),
            Escape(row.Url),
            row.SegmentCount.ToString(),
            row.State,
            row.TotalBytes.ToString(),
            Math.Round(row.ElapsedSeconds, 3).ToString(),
            Math.Round(row.AvgSpeedBytesPerSecond, 2).ToString(),
            Escape(row.OutputPath)
        ]);
    }
}

static string Escape(string value)
    => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

static string BuildChartHtml(string url, IReadOnlyList<BenchmarkRow> rows)
{
    var labels = string.Join(",", rows.Select(row => $"'{row.SegmentCount} segment'"));
    var times = string.Join(",", rows.Select(row => Math.Round(row.ElapsedSeconds, 3)));
    var speeds = string.Join(",", rows.Select(row => Math.Round(row.AvgSpeedBytesPerSecond / 1048576, 2)));

    return $$"""
    <!doctype html>
    <html lang="vi">
    <head>
      <meta charset="utf-8" />
      <title>pbl4 Local Benchmark</title>
      <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    </head>
    <body style="font-family: Segoe UI, Arial, sans-serif; margin: 32px">
      <h1>pbl4 Local Benchmark</h1>
      <p>URL: {{url}}</p>
      <canvas id="chart" style="max-height: 420px"></canvas>
      <script>
        new Chart(document.getElementById('chart'), {
          type: 'bar',
          data: {
            labels: [{{labels}}],
            datasets: [
              { label: 'Thoi gian (giay)', data: [{{times}}], yAxisID: 'y' },
              { label: 'Toc do TB (MB/s)', data: [{{speeds}}], yAxisID: 'y1' }
            ]
          },
          options: {
            responsive: true,
            scales: {
              y: { beginAtZero: true, position: 'left' },
              y1: { beginAtZero: true, position: 'right', grid: { drawOnChartArea: false } }
            }
          }
        });
      </script>
    </body>
    </html>
    """;
}

internal sealed record BenchmarkRow(
    DateTimeOffset Timestamp,
    string Url,
    int SegmentCount,
    string State,
    long TotalBytes,
    double ElapsedSeconds,
    double AvgSpeedBytesPerSecond,
    string OutputPath);

