namespace CodexUsageTray;

internal sealed partial class TrayApplicationContext : ApplicationContext
{
    private readonly AlertStateStore _alertStateStore = new();
    private readonly JsonExportController _jsonExport = new();
    private readonly StartupRegistration _startupRegistration = new();
    private readonly TrayNumberStore _trayNumbers = new(UsageSources.All);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<SourceRuntime> _sources = [];
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _startupItem = new("Start with Windows");
    private readonly ToolStripMenuItem _jsonExportItem = new("Enable export");
    private readonly ToolStripMenuItem _jsonExportMenu = new("JSON export");
    private readonly ToolStripMenuItem _trayNumbersMenu = new("Tray numbers");
    private readonly ToolStripMenuItem _claudePlanItem = new(ClaudePlanSwitch.MenuText);
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 300_000 };

    /// <summary>
    /// Creates the tray numbers, their shared menu, and the periodic refresh loop.
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
        _claudePlanItem.Checked = ClaudePlanSwitch.HasFiveHourLimit(_trayNumbers);
        _claudePlanItem.ToolTipText = "Shows or hides the Claude 5-hour tray number and its phone alert together.";
        _claudePlanItem.Click += async (_, _) =>
            await SwitchClaudePlanAsync(!ClaudePlanSwitch.HasFiveHourLimit(_trayNumbers));
        // The Claude 5-hour number can also be ticked on its own under Tray numbers.
        _menu.Opening += (_, _) => _claudePlanItem.Checked = ClaudePlanSwitch.HasFiveHourLimit(_trayNumbers);
        _menu.Items.AddRange(
            [refreshItem, _jsonExportMenu, _trayNumbersMenu, _claudePlanItem, _startupItem, new ToolStripSeparator(), exitItem]);

        InitializePhoneNotifications();

        foreach (UsageSource source in UsageSources.All)
        {
            SourceRuntime runtime = new(source);
            _sources.Add(runtime);
            foreach (LimitWindow window in source.Windows)
            {
                ToolStripMenuItem item = new(
                    $"{source.DisplayName} {UsageSource.WindowName(window)} ({DescribeFrame(source, window)})")
                {
                    Checked = _trayNumbers.IsShown(source, window),
                };
                item.Click += (_, _) => ToggleTrayNumber(runtime, window);
                runtime.NumberItems[window] = item;
                _trayNumbersMenu.DropDownItems.Add(item);
            }

            SyncTrayNumbers(runtime);
            ApplySnapshot(runtime);
        }

        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Shows or hides one tray number. A source with no numbers left stops being polled.
    /// </summary>
    /// <param name="runtime">The source that owns the window.</param>
    /// <param name="window">The window to show or hide.</param>
    private void ToggleTrayNumber(SourceRuntime runtime, LimitWindow window)
    {
        bool show = !_trayNumbers.IsShown(runtime.Source, window);
        bool last = !show && _trayNumbers.ShownCount <= 1;
        if (!_trayNumbers.TrySet(runtime.Source, window, show))
        {
            WarnTrayNumberUnchanged(last);
        }

        ApplyTrayNumber(runtime, window, refresh: show);
    }

    /// <summary>
    /// Switches between a Claude plan with a five-hour limit and one without: the Claude 5-hour
    /// tray number and its phone alert move together, and nothing else changes.
    /// </summary>
    /// <param name="hasFiveHourLimit">Whether the Claude plan has a five-hour limit.</param>
    private async Task SwitchClaudePlanAsync(bool hasFiveHourLimit)
    {
        SourceRuntime runtime = _sources.First(candidate => candidate.Source == UsageSources.Claude);
        bool last = !hasFiveHourLimit && _trayNumbers.ShownCount <= 1;
        if (!ClaudePlanSwitch.TrySwitch(
            _trayNumbers,
            _phoneNotifications.Settings,
            hasFiveHourLimit,
            out PhoneNotificationSettings alerts))
        {
            WarnTrayNumberUnchanged(last);
            ApplyTrayNumber(runtime, LimitWindow.FiveHour, refresh: false);
            return;
        }

        ApplyTrayNumber(runtime, LimitWindow.FiveHour, refresh: hasFiveHourLimit);
        if (alerts != _phoneNotifications.Settings)
        {
            await SavePhoneSettingsAsync(alerts);
        }
    }

    /// <summary>
    /// Explains why a tray number stayed as it was.
    /// </summary>
    /// <param name="last">Whether it is the last number shown, which cannot be hidden.</param>
    private static void WarnTrayNumberUnchanged(bool last) =>
        MessageBox.Show(
            last
                ? "Keep at least one tray number so this menu stays reachable."
                : "Could not save the tray number settings. The previous settings remain in effect.",
            "Codex Usage Tray",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);

    /// <summary>
    /// Brings one window's menu tick, its source's icons and their readings in line with the saved choice.
    /// </summary>
    /// <param name="runtime">The source that owns the window.</param>
    /// <param name="window">The window whose choice may have changed.</param>
    /// <param name="refresh">Whether to request a fresh reading for the source.</param>
    private void ApplyTrayNumber(SourceRuntime runtime, LimitWindow window, bool refresh)
    {
        runtime.NumberItems[window].Checked = _trayNumbers.IsShown(runtime.Source, window);
        _claudePlanItem.Checked = ClaudePlanSwitch.HasFiveHourLimit(_trayNumbers);
        SyncTrayNumbers(runtime);
        if (!_trayNumbers.IsActive(runtime.Source))
        {
            // Readings from a source that is no longer polled must not linger as if current.
            runtime.Snapshot = UsageSnapshot.Initial();
            runtime.Popup.Hide();
        }

        ApplySnapshot(runtime);
        if (refresh)
        {
            _ = RefreshAsync(runtime);
        }
    }

    /// <summary>
    /// Creates and removes tray icons so they match the numbers the user chose.
    /// </summary>
    /// <param name="runtime">The source whose icons to reconcile.</param>
    private void SyncTrayNumbers(SourceRuntime runtime)
    {
        foreach (LimitWindow window in runtime.Source.Windows)
        {
            bool shown = _trayNumbers.IsShown(runtime.Source, window);
            if (shown && !runtime.Numbers.ContainsKey(window))
            {
                NotifyIcon icon = new() { ContextMenuStrip = _menu };
                icon.MouseClick += (_, eventArgs) =>
                {
                    if (eventArgs.Button == MouseButtons.Left)
                    {
                        ShowPopup(runtime);
                    }
                };
                runtime.Numbers[window] = new TrayNumber(icon);
            }
            else if (!shown && runtime.Numbers.Remove(window, out TrayNumber? removed))
            {
                removed.Dispose();
            }
        }
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
    /// Shows one source's popup and requests a fresh snapshot for it.
    /// </summary>
    /// <param name="runtime">The source whose tray number was clicked.</param>
    private void ShowPopup(SourceRuntime runtime)
    {
        runtime.Popup.UpdateSnapshot(runtime.Snapshot, runtime.Refreshing, runtime.History.Points);
        runtime.Popup.ShowNearTaskbar();
        _ = RefreshAsync(runtime);
    }

    /// <summary>
    /// Refreshes every source that has a tray number. Sources refresh independently.
    /// </summary>
    private Task RefreshAsync() =>
        Task.WhenAll(_sources.Where(runtime => _trayNumbers.IsActive(runtime.Source)).Select(RefreshAsync));

    /// <summary>
    /// Fetches and applies one source's snapshot while preventing overlapping requests to it.
    /// A failure here never changes another source's readings.
    /// </summary>
    /// <param name="runtime">The source to refresh.</param>
    private async Task RefreshAsync(SourceRuntime runtime)
    {
        if (runtime.Refreshing || _shutdown.IsCancellationRequested || !_trayNumbers.IsActive(runtime.Source))
        {
            return;
        }

        runtime.Refreshing = true;
        runtime.Popup.UpdateSnapshot(runtime.Snapshot, refreshing: true, runtime.History.Points);
        try
        {
            UsageSnapshot snapshot = await runtime.Reader.FetchAsync(_shutdown.Token);
            if (!_trayNumbers.IsActive(runtime.Source))
            {
                return;
            }

            runtime.Snapshot = snapshot;
            _jsonExport.Save(snapshot, runtime.Source.ExportFileName, includeScoped: runtime.Source.ScopedRows > 0);
            runtime.History.Record(snapshot.Weekly, snapshot.RefreshedAt);
            ApplySnapshot(runtime);
            ShowLowUsageAlerts(runtime);
            _ = _phoneNotifications.ProcessAsync(runtime.Source, snapshot, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            runtime.Snapshot = UsageSnapshot.Error(exception.Message);
            ApplySnapshot(runtime);
        }
        finally
        {
            runtime.Refreshing = false;
            if (!_shutdown.IsCancellationRequested)
            {
                runtime.Popup.UpdateSnapshot(runtime.Snapshot, refreshing: false, runtime.History.Points);
            }
        }
    }

    /// <summary>
    /// Applies one source's snapshot to its tray numbers, tooltips, and popup.
    /// </summary>
    /// <param name="runtime">The source to display.</param>
    private static void ApplySnapshot(SourceRuntime runtime)
    {
        UsageSource source = runtime.Source;
        UsageSnapshot snapshot = runtime.Snapshot;
        foreach ((LimitWindow window, TrayNumber number) in runtime.Numbers)
        {
            ScopedLimit? scoped = window == LimitWindow.Scoped ? snapshot.TightestScoped() : null;
            LimitReading reading = window switch
            {
                LimitWindow.FiveHour => snapshot.FiveHour,
                LimitWindow.Weekly => snapshot.Weekly,
                _ => scoped?.Reading ?? new LimitReading(LimitState.Unavailable, null, null),
            };
            Color textColor = window == LimitWindow.FiveHour
                ? TrayIconRenderer.ResolveShortWindowColor(reading)
                : TrayIconRenderer.ResolveTextColor(UsagePopup.CalculateWeeklyLeft(reading, DateTimeOffset.Now));
            number.Show(
                TrayIconRenderer.Render(reading, textColor, ResolveFrame(source, window), source.FrameColor),
                FormatTooltip($"{source.DisplayName} {UsageSource.WindowName(window, scoped?.Label)}", reading));
        }

        runtime.Popup.UpdateSnapshot(snapshot, runtime.Refreshing, runtime.History.Points);
    }

    /// <summary>
    /// Chooses the frame that tells one tray number from another. The original single
    /// weekly number keeps its plain look; every other number is framed in its source's colour.
    /// </summary>
    /// <param name="source">The source that owns the number.</param>
    /// <param name="window">The window the number shows.</param>
    /// <returns>A circle for five-hour, a flagged square for scoped, and a square or nothing for weekly.</returns>
    internal static TrayFrame ResolveFrame(UsageSource source, LimitWindow window) => window switch
    {
        LimitWindow.FiveHour => TrayFrame.Circle,
        LimitWindow.Scoped => TrayFrame.Flagged,
        _ => source == UsageSources.Codex ? TrayFrame.None : TrayFrame.Square,
    };

    /// <summary>
    /// Describes a tray number's frame in words, so the menu doubles as a legend.
    /// </summary>
    /// <param name="source">The source that owns the number.</param>
    /// <param name="window">The window the number shows.</param>
    /// <returns>A short description such as "orange circle".</returns>
    internal static string DescribeFrame(UsageSource source, LimitWindow window) =>
        ResolveFrame(source, window) switch
        {
            TrayFrame.Square => $"{source.FrameColorName} square",
            TrayFrame.Circle => $"{source.FrameColorName} circle",
            TrayFrame.Flagged => $"{source.FrameColorName} square, filled corner",
            _ => "no frame",
        };

    /// <summary>
    /// Formats one short tray tooltip.
    /// </summary>
    /// <param name="name">The source and rate-limit window name.</param>
    /// <param name="reading">The normalized limit reading.</param>
    /// <returns>A tooltip below the Windows length limit.</returns>
    private static string FormatTooltip(string name, LimitReading reading) =>
        reading.State == LimitState.Available && reading.RemainingPercent is int remaining
            ? $"{name}: {Math.Clamp(remaining, 0, 100)}% remaining"
            : $"{name}: N/A";

    /// <summary>
    /// Shows each low-usage balloon at most once for its reset cycle.
    /// </summary>
    /// <param name="runtime">The source with a newly fetched snapshot.</param>
    private void ShowLowUsageAlerts(SourceRuntime runtime)
    {
        ShowLowUsageAlert(runtime, "five-hour", "5-hour", runtime.Snapshot.FiveHour);
        ShowLowUsageAlert(runtime, "weekly", "weekly", runtime.Snapshot.Weekly);
    }

    /// <summary>
    /// Shows one native warning balloon when persistence indicates it is due.
    /// </summary>
    /// <param name="runtime">The source that owns the limit.</param>
    /// <param name="windowKey">The persistence key for the limit window.</param>
    /// <param name="displayName">The user-facing limit-window name.</param>
    /// <param name="reading">The normalized limit reading.</param>
    private void ShowLowUsageAlert(
        SourceRuntime runtime,
        string windowKey,
        string displayName,
        LimitReading reading)
    {
        TrayNumber? owner = runtime.Numbers.Values.FirstOrDefault();
        if (owner is null || !_alertStateStore.ShouldAlert(runtime.Source.AlertKeyPrefix + windowKey, reading))
        {
            return;
        }

        owner.Icon.ShowBalloonTip(
            5_000,
            $"{runtime.Source.DisplayName} {displayName} limit low",
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
        _pollTimer.Dispose();
        foreach (SourceRuntime runtime in _sources)
        {
            runtime.Dispose();
        }

        _menu.Dispose();
        _shutdown.Dispose();
        base.ExitThreadCore();
    }

    /// <summary>
    /// One tray icon showing one quota window, together with the icon image it currently owns.
    /// </summary>
    private sealed class TrayNumber(NotifyIcon icon) : IDisposable
    {
        private Icon? _rendered;

        internal NotifyIcon Icon { get; } = icon;

        /// <summary>
        /// Replaces the displayed image and tooltip, releasing the previous image.
        /// </summary>
        /// <param name="rendered">The newly rendered icon, owned by this number from now on.</param>
        /// <param name="tooltip">The tooltip text.</param>
        internal void Show(Icon rendered, string tooltip)
        {
            Icon? previous = _rendered;
            _rendered = rendered;
            Icon.Icon = rendered;
            Icon.Text = tooltip;
            Icon.Visible = true;
            previous?.Dispose();
        }

        /// <summary>
        /// Hides the tray icon and releases its image.
        /// </summary>
        public void Dispose()
        {
            Icon.Visible = false;
            Icon.Dispose();
            _rendered?.Dispose();
        }
    }

    /// <summary>
    /// The live state of one usage source: its reader, readings, history, popup, and tray numbers.
    /// </summary>
    private sealed class SourceRuntime(UsageSource source) : IDisposable
    {
        internal UsageSource Source { get; } = source;

        internal IUsageReader Reader { get; } = source.CreateReader();

        internal UsageHistoryStore History { get; } = new(
            Path.Combine(PhoneNotificationFiles.DefaultDirectory, source.HistoryFileName));

        internal UsagePopup Popup { get; } = new(source.DisplayName, source.ScopedRows);

        internal Dictionary<LimitWindow, TrayNumber> Numbers { get; } = [];

        internal Dictionary<LimitWindow, ToolStripMenuItem> NumberItems { get; } = [];

        internal UsageSnapshot Snapshot { get; set; } = UsageSnapshot.Initial();

        internal bool Refreshing { get; set; }

        /// <summary>
        /// Hides this source's tray numbers and releases its popup.
        /// </summary>
        public void Dispose()
        {
            foreach (TrayNumber number in Numbers.Values)
            {
                number.Dispose();
            }

            Numbers.Clear();
            Popup.Dispose();
        }
    }
}
