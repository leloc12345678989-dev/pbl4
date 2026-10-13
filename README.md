# pbl4 - Windows HTTP Segmented Download Accelerator

De tai: **Nghien cuu co che Windows Thread Pool va Overlapped I/O trong viec toi uu hoa toc do va quan ly tai nguyen cho trinh tai xuong HTTP phan doan**.

Huong trien khai: **WinForms desktop app**, tai truc tiep tren may nguoi dung. Khong dung web public lam download server vi se ton bang thong, socket, CPU va disk I/O khi nhieu nguoi dung.

## Stack

- C# .NET 8
- WinForms desktop UI
- SQLite de luu metadata resume mac dinh
- SQL Server tuy chon neu muon demo DB rieng
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

## Luu metadata

Mac dinh UI dung SQLite tai thu muc output cua app nen co the chay ngay, khong can tao database truoc. Neu muon doi file SQLite:

```powershell
$env:PBL4_SQLITE_PATH="C:\data\pbl4-downloads.db"
.\scripts\run-ui.ps1
```

## SQL Server tuy chon

Project van ho tro SQL Server qua package `Microsoft.Data.SqlClient` trong `src/Persistence/pbl4.Persistence.csproj`. Tao database `pbl4_downloads` trong SSMS truoc, app se tu tao/kiem tra cac bang `downloads` va `segments` khi chay.

Connection string mac dinh:

```text
Server=.;Database=pbl4_downloads;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;
```

Neu may dung ten server khac, set bien moi truong `PBL4_SQLSERVER_CONNECTION_STRING` truoc khi chay app:

```powershell
$env:PBL4_REPOSITORY="sqlserver"
$env:PBL4_SQLSERVER_CONNECTION_STRING="Server=DESKTOP-K647TQ4;Database=pbl4_downloads;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;"
.\scripts\run-ui.ps1
```

## Benchmark

```powershell
.\scripts\benchmark.ps1 -Url "http://speedtest.tele2.net/100MB.zip"
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

## Link demo tai nhieu luong

Nen dung cac URL co `Content-Length` va `Accept-Ranges: bytes`:

```text
http://speedtest.tele2.net/100MB.zip
http://speedtest.tele2.net/1GB.zip
https://speedtest.singapore.linode.com/100MB-singapore.bin
https://speedtest.dallas.linode.com/100MB-dallas.bin
https://speedtest.newark.linode.com/100MB-newark.bin
```

Neu server tra `429 Too Many Requests`, giam Threads xuong 2-3 hoac doi server khac trong danh sach.
