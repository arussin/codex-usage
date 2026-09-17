namespace CodexUsageTray;

internal sealed partial class TrayApplicationContext : ApplicationContext
{
    private readonly CodexRateLimitReader _reader = new();
    private readonly AlertStateStore _alertStateStore = new();
    private readonly UsageHistoryStore _usageHistoryStore = new();
    private readonly JsonExportController _jsonExport = new();
    private readonly StartupRegistration _startupRegistration = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly NotifyIcon _weeklyIcon = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _startupItem = new("Start with Windows");
    private readonly ToolStripMenuItem _jsonExportItem = new("Enable export");
    private readonly ToolStripMenuItem _jsonExportMenu = new("JSON export");
    private readonly UsagePopup _popup = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 300_000 };
    private UsageSnapshot _snapshot = UsageSnapshot.Initial();
    private Icon? _weeklyRenderedIcon;
    private bool _refreshing;

    /// <summary>
    /// Creates the weekly tray icon, its menu, and the periodic refresh loop.
    /// </summary>
    internal TrayApplicationContext()
    {
        ToolStripMenuItem refreshItem = new("Refresh");
        ToolStripMenuItem exitItem = new("Exit");
        refreshItem.Click += async (_, _) => await RefreshAsync();
        exitItem.Click += (_, _) => ExitThread();
        _startupItem.CheckOnClick = true;
        _startupItem.Checked = ReadStartupEnabled();
        _startupItem.Click += (_, _) => SetStartupEnabled(_startupItem.Checked);
        ToolStripMenuItem chooseOutputItem = new("Choose output file…");
        chooseOutputItem.Click += (_, _) => ChooseJsonOutputFile();
        _jsonExportItem.Checked = _jsonExport.Settings.Enabled;
        _jsonExportItem.Click += (_, _) => ConfigureJsonExport(
            _jsonExport.Settings with { Enabled = !_jsonExport.Settings.Enabled });
        _jsonExportMenu.ToolTipText = _jsonExport.Settings.OutputPath;
        _jsonExportMenu.DropDownItems.AddRange([_jsonExportItem, chooseOutputItem]);
        _menu.Items.AddRange([refreshItem, _jsonExportMenu, _startupItem, new ToolStripSeparator(), exitItem]);

        InitializePhoneNotifications();

        _weeklyIcon.ContextMenuStrip = _menu;
        _weeklyIcon.Text = "Codex weekly: N/A";
        _weeklyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                ShowPopup();
            }
        };

        ApplySnapshot(_snapshot);
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Changes export preferences without publishing a cached snapshot as fresh data.
    /// </summary>
    private void ConfigureJsonExport(JsonExportSettings settings)
    {
        bool saved = _jsonExport.TryConfigure(settings);
        _jsonExportItem.Checked = _jsonExport.Settings.Enabled;
        _jsonExportMenu.ToolTipText = _jsonExport.Settings.OutputPath;
        if (!saved)
        {
            MessageBox.Show(
                settings.Enabled
                    ? "Could not save the export settings. The previous settings remain in effect."
                    : "JSON export is off for this session, but the preference could not be saved. Check it before the next launch.",
                "Codex Usage Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (settings.Enabled)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>
    /// Selects a JSON destination. Existing files are replaced only after a successful refresh.
    /// </summary>
    private void ChooseJsonOutputFile()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Choose JSON export file",
            Filter = "JSON files (*.json)|*.json",
            DefaultExt = "json",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Path.GetDirectoryName(_jsonExport.Settings.OutputPath),
            FileName = Path.GetFileName(_jsonExport.Settings.OutputPath),
        };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            ConfigureJsonExport(_jsonExport.Settings with { OutputPath = dialog.FileName });
        }
    }

    /// <summary>
    /// Reads the current per-user startup-registration state without preventing launch on registry errors.
    /// </summary>
    /// <returns><see langword="true"/> when startup registration is enabled.</returns>
    private bool ReadStartupEnabled()
    {
        try
        {
            return _startupRegistration.IsEnabled();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies the requested per-user startup-registration state.
    /// </summary>
    /// <param name="enabled">Whether the application should start with Windows.</param>
    private void SetStartupEnabled(bool enabled)
    {
        try
        {
            _startupRegistration.SetEnabled(enabled);
            _startupItem.Checked = _startupRegistration.IsEnabled();
        }
        catch (Exception exception)
        {
            _startupItem.Checked = ReadStartupEnabled();
            MessageBox.Show(
                $"Could not update Windows startup: {exception.Message}",
                "Codex Usage Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Shows the combined popup and requests a fresh snapshot.
    /// </summary>
    private void ShowPopup()
    {
        _popup.UpdateSnapshot(_snapshot, _refreshing, _usageHistoryStore.Points);
        _popup.ShowNearTaskbar();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Fetches and applies one snapshot while preventing overlapping requests.
    /// </summary>
    private async Task RefreshAsync()
    {
        if (_refreshing || _shutdown.IsCancellationRequested)
        {
            return;
        }

        _refreshing = true;
        _popup.UpdateSnapshot(_snapshot, refreshing: true, _usageHistoryStore.Points);
        try
        {
            _snapshot = await _reader.FetchAsync(_shutdown.Token);
            _jsonExport.Save(_snapshot);
            _usageHistoryStore.Record(_snapshot.Weekly, _snapshot.RefreshedAt);
            ApplySnapshot(_snapshot);
            ShowLowUsageAlerts(_snapshot);
            _ = _phoneNotifications.ProcessAsync(_snapshot, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _snapshot = UsageSnapshot.Error(exception.Message);
            ApplySnapshot(_snapshot);
        }
        finally
        {
            _refreshing = false;
            if (!_shutdown.IsCancellationRequested)
            {
                _popup.UpdateSnapshot(_snapshot, refreshing: false, _usageHistoryStore.Points);
            }
        }
    }

    /// <summary>
    /// Applies one snapshot to the weekly tray icon, tooltip, and popup.
    /// </summary>
    /// <param name="snapshot">The normalized snapshot to display.</param>
    private void ApplySnapshot(UsageSnapshot snapshot)
    {
        double? weeklyLeft = UsagePopup.CalculateWeeklyLeft(snapshot.Weekly, DateTimeOffset.Now);
        Icon weekly = TrayIconRenderer.Render(snapshot.Weekly, weeklyLeft);
        Icon? oldWeekly = _weeklyRenderedIcon;
        _weeklyRenderedIcon = weekly;
        _weeklyIcon.Icon = weekly;
        _weeklyIcon.Visible = true;
        oldWeekly?.Dispose();
        _weeklyIcon.Text = FormatTooltip("Weekly", snapshot.Weekly);
        _popup.UpdateSnapshot(snapshot, _refreshing, _usageHistoryStore.Points);
    }

    /// <summary>
    /// Formats one short tray tooltip.
    /// </summary>
    /// <param name="name">The rate-limit window name.</param>
    /// <param name="reading">The normalized limit reading.</param>
    /// <returns>A tooltip below the Windows length limit.</returns>
    private static string FormatTooltip(string name, LimitReading reading) =>
        reading.State == LimitState.Available && reading.RemainingPercent is int remaining
            ? $"Codex {name}: {Math.Clamp(remaining, 0, 100)}% remaining"
            : $"Codex {name}: N/A";

    /// <summary>
    /// Shows each low-usage balloon at most once for its reset cycle.
    /// </summary>
    /// <param name="snapshot">The newly fetched usage snapshot.</param>
    private void ShowLowUsageAlerts(UsageSnapshot snapshot)
    {
        ShowLowUsageAlert(_weeklyIcon, "five-hour", "5-hour", snapshot.FiveHour);
        ShowLowUsageAlert(_weeklyIcon, "weekly", "weekly", snapshot.Weekly);
    }

    /// <summary>
    /// Shows one native warning balloon when persistence indicates it is due.
    /// </summary>
    /// <param name="icon">The owning tray icon.</param>
    /// <param name="windowKey">The persistence key for the limit window.</param>
    /// <param name="displayName">The user-facing limit-window name.</param>
    /// <param name="reading">The normalized limit reading.</param>
    private void ShowLowUsageAlert(
        NotifyIcon icon,
        string windowKey,
        string displayName,
        LimitReading reading)
    {
        if (!_alertStateStore.ShouldAlert(windowKey, reading))
        {
            return;
        }

        icon.ShowBalloonTip(
            5_000,
            $"Codex {displayName} limit low",
            $"{reading.RemainingPercent}% remains until this limit resets.",
            ToolTipIcon.Warning);
    }

    /// <summary>
    /// Stops polling, hides the tray icons, and releases all owned resources.
    /// </summary>
    protected override void ExitThreadCore()
    {
        _pollTimer.Stop();
        _shutdown.Cancel();
        _phoneNotifications.Dispose();
        _weeklyIcon.Visible = false;
        _pollTimer.Dispose();
        _popup.Dispose();
        _weeklyIcon.Dispose();
        _menu.Dispose();
        _weeklyRenderedIcon?.Dispose();
        _shutdown.Dispose();
        base.ExitThreadCore();
    }
}
