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
    private string _message = "";

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

    public string Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    public string DownloadedText => $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes)}";

    public string SpeedText => $"{FormatBytes((long)SpeedBytesPerSecond)}/s";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyProgress(DownloadProgressSnapshot progress)
    {
        State = progress.State.ToString();
        DownloadedBytes = progress.DownloadedBytes;
        TotalBytes = progress.TotalBytes;
        ProgressPercent = Math.Round(progress.ProgressPercent, 2);
        SpeedBytesPerSecond = progress.SpeedBytesPerSecond;
        ActiveSegments = progress.ActiveSegments;
        Message = progress.Message;
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
}
