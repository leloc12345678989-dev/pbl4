# TEST PLAN - pbl4

## Unit tests

| Area | Test file | Noi dung |
| --- | --- | --- |
| Networking | `Networking.Tests/HttpRangeParserTests.cs` | Parse `Content-Range`, parse `Range`, invalid cases |
| Networking | `Networking.Tests/RawHttpResponseParserTests.cs` | Parse raw HTTP status line va headers |
| OSIntegration | `OSIntegration.Tests/SegmentPlannerTests.cs` | Chia segment deu, co du, segment > bytes |
| OSIntegration | `OSIntegration.Tests/SafeFileWriterTests.cs` | Ghi file theo offset bang writer an toan |
| Core | `Core.Tests/DownloadJobTests.cs` | State machine va progress |
| Core | `Core.Tests/TokenBucketRateLimiterTests.cs` | Token bucket basic behavior |
| Persistence | `Persistence.Tests/SqliteDownloadRepositoryTests.cs` | Luu job, segment progress, pause state |

## Integration tests

| Test | Muc tieu |
| --- | --- |
| `CreateAsync_WhenServerSupportsRanges_CreatesMultipleSegments` | Probe HTTP Range -> chia segment |
| `CreateAsync_WhenServerDoesNotSupportRanges_FallsBackToOneSegment` | Fallback 1 luong khi server khong support range |
| `StartAsync_DownloadsAllSegmentsAndMovesPartFileToFinalPath` | Engine + file writer tai day du noi dung |
| `StartAsync_WithExistingSegmentProgress_RequestsOnlyRemainingBytes` | Resume chi tai byte con thieu |

