# pbl4 - Windows HTTP Segmented Download Accelerator

De tai: **Nghien cuu co che Windows Thread Pool va Overlapped I/O trong viec toi uu hoa toc do va quan ly tai nguyen cho trinh tai xuong HTTP phan doan**.

Huong trien khai: **WinForms desktop app**, tai truc tiep tren may nguoi dung. Khong dung web public lam download server vi se ton bang thong, socket, CPU va disk I/O khi nhieu nguoi dung.

## Stack

- C# .NET 8
- WinForms desktop UI
- SQLite de luu metadata resume
- xUnit cho unit test va integration test

## Diem hoc thuat chinh

- HTTP/HTTPS segmented download bang `Range: bytes=start-end`.
- Windows Thread Pool qua `Task.Run` va async task scheduling.
- Gioi han song song bang `SemaphoreSlim`.
- Overlapped I/O qua `FileOptions.Asynchronous` va `RandomAccess.WriteAsync`.
- Pause/resume/cancel an toan bang `.part` file va SQLite metadata.
- Retry exponential backoff va token bucket bandwidth throttle.
- TCP RTT probe va UDP packet loss demo.
- Named Pipe IPC demo trong `OSIntegration`.

## Cau truc thu muc

```text
pbl4/
|-- src/
|   |-- Core/             Download engine, job state, progress, throttle
|   |-- Networking/       HTTP Range, TCP probe, UDP probe
|   |-- OSIntegration/    ThreadPool, Overlapped I/O writer, named pipe
|   |-- Persistence/      SQLite repository va resume metadata
|   |-- UI/               WinForms desktop app
|   `-- Benchmark/        Benchmark local 1/4/8 segment
|-- tests/
|   |-- Core.Tests/
|   |-- Networking.Tests/
|   |-- OSIntegration.Tests/
|   |-- Persistence.Tests/
|   `-- Integration.Tests/
|-- docs/
|   |-- ARCHITECTURE.md
|   |-- BAOCAO.md
|   `-- DEMO_5_PHUT.md
|-- scripts/
|   |-- build.ps1
|   |-- run-ui.ps1
|   `-- benchmark.ps1
|-- pbl4.sln
|-- README.md
`-- LICENSE
```

## Chay trong Visual Studio

1. Mo `pbl4.sln`.
2. Chuot phai `pbl4.UI`.
3. Chon **Set as Startup Project**.
4. Bam **Start**.

## Chay bang PowerShell

```powershell
cd C:\Users\ADMIN\Documents\Codex\2026-09-14\role-b-n-l-m-t\pbl4
.\scripts\build.ps1
.\scripts\run-ui.ps1
```

## Benchmark

```powershell
.\scripts\benchmark.ps1 -Url "https://speed.hetzner.de/100MB.bin"
```

Ket qua xuat ra:

```text
benchmark-results/benchmark.csv
benchmark-results/benchmark-chart.html
```

## Test

```powershell
dotnet test .\pbl4.sln --no-build --no-restore
```

Project khong dung aria2 hay download manager co san. Engine tu cai dat probe Range, chia segment, ghi offset, resume, retry, throttle va progress reporting.

