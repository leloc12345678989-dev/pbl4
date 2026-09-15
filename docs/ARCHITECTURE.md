# ARCHITECTURE - pbl4

De tai: **Nghien cuu co che Windows Thread Pool va Overlapped I/O trong viec toi uu hoa toc do va quan ly tai nguyen cho trinh tai xuong HTTP phan doan**.

## 1. Kien truc desktop local

```mermaid
flowchart TB
    User["Nguoi dung"] --> UI["WinForms UI"]
    UI --> Core["DownloadEngine"]
    Core --> Networking["Networking"]
    Core --> OS["OSIntegration"]
    Core --> Persistence["SQLite Persistence"]
    Networking --> Server["HTTP/HTTPS file server"]
    OS --> File[".part / final file"]
    Core --> Progress["ProgressReporter"]
    Progress --> UI
    Benchmark["Benchmark Console"] --> Core
```

Ung dung khong can web public hay host `localhost`. WinForms goi truc tiep engine local, file duoc tai ve may nguoi dung. Cach nay dung voi muc tieu hoc Windows Thread Pool, Overlapped I/O, socket, disk I/O va quan ly tai nguyen tren may local.

## 2. Module

| Module | Vai tro |
| --- | --- |
| `src/UI` | WinForms app, nhap URL, thread count, pause/resume/cancel |
| `src/Core` | Download engine, state machine, retry, resume, throttle |
| `src/Networking` | HTTP Range client, raw HTTP parser, TCP/UDP probe |
| `src/OSIntegration` | ThreadPool manager, Overlapped file writer, Named Pipe IPC demo |
| `src/Persistence` | SQLite metadata cho resume |
| `src/Benchmark` | Benchmark local 1/4/8 segment |

## 3. Class diagram

```mermaid
classDiagram
    class MainForm
    class DownloadEngine
    class DownloadJob
    class DownloadSegment
    class HttpRangeClient
    class SegmentPlanner
    class ThreadPoolManager
    class SafeFileWriter
    class SqliteDownloadRepository
    class ProgressReporter

    MainForm --> DownloadEngine
    DownloadEngine --> DownloadJob
    DownloadJob --> DownloadSegment
    DownloadEngine --> HttpRangeClient
    DownloadEngine --> SegmentPlanner
    DownloadEngine --> ThreadPoolManager
    DownloadEngine --> SafeFileWriter
    DownloadEngine --> SqliteDownloadRepository
    DownloadEngine --> ProgressReporter
```

## 4. Sequence download nhieu segment

```mermaid
sequenceDiagram
    participant UI as WinForms UI
    participant Engine as DownloadEngine
    participant HTTP as HttpRangeClient
    participant TP as ThreadPoolManager
    participant Writer as SafeFileWriter
    participant DB as SQLite
    participant Server as HTTP Server

    UI->>Engine: CreateAsync(url, outputPath, segmentCount)
    Engine->>HTTP: HEAD probe
    HTTP->>Server: HEAD /file
    Server-->>HTTP: Accept-Ranges + Content-Length
    Engine->>DB: Save job + segments
    Engine-->>UI: jobId
    UI->>Engine: StartAsync(jobId)
    loop moi segment
        Engine->>TP: RunAsync(segmentWorker)
        TP->>HTTP: GET Range bytes=start-end
        HTTP->>Server: Range request
        Server-->>HTTP: 206 Partial Content
        HTTP->>Writer: WriteAsync(offset, buffer)
        Writer->>DB: Update segment progress
        Engine-->>UI: Progress event
    end
```

## 5. Mapping yeu cau hoc thuat

| Yeu cau | Code |
| --- | --- |
| Windows Thread Pool | `OSIntegration/ThreadPoolManager.cs`, `Core/DownloadEngine.cs` |
| Overlapped I/O | `OSIntegration/SafeFileWriter.cs` dung `FileOptions.Asynchronous` va `RandomAccess.WriteAsync` |
| Gioi han luong | `SemaphoreSlim` trong `ThreadPoolManager` |
| Critical section | `DownloadJob.TransitionTo`, `SafeFileWriter.WriteAsync`, `SqliteDownloadRepository` |
| Deadlock prevention | Timeout khi doi semaphore/lock, khong giu lock khi network I/O |
| HTTP Range | `Networking/HttpRangeClient.cs`, `HttpRangeParser.cs` |
| Resume | `.part` file + SQLite segment metadata |
| TCP/UDP | `TcpProbeClient.cs`, `UdpProbeClient.cs` |
| Benchmark | `src/Benchmark/Program.cs`, `scripts/benchmark.ps1` |

