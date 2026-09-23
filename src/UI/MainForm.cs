using System.ComponentModel;
using pbl4.Core;
using pbl4.Networking;
using pbl4.OSIntegration;
using pbl4.Persistence;

namespace pbl4.UI;

public sealed class MainForm : Form
{
    private readonly BindingList<DownloadItemViewModel> _downloads = [];
    private readonly BindingSource _bindingSource = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _eventSubscriptions = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastUiLogAt = new();
    private readonly SqliteDownloadRepository _repository;
    private readonly ProgressReporter _progressReporter = new();
    private readonly DownloadEngine _engine;

    private readonly TextBox _urlTextBox = new() { Text = "https://speed.hetzner.de/100MB.bin" };
    private readonly TextBox _outputTextBox = new() { Text = "downloads\\100MB.bin" };
    private readonly NumericUpDown _segmentsInput = new() { Minimum = 1, Maximum = 8, Value = 4 };
    private readonly NumericUpDown _limitInput = new() { Minimum = 0, Maximum = 1024 * 1024, Value = 0, Increment = 128 };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Engine local: ready" };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, AllowUserToAddRows = false };
    private readonly ListBox _logListBox = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
    private readonly Button _startButton = new();
    private readonly Button _pauseButton = new();
    private readonly Button _resumeButton = new();
    private readonly Button _cancelButton = new();

    public MainForm()
    {
        Text = "pbl4 Download Accelerator - WinForms";
        Width = 1180;
        Height = 720;
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var dbPath = Path.Combine(AppContext.BaseDirectory, "data", "pbl4-downloads.db");
        _repository = new SqliteDownloadRepository(dbPath);
        _engine = new DownloadEngine(
            new HttpRangeClient(new HttpRangeClientOptions { BufferSize = 1024 * 1024 }),
            new SegmentPlanner(),
            _repository,
            _progressReporter,
            new DownloadEngineOptions { DefaultConcurrency = 4, MaxConcurrency = 8, BufferSize = 1024 * 1024 });

        BuildLayout();
        _bindingSource.DataSource = _downloads;
        _grid.DataSource = _bindingSource;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await _repository.InitializeAsync();
        await RefreshAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeSubscriptions();
        base.OnFormClosed(e);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        Controls.Add(root);

        var inputPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5 };
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.Controls.Add(inputPanel, 0, 0);

        inputPanel.Controls.Add(CreateLabeledControl("URL", _urlTextBox), 0, 0);
        inputPanel.Controls.Add(CreateOutputPathControl(), 1, 0);
        inputPanel.Controls.Add(CreateLabeledControl("Threads", _segmentsInput), 2, 0);
        inputPanel.Controls.Add(CreateLabeledControl("Limit KB/s", _limitInput), 3, 0);

        ConfigureButton(_startButton, "Tai xuong", async () => await StartAsync(), width: 112);
        _startButton.Dock = DockStyle.Fill;
        _startButton.Height = 34;
        _startButton.Margin = new Padding(8, 28, 4, 8);
        inputPanel.Controls.Add(_startButton, 4, 0);

        var commandPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        root.Controls.Add(commandPanel, 0, 1);
        ConfigureButton(_pauseButton, "Tam dung", async () => await PauseSelectedAsync());
        ConfigureButton(_resumeButton, "Tiep tuc", async () => await ResumeSelectedAsync());
        ConfigureButton(_cancelButton, "Huy", async () => await CancelSelectedAsync());
        commandPanel.Controls.Add(_pauseButton);
        commandPanel.Controls.Add(_resumeButton);
        commandPanel.Controls.Add(_cancelButton);
        commandPanel.Controls.Add(_statusLabel);

        ConfigureGrid();
        UpdateCommandButtons();
        root.Controls.Add(_grid, 0, 2);

        var logPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        logPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logPanel.Controls.Add(new Label { Text = "Log", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        logPanel.Controls.Add(_logListBox, 0, 1);
        root.Controls.Add(logPanel, 0, 3);
    }

    private static Control CreateLabeledControl(string label, Control control)
    {
        control.Dock = DockStyle.Fill;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(4) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) }, 0, 0);
        panel.Controls.Add(control, 0, 1);
        return panel;
    }

    private Control CreateOutputPathControl()
    {
        var browseButton = new Button
        {
            Text = "Chon...",
            Dock = DockStyle.Fill,
            Width = 82
        };
        browseButton.Click += (_, _) => BrowseOutputPath();

        var inputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        _outputTextBox.Dock = DockStyle.Fill;
        inputRow.Controls.Add(_outputTextBox, 0, 0);
        inputRow.Controls.Add(browseButton, 1, 0);

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(4) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = "Output path", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) }, 0, 0);
        panel.Controls.Add(inputRow, 0, 1);
        return panel;
    }

    private void BrowseOutputPath()
    {
        var suggestedFileName = GuessFileNameFromUrl(_urlTextBox.Text.Trim());
        using var dialog = new SaveFileDialog
        {
            Title = "Chon noi luu file",
            FileName = suggestedFileName,
            Filter = "All files (*.*)|*.*",
            OverwritePrompt = true,
            AddExtension = false,
            InitialDirectory = Directory.Exists("downloads")
                ? Path.GetFullPath("downloads")
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputTextBox.Text = dialog.FileName;
        }
    }

    private static string GuessFileNameFromUrl(string rawUrl)
    {
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return "download.bin";
    }

    private static void ConfigureButton(Button button, string text, Func<Task> action, int width = 96)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 30;
        button.Margin = new Padding(0, 4, 8, 4);
        button.Click += async (_, _) => await action();
    }

    private void ConfigureGrid()
    {
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionChanged += (_, _) => UpdateCommandButtons();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "State", DataPropertyName = nameof(DownloadItemViewModel.State), Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File", DataPropertyName = nameof(DownloadItemViewModel.OutputPath), Width = 240 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Downloaded", DataPropertyName = nameof(DownloadItemViewModel.DownloadedText), Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Progress %", DataPropertyName = nameof(DownloadItemViewModel.ProgressPercent), Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time", DataPropertyName = nameof(DownloadItemViewModel.ElapsedText), Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Speed", DataPropertyName = nameof(DownloadItemViewModel.SpeedText), Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Active", DataPropertyName = nameof(DownloadItemViewModel.ActiveSegments), Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Segments", DataPropertyName = nameof(DownloadItemViewModel.SegmentCount), Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Message", DataPropertyName = nameof(DownloadItemViewModel.Message), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private async Task StartAsync()
    {
        try
        {
            if (!Uri.TryCreate(_urlTextBox.Text.Trim(), UriKind.Absolute, out var url))
            {
                MessageBox.Show(this, "URL khong hop le.", "pbl4", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var rawOutputPath = _outputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawOutputPath))
            {
                MessageBox.Show(this, "Hay chon noi luu file truoc khi tai.", "pbl4", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var outputPath = Path.GetFullPath(rawOutputPath);
            var bandwidthLimit = _limitInput.Value > 0 ? (long?)_limitInput.Value * 1024 : null;
            var job = await _engine.CreateAsync(new pbl4.Core.CreateDownloadRequest(url, outputPath, (int)_segmentsInput.Value, bandwidthLimit));
            var item = ToViewModel(job);
            item.StartedAt = DateTimeOffset.UtcNow;
            _downloads.Insert(0, item);
            _bindingSource.Position = 0;
            SubscribeToEvents(item.Id);
            UpdateCommandButtons();
            AddLog($"Created {item.Id} - {item.OutputPath}");

            _ = Task.Run(async () =>
            {
                try
                {
                    await _engine.StartAsync(job.Id, bandwidthLimit);
                }
                catch (Exception ex)
                {
                    BeginInvoke(() => AddLog($"Download failed: {ex.Message}"));
                }
            });
        }
        catch (Exception ex)
        {
            AddLog($"Start failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "pbl4", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var jobs = await _engine.ListAsync();
            var refreshedItems = jobs.Select(ToViewModel).ToList();

            _downloads.Clear();
            foreach (var item in refreshedItems)
            {
                _downloads.Add(item);
                if (item.State is "Running" or "Pending" or "Paused")
                {
                    SubscribeToEvents(item.Id);
                }
            }

            _statusLabel.Text = $"Engine local: ready - {_downloads.Count} job(s)";
            UpdateCommandButtons();
            AddLog($"Tai lai danh sach: {_downloads.Count} job(s).");
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Engine local: error";
            AddLog($"Refresh failed: {ex.Message}");
        }
    }

    private async Task PauseSelectedAsync()
    {
        var item = SelectedItem();
        if (item is null) return;
        await _engine.PauseAsync(item.Id);
        item.State = DownloadState.Paused.ToString();
        _bindingSource.ResetBindings(false);
        UpdateCommandButtons();
        AddLog($"Pause requested: {item.Id}");
    }

    private async Task ResumeSelectedAsync()
    {
        var item = SelectedItem();
        if (item is null) return;
        SubscribeToEvents(item.Id);
        _ = Task.Run(async () => await _engine.ResumeAsync(item.Id));
        item.State = DownloadState.Running.ToString();
        _bindingSource.ResetBindings(false);
        UpdateCommandButtons();
        AddLog($"Resume requested: {item.Id}");
    }

    private async Task CancelSelectedAsync()
    {
        var item = SelectedItem();
        if (item is null) return;
        await _engine.CancelAsync(item.Id);
        item.State = DownloadState.Canceled.ToString();
        _bindingSource.ResetBindings(false);
        UpdateCommandButtons();
        AddLog($"Cancel requested: {item.Id}");
    }

    private DownloadItemViewModel? SelectedItem()
        => _grid.CurrentRow?.DataBoundItem as DownloadItemViewModel;

    private void SubscribeToEvents(Guid jobId)
    {
        if (_eventSubscriptions.ContainsKey(jobId))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _eventSubscriptions[jobId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var reader = _engine.Subscribe(jobId);
                await foreach (var progress in reader.ReadAllAsync(cts.Token))
                {
                    BeginInvoke(() =>
                    {
                        var item = _downloads.FirstOrDefault(download => download.Id == progress.JobId);
                        if (item is null) return;
                        item.ApplyProgress(progress);
                        _bindingSource.ResetBindings(false);
                        UpdateCommandButtons();
                        AddProgressLog(progress);
                    });

                    if (progress.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _eventSubscriptions.Remove(jobId);
                cts.Dispose();
            }
        });
    }

    private static DownloadItemViewModel ToViewModel(DownloadJob job)
        => new()
        {
            Id = job.Id,
            Url = job.Url.ToString(),
            OutputPath = job.OutputPath,
            TotalBytes = job.TotalBytes,
            DownloadedBytes = job.DownloadedBytes,
            ProgressPercent = job.ProgressPercent,
            State = job.State.ToString(),
            SegmentCount = job.Segments.Count,
            ActiveSegments = 0,
            Message = ""
        };

    private void AddLog(string message)
    {
        _logListBox.Items.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
        while (_logListBox.Items.Count > 200)
        {
            _logListBox.Items.RemoveAt(_logListBox.Items.Count - 1);
        }
    }

    private void AddProgressLog(DownloadProgressSnapshot progress)
    {
        var important = progress.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled or DownloadState.Paused
                        || progress.ProgressPercent >= 100
                        || progress.Message.Contains("retry", StringComparison.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        if (!important &&
            _lastUiLogAt.TryGetValue(progress.JobId, out var lastLog) &&
            now - lastLog < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastUiLogAt[progress.JobId] = now;
        AddLog($"{progress.JobId}: {progress.State} {progress.ProgressPercent:0.##}% - {progress.Message}");
    }

    private void UpdateCommandButtons()
    {
        var item = SelectedItem();
        var state = item?.State;
        _pauseButton.Enabled = state == DownloadState.Running.ToString();
        _resumeButton.Enabled = state is "Paused" or "Failed";
        _cancelButton.Enabled = state is "Pending" or "Running" or "Paused" or "Failed";
    }

    private void DisposeSubscriptions()
    {
        foreach (var cts in _eventSubscriptions.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _eventSubscriptions.Clear();
    }
}
