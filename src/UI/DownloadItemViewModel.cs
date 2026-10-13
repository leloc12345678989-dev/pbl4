using System.ComponentModel;
using System.Runtime.CompilerServices;
using pbl4.Core;

namespace pbl4.UI;

public sealed class DownloadItemViewModel : INotifyPropertyChanged
{
    private string _state = "Pending";
    private double _progressPercent;
    private long _downloadedBytes;
    private long _totalBytes;
    private double _speedBytesPerSecond;
    private int _activeSegments;
    private string _segmentStatusText = "";
    private string _message = "";
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;

    public Guid Id { get; init; }

    public string Url { get; init; } = "";

    public string OutputPath { get; init; } = "";

    public int SegmentCount { get; init; }

    public string State
    {
        get => _state;
        set => SetField(ref _state, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetField(ref _progressPercent, value);
    }

    public long DownloadedBytes
    {
        get => _downloadedBytes;
        set
        {
            if (SetField(ref _downloadedBytes, value))
            {
                OnPropertyChanged(nameof(DownloadedText));
            }
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            if (SetField(ref _totalBytes, value))
            {
                OnPropertyChanged(nameof(DownloadedText));
            }
        }
    }

    public double SpeedBytesPerSecond
    {
        get => _speedBytesPerSecond;
        set
        {
            if (SetField(ref _speedBytesPerSecond, value))
            {
                OnPropertyChanged(nameof(SpeedText));
            }
        }
    }

    public int ActiveSegments
    {
        get => _activeSegments;
        set => SetField(ref _activeSegments, value);
    }

    public string ThreadStatusText
    {
        get => _segmentStatusText;
        set => SetField(ref _segmentStatusText, value);
    }

    public string Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    public string DownloadedText => $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes)}";

    public string SpeedText => $"{FormatBytes((long)SpeedBytesPerSecond)}/s";

    public DateTimeOffset? StartedAt
    {
        get => _startedAt;
        set
        {
            if (SetField(ref _startedAt, value))
            {
                OnPropertyChanged(nameof(ElapsedText));
            }
        }
    }

    public DateTimeOffset? FinishedAt
    {
        get => _finishedAt;
        set
        {
            if (SetField(ref _finishedAt, value))
            {
                OnPropertyChanged(nameof(ElapsedText));
            }
        }
    }

    public string ElapsedText
    {
        get
        {
            if (StartedAt is null)
            {
                return "";
            }

            var end = FinishedAt ?? DateTimeOffset.UtcNow;
            var elapsed = end - StartedAt.Value;
            return elapsed.TotalHours >= 1
                ? elapsed.ToString(@"hh\:mm\:ss")
                : elapsed.ToString(@"mm\:ss\.f");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyProgress(DownloadProgressSnapshot progress)
    {
        State = progress.State.ToString();
        DownloadedBytes = progress.DownloadedBytes;
        TotalBytes = progress.TotalBytes;
        ProgressPercent = Math.Round(progress.ProgressPercent, 2);
        SpeedBytesPerSecond = progress.SpeedBytesPerSecond;
        ActiveSegments = progress.ActiveSegments;
        ThreadStatusText = FormatSegmentStatus(progress.Segments);
        Message = progress.Message;
        if (StartedAt is null && progress.State is DownloadState.Running)
        {
            StartedAt = progress.Timestamp;
        }

        if (progress.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled)
        {
            FinishedAt ??= progress.Timestamp;
        }

        OnPropertyChanged(nameof(ElapsedText));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatSegmentStatus(IReadOnlyList<SegmentProgressSnapshot> segments)
    {
        if (segments.Count == 0)
        {
            return "";
        }

        return string.Join(" | ", segments.Select(segment =>
        {
            var state = segment.State switch
            {
                SegmentState.Running => "R",
                SegmentState.Completed => "OK",
                SegmentState.Failed => "ERR",
                _ => "..."
            };

            return $"#{segment.Index + 1} {state} {segment.ProgressPercent:0}%";
        }));
    }
}
