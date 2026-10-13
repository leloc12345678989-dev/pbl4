using System.ComponentModel;
using System.Net.Http;
using System.Net.Sockets;
using pbl4.Core;
using pbl4.Networking;
using pbl4.OSIntegration;
using pbl4.Persistence;

namespace pbl4.UI;

public sealed class MainForm : Form
{
    private static readonly string[] DemoDownloadUrls =
    [
        "http://speedtest.tele2.net/100MB.zip",
        "http://speedtest.tele2.net/1GB.zip",
        "http://speedtest.tele2.net/10MB.zip",
        "https://speedtest.singapore.linode.com/100MB-singapore.bin",
        "https://speedtest.dallas.linode.com/100MB-dallas.bin",
        "https://speedtest.newark.linode.com/100MB-newark.bin",
        "https://proof.ovh.net/files/100Mb.dat"
    ];

    private readonly BindingList<DownloadItemViewModel> _downloads = [];
    private readonly BindingSource _bindingSource = new();
    private readonly Dictionary<Guid, DownloadItemViewModel> _downloadById = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _eventSubscriptions = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastUiLogAt = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastUiProgressAt = new();
    private readonly object _uiThrottleLock = new();
    private readonly IDownloadRepository _repository;
    private readonly ProgressReporter _progressReporter = new();
    private readonly DownloadEngine _engine;

    private readonly ComboBox _urlTextBox = new() { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly TextBox _outputTextBox = new() { Text = "downloads\\100MB.zip" };
    private readonly NumericUpDown _segmentsInput = new() { Minimum = 1, Maximum = 8, Value = 4 };
    private readonly NumericUpDown _limitInput = new() { Minimum = 0, Maximum = 1024 * 1024, Value = 0, Increment = 128 };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Engine local: ready" };
    private readonly DataGridView _grid = new BufferedDataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, AllowUserToAddRows = false };
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
        ConfigureUrlPresets();

        _repository = BuildRepository();
        _engine = new DownloadEngine(
            new HttpRangeClient(new HttpRangeClientOptions { BufferSize = 1024 * 1024, MaxConnectionsPerServer = 8 }),
            new SegmentPlanner(),
            _repository,
            _progressReporter,
            new DownloadEngineOptions
            {
                DefaultConcurrency = 4,
                MaxConcurrency = 8,
                BufferSize = 1024 * 1024,
                MaxRetryAttempts = 10,
                InitialRetryDelay = TimeSpan.FromSeconds(2)
            });

        BuildLayout();
        _bindingSource.DataSource = _downloads;
        _grid.DataSource = _bindingSource;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            await _repository.InitializeAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "SQL Server: connection error";
            AddLog($"SQL Server failed: {ex.Message}");
            MessageBox.Show(
                this,
                $"Khong ket noi duoc SQL Server.\n\n{ex.Message}",
                "pbl4",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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

    private void ConfigureUrlPresets()
    {
        _urlTextBox.Items.AddRange(DemoDownloadUrls);
        _urlTextBox.Text = DemoDownloadUrls[0];
    }

    private static IDownloadRepository BuildRepository()
    {
        var provider = Environment.GetEnvironmentVariable("PBL4_REPOSITORY");
        if (provider?.Equals("sqlserver", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new SqlServerDownloadRepository(BuildSqlServerConnectionString());
        }

        var configuredPath = Environment.GetEnvironmentVariable("PBL4_SQLITE_PATH");
        var databasePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "pbl4-downloads.db")
            : configuredPath;

        return new SqliteDownloadRepository(databasePath);
    }

    private static string BuildSqlServerConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("PBL4_SQLSERVER_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return "Server=.;Database=pbl4_downloads;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;";
    }

    private void ConfigureGrid()
    {
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.ShowCellToolTips = true;
        _grid.SelectionChanged += (_, _) => UpdateCommandButtons();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "State", DataPropertyName = nameof(DownloadItemViewModel.State), Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File", DataPropertyName = nameof(DownloadItemViewModel.OutputPath), Width = 240 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Downloaded", DataPropertyName = nameof(DownloadItemViewModel.DownloadedText), Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Progress %", DataPropertyName = nameof(DownloadItemViewModel.ProgressPercent), Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time", DataPropertyName = nameof(DownloadItemViewModel.ElapsedText), Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Speed", DataPropertyName = nameof(DownloadItemViewModel.SpeedText), Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Active", DataPropertyName = nameof(DownloadItemViewModel.ActiveSegments), Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Segments", DataPropertyName = nameof(DownloadItemViewModel.SegmentCount), Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Threads", DataPropertyName = nameof(DownloadItemViewModel.ThreadStatusText), Width = 280 });
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
            AddDownloadItem(item, insertAtTop: true);
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
            var message = BuildStartErrorMessage(ex);
            AddLog($"Start failed: {message}");
            MessageBox.Show(this, message, "pbl4", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var jobs = await _engine.ListAsync();
            var refreshedItems = new List<DownloadItemViewModel>(jobs.Count);
            foreach (var job in jobs)
            {
                var item = ToViewModel(job);
                if (job.State is DownloadState.Running)
                {
                    await _repository.UpdateJobStateAsync(job.Id, DownloadState.Paused);
                    item.State = DownloadState.Paused.ToString();
                    item.Message = "Da tam dung sau khi mo lai app.";
                }

                refreshedItems.Add(item);
            }

            _downloadById.Clear();
            _downloads.Clear();
            foreach (var item in refreshedItems)
            {
                AddDownloadItem(item, insertAtTop: false);
                if (item.State is "Running")
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
        UpdateCommandButtons();
        AddLog($"Resume requested: {item.Id}");
    }

    private async Task CancelSelectedAsync()
    {
        var item = SelectedItem();
        if (item is null) return;
        await _engine.CancelAsync(item.Id);
        item.State = DownloadState.Canceled.ToString();
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
                    if (!ShouldUpdateUi(progress))
                    {
                        continue;
                    }

                    BeginInvoke(() =>
                    {
                        if (!_downloadById.TryGetValue(progress.JobId, out var item))
                        {
                            return;
                        }

                        item.ApplyProgress(progress);
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
                _lastUiLogAt.Remove(jobId);
                _lastUiProgressAt.Remove(jobId);
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
            ThreadStatusText = FormatInitialSegmentStatus(job.Segments),
            Message = ""
        };

    private static string BuildStartErrorMessage(Exception ex)
    {
        if (ex is HttpRequestException { InnerException: SocketException socketException } &&
            socketException.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
        {
            return "Khong phan giai duoc ten mien cua URL. Hay kiem tra mang/DNS hoac thu URL khac, vi app chua ket noi duoc toi server tai file.";
        }

        return ex.Message;
    }

    private void AddLog(string message)
    {
        _logListBox.BeginUpdate();
        try
        {
            _logListBox.Items.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
            while (_logListBox.Items.Count > 200)
            {
                _logListBox.Items.RemoveAt(_logListBox.Items.Count - 1);
            }
        }
        finally
        {
            _logListBox.EndUpdate();
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

    private bool ShouldUpdateUi(DownloadProgressSnapshot progress)
    {
        var important = progress.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled or DownloadState.Paused
                        || progress.ProgressPercent >= 100
                        || progress.Message.Contains("retry", StringComparison.OrdinalIgnoreCase);

        if (important)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        lock (_uiThrottleLock)
        {
            if (_lastUiProgressAt.TryGetValue(progress.JobId, out var lastUpdate) &&
                now - lastUpdate < TimeSpan.FromMilliseconds(200))
            {
                return false;
            }

            _lastUiProgressAt[progress.JobId] = now;
            return true;
        }
    }

    private static string FormatInitialSegmentStatus(IReadOnlyList<DownloadSegment> segments)
        => string.Join(" | ", segments.Select(segment =>
        {
            var state = segment.State switch
            {
                SegmentState.Running => "R",
                SegmentState.Completed => "OK",
                SegmentState.Failed => "ERR",
                _ => "..."
            };

            var percent = segment.Length <= 0 ? 0 : segment.DownloadedBytes * 100.0 / segment.Length;
            return $"#{segment.Index + 1} {state} {percent:0}%";
        }));

    private void UpdateCommandButtons()
    {
        var item = SelectedItem();
        var state = item?.State;
        _pauseButton.Enabled = state == DownloadState.Running.ToString();
        _resumeButton.Enabled = state is "Paused" or "Failed";
        _cancelButton.Enabled = state is "Pending" or "Running" or "Paused" or "Failed";
    }

    private void AddDownloadItem(DownloadItemViewModel item, bool insertAtTop)
    {
        _downloadById[item.Id] = item;
        if (insertAtTop)
        {
            _downloads.Insert(0, item);
            return;
        }

        _downloads.Add(item);
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

    private sealed class BufferedDataGridView : DataGridView
    {
        public BufferedDataGridView()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }
}
