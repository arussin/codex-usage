using System.Text;

namespace CodexUsageTray;

internal sealed class UsagePopup : Form
{
    private readonly Label _fiveHourValue = new();
    private readonly ProgressBar _fiveHourProgress = new();
    private readonly Label _fiveHourReset = new();
    private readonly Label _weeklyValue = new();
    private readonly ProgressBar _weeklyProgress = new();
    private readonly Label _weeklyDailyRates = new();
    private readonly Label _weeklyEndOfDay = new();
    private readonly Label _weeklyLeft = new();
    private readonly Label _weeklyReset = new();
    private readonly Panel _historyPlot = new();
    private readonly Button _copyPlotButton = new();
    private readonly Button _downloadHistoryButton = new();
    private readonly CheckBox _plotAllHistoryToggle = new();
    private readonly Label _status = new();
    private readonly Label _error = new();
    private readonly System.Windows.Forms.Timer _countdownTimer = new() { Interval = 1_000 };
    private UsageSnapshot _snapshot = UsageSnapshot.Initial();
    private IReadOnlyList<UsageHistoryPoint> _history = [];
    private bool _refreshing;

    /// <summary>
    /// Creates the compact combined usage popup.
    /// </summary>
    internal UsagePopup()
    {
        Text = "Codex usage";
        ClientSize = new Size(360, 515);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        Label title = new()
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(12, 12),
            Text = "Codex limits",
        };
        ConfigureValueLabel(_fiveHourValue, "5-hour", 45);
        ConfigureProgress(_fiveHourProgress, 65);
        ConfigureResetLabel(_fiveHourReset, 91);
        ConfigureValueLabel(_weeklyValue, "Weekly", 121);
        ConfigureProgress(_weeklyProgress, 141);
        _historyPlot.SetBounds(12, 167, 336, 180);
        _historyPlot.BackColor = SystemColors.Window;
        _historyPlot.BorderStyle = BorderStyle.FixedSingle;
        _historyPlot.Paint += (_, eventArgs) =>
            DrawHistory(
                eventArgs.Graphics,
                _historyPlot.ClientRectangle,
                _history,
                _snapshot.Weekly,
                _plotAllHistoryToggle.Checked);
        _copyPlotButton.SetBounds(12, 355, 100, 25);
        _copyPlotButton.Text = "Copy PNG";
        _copyPlotButton.Click += (_, _) => CopyPlotToClipboard();
        _downloadHistoryButton.SetBounds(120, 355, 112, 25);
        _downloadHistoryButton.Text = "Download CSV";
        _downloadHistoryButton.Click += (_, _) => DownloadHistoryCsv();
        _plotAllHistoryToggle.SetBounds(240, 355, 108, 25);
        _plotAllHistoryToggle.Appearance = Appearance.Button;
        _plotAllHistoryToggle.Text = "All data";
        _plotAllHistoryToggle.TextAlign = ContentAlignment.MiddleCenter;
        _plotAllHistoryToggle.CheckedChanged += (_, _) => _historyPlot.Invalidate();
        ConfigureResetLabel(_weeklyDailyRates, 388);
        ConfigureResetLabel(_weeklyEndOfDay, 407);
        ConfigureResetLabel(_weeklyLeft, 426);
        ConfigureResetLabel(_weeklyReset, 445);

        _status.SetBounds(12, 473, 336, 18);
        _status.ForeColor = SystemColors.GrayText;
        _error.SetBounds(12, 492, 336, 20);
        _error.AutoEllipsis = true;
        _error.ForeColor = Color.Firebrick;

        Controls.AddRange([
            title,
            _fiveHourValue,
            _fiveHourProgress,
            _fiveHourReset,
            _weeklyValue,
            _weeklyProgress,
            _weeklyDailyRates,
            _weeklyEndOfDay,
            _weeklyLeft,
            _weeklyReset,
            _historyPlot,
            _copyPlotButton,
            _downloadHistoryButton,
            _plotAllHistoryToggle,
            _status,
            _error,
        ]);

        _countdownTimer.Tick += (_, _) => Render();
        _countdownTimer.Start();
        Deactivate += (_, _) => Hide();
        Render();
    }

    /// <summary>
    /// Updates the snapshot and refresh state shown by the popup.
    /// </summary>
    /// <param name="snapshot">The latest normalized usage snapshot.</param>
    /// <param name="refreshing">Whether a refresh is currently running.</param>
    /// <param name="history">The retained weekly remaining-percentage samples.</param>
    internal void UpdateSnapshot(
        UsageSnapshot snapshot,
        bool refreshing,
        IReadOnlyList<UsageHistoryPoint> history)
    {
        _snapshot = snapshot;
        _refreshing = refreshing;
        _history = history.ToArray();
        _historyPlot.Invalidate();
        Render();
    }

    /// <summary>
    /// Positions and shows the popup near the notification area selected by the cursor.
    /// </summary>
    internal void ShowNearTaskbar()
    {
        Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(workingArea.Right - Width - 8, workingArea.Bottom - Height - 8);
        Show();
        Activate();
    }

    /// <summary>
    /// Hides instead of disposing the reusable popup when the user closes it.
    /// </summary>
    /// <param name="eventArgs">The form-closing event arguments.</param>
    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(eventArgs);
    }

    /// <summary>
    /// Releases the countdown timer owned by the popup.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdownTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Configures a heading/value label for one rate-limit window.
    /// </summary>
    /// <param name="label">The label to configure.</param>
    /// <param name="text">The window heading.</param>
    /// <param name="top">The top coordinate.</param>
    private static void ConfigureValueLabel(Label label, string text, int top)
    {
        label.SetBounds(12, top, 336, 18);
        label.Text = text;
    }

    /// <summary>
    /// Configures a progress bar for one rate-limit window.
    /// </summary>
    /// <param name="progress">The progress bar to configure.</param>
    /// <param name="top">The top coordinate.</param>
    private static void ConfigureProgress(ProgressBar progress, int top)
    {
        progress.SetBounds(12, top, 336, 18);
        progress.Maximum = 100;
    }

    /// <summary>
    /// Configures a reset-countdown label for one rate-limit window.
    /// </summary>
    /// <param name="label">The label to configure.</param>
    /// <param name="top">The top coordinate.</param>
    private static void ConfigureResetLabel(Label label, int top)
    {
        label.SetBounds(12, top, 336, 18);
        label.ForeColor = SystemColors.GrayText;
    }

    /// <summary>
    /// Draws the retained weekly remaining-percentage samples as a compact line plot.
    /// </summary>
    /// <param name="graphics">The plot drawing surface.</param>
    /// <param name="bounds">The available plot bounds.</param>
    /// <param name="history">The retained weekly samples.</param>
    /// <param name="reading">The current weekly limit reading.</param>
    /// <param name="includeAllHistory">Whether every retained sample should be plotted.</param>
    private static void DrawHistory(
        Graphics graphics,
        Rectangle bounds,
        IReadOnlyList<UsageHistoryPoint> history,
        LimitReading reading,
        bool includeAllHistory)
    {
        graphics.Clear(SystemColors.Window);
        Rectangle titleBounds = new(42, 2, bounds.Width - 46, 16);
        TextRenderer.DrawText(
            graphics,
            "%-left",
            SystemFonts.MessageBoxFont,
            new Rectangle(2, 2, 36, 16),
            SystemColors.GrayText,
            TextFormatFlags.Right | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            graphics,
            "Magenta: remaining / refresh",
            SystemFonts.MessageBoxFont,
            titleBounds,
            SystemColors.ControlText,
            TextFormatFlags.Left | TextFormatFlags.NoPadding);

        DateTimeOffset cycleEnd = reading.ResetsAt ?? DateTimeOffset.Now;
        DateTimeOffset cycleStart = cycleEnd.AddMinutes(-CodexRateLimitReader.WeeklyMinutes);
        UsageHistoryPoint[] selectedHistory = SelectPlotHistory(
            history,
            cycleStart,
            cycleEnd,
            includeAllHistory);
        DateTimeOffset plotStart = includeAllHistory && selectedHistory.Length > 0
            ? selectedHistory[0].RecordedAt
            : cycleStart;
        DateTimeOffset plotEnd = includeAllHistory && selectedHistory.Length > 0
            ? selectedHistory[^1].RecordedAt > DateTimeOffset.Now
                ? selectedHistory[^1].RecordedAt
                : DateTimeOffset.Now
            : cycleEnd;
        if (plotEnd <= plotStart)
        {
            plotEnd = plotStart.AddMinutes(1);
        }

        Rectangle plot = new(42, 20, Math.Max(1, bounds.Width - 48), Math.Max(1, bounds.Height - 60));
        Rectangle leftPeriodBounds = new(plot.Left, plot.Bottom + 18, plot.Width / 2, 16);
        Rectangle rightPeriodBounds = new(
            plot.Left + (plot.Width / 2),
            plot.Bottom + 18,
            plot.Width - (plot.Width / 2),
            16);
        TextRenderer.DrawText(
            graphics,
            plotStart.ToLocalTime().ToString("dd MMM HH:mm"),
            SystemFonts.MessageBoxFont,
            leftPeriodBounds,
            SystemColors.GrayText,
            TextFormatFlags.Left | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            graphics,
            plotEnd.ToLocalTime().ToString("dd MMM HH:mm"),
            SystemFonts.MessageBoxFont,
            rightPeriodBounds,
            SystemColors.GrayText,
            TextFormatFlags.Right | TextFormatFlags.NoPadding);

        using Pen gridPen = new(SystemColors.ControlLight);
        using Font axisFont = new("Segoe UI", 7, FontStyle.Regular, GraphicsUnit.Point);
        for (int percent = 0; percent <= 100; percent += 10)
        {
            int y = plot.Bottom - (int)Math.Round(percent / 100d * plot.Height);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            TextRenderer.DrawText(
                graphics,
                $"{percent}%",
                axisFont,
                new Rectangle(2, y - 7, 36, 14),
                SystemColors.GrayText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        for (int day = 0; day <= 7; day++)
        {
            int x = plot.Left + (int)Math.Round(day / 7d * plot.Width);
            DateTimeOffset tickTime = plotStart.AddSeconds((plotEnd - plotStart).TotalSeconds / 7d * day)
                .ToLocalTime();
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            TextRenderer.DrawText(
                graphics,
                tickTime.ToString("dd"),
                axisFont,
                new Rectangle(x - 16, plot.Bottom + 2, 32, 14),
                SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }

        using Pen pacingPen = new(Color.Black, 1);
        double plotSeconds = (plotEnd - plotStart).TotalSeconds;
        float pacingStartX = plot.Left + ((float)((cycleStart - plotStart).TotalSeconds / plotSeconds) * plot.Width);
        float pacingEndX = plot.Left + ((float)((cycleEnd - plotStart).TotalSeconds / plotSeconds) * plot.Width);
        graphics.SetClip(plot);
        graphics.DrawLine(pacingPen, pacingStartX, plot.Top, pacingEndX, plot.Bottom);
        graphics.ResetClip();
        TextRenderer.DrawText(
            graphics,
            "100/7% per day",
            axisFont,
            new Rectangle(plot.Left + 4, plot.Top + 3, 82, 14),
            Color.Black,
            TextFormatFlags.Left | TextFormatFlags.NoPadding);

        if (selectedHistory.Length == 0
            && reading.State == LimitState.Available
            && reading.RemainingPercent is int currentRemaining)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset sampleTime = now < cycleStart
                ? cycleStart
                : now > cycleEnd
                    ? cycleEnd
                    : now;
            selectedHistory =
            [
                new UsageHistoryPoint(sampleTime, Math.Clamp(currentRemaining, 0, 100)),
            ];
        }

        if (selectedHistory.Length == 0)
        {
            TextRenderer.DrawText(
                graphics,
                "No samples yet",
                SystemFonts.MessageBoxFont,
                plot,
                SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        double? pacingLeft = CalculateWeeklyLeft(reading, DateTimeOffset.Now);
        string pacingText = pacingLeft is double value
            ? FormattableString.Invariant($"Left: {value:F1}%")
            : "Left: N/A";
        TextRenderer.DrawText(
            graphics,
            pacingText,
            SystemFonts.MessageBoxFont,
            titleBounds,
            TrayIconRenderer.ResolveTextColor(pacingLeft),
            TextFormatFlags.Right | TextFormatFlags.NoPadding);

        UsageHistoryPoint[] plotHistory =
            includeAllHistory
                ? selectedHistory
                :
                [
                    new UsageHistoryPoint(cycleStart, 100),
                    .. selectedHistory,
                ];
        PointF[] points = new PointF[plotHistory.Length];
        for (int index = 0; index < plotHistory.Length; index++)
        {
            double elapsedSeconds = (plotHistory[index].RecordedAt - plotStart).TotalSeconds;
            float x = plot.Left + ((float)(elapsedSeconds / plotSeconds) * plot.Width);
            int remaining = Math.Clamp(plotHistory[index].RemainingPercent, 0, 100);
            float y = plot.Top + ((100 - remaining) / 100f * plot.Height);
            points[index] = new PointF(x, y);
        }

        using Pen linePen = new(Color.Magenta, 2);
        if (points.Length > 1)
        {
            graphics.DrawLines(linePen, points);
        }
        else
        {
            graphics.FillEllipse(Brushes.Magenta, points[0].X - 2, points[0].Y - 2, 4, 4);
        }

        DateTimeOffset zeroAt = selectedHistory[^1].RemainingPercent == 0
            ? cycleEnd
            : PredictZeroAt(plotHistory) ?? cycleEnd;
        float predictionX = plot.Left
            + ((float)((zeroAt - plotStart).TotalSeconds / plotSeconds) * plot.Width);
        using Pen predictionPen = new(Color.OrangeRed, 1)
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
        };
        graphics.SetClip(plot);
        graphics.DrawLine(
            predictionPen,
            points[^1],
            new PointF(predictionX, plot.Bottom));
        graphics.ResetClip();

        if (reading.RemainingPercent is int displayedRemaining)
        {
            int current = Math.Clamp(displayedRemaining, 0, 100);
            int currentY = plot.Top + (int)Math.Round((100 - current) / 100d * plot.Height);
            using Pen currentPen = new(Color.Magenta, 1)
            {
                DashStyle = System.Drawing.Drawing2D.DashStyle.Dot,
            };
            graphics.DrawLine(currentPen, plot.Left, currentY, plot.Right, currentY);
            TextRenderer.DrawText(
                graphics,
                $"Current {current}%",
                axisFont,
                new Rectangle(plot.Right - 72, currentY - 14, 70, 14),
                Color.Magenta,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }

        DateTimeOffset markerTime = DateTimeOffset.Now;
        double markerPosition = Math.Clamp(
            (markerTime - plotStart).TotalSeconds / plotSeconds,
            0,
            1);
        int markerX = plot.Left + (int)Math.Round(markerPosition * plot.Width);
        using Pen markerPen = new(Color.Black, 1);
        graphics.DrawLine(markerPen, markerX, plot.Top, markerX, plot.Bottom);
        int markerLabelX = Math.Clamp(markerX - 30, plot.Left, plot.Right - 60);
        TextRenderer.DrawText(
            graphics,
            $"Today {markerTime.ToLocalTime():dd}",
            axisFont,
            new Rectangle(markerLabelX, plot.Top + 1, 60, 14),
            Color.Black,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>
    /// Selects the saved samples to draw in either weekly-window or all-history mode.
    /// </summary>
    /// <param name="history">The retained weekly samples.</param>
    /// <param name="windowStart">The current weekly window start.</param>
    /// <param name="windowEnd">The current weekly window end.</param>
    /// <param name="includeAllHistory">Whether every retained sample should be selected.</param>
    /// <returns>The selected samples in chronological order.</returns>
    internal static UsageHistoryPoint[] SelectPlotHistory(
        IReadOnlyList<UsageHistoryPoint> history,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        bool includeAllHistory)
    {
        IEnumerable<UsageHistoryPoint> selected = includeAllHistory
            ? history
            : history.Where(point => point.RecordedAt >= windowStart && point.RecordedAt <= windowEnd);
        return selected
            .OrderBy(point => point.RecordedAt)
            .ToArray();
    }

    /// <summary>
    /// Builds a CSV export of retained weekly remaining-percentage samples.
    /// </summary>
    /// <param name="history">Chronologically ordered weekly remaining-percentage samples.</param>
    /// <returns>The CSV document.</returns>
    internal static string BuildHistoryCsv(IReadOnlyList<UsageHistoryPoint> history)
    {
        StringBuilder csv = new("recordedAt,remainingPercent");
        csv.AppendLine();
        for (int index = 0; index < history.Count; index++)
        {
            csv.Append(FormattableString.Invariant(
                $"{history[index].RecordedAt:O},{history[index].RemainingPercent}"));
            csv.AppendLine();
        }

        return csv.ToString();
    }

    /// <summary>
    /// Copies the rendered weekly graph to the Windows clipboard as PNG data.
    /// </summary>
    private void CopyPlotToClipboard()
    {
        try
        {
            using Bitmap bitmap = new(_historyPlot.Width, _historyPlot.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                DrawHistory(
                    graphics,
                    new Rectangle(Point.Empty, bitmap.Size),
                    _history,
                    _snapshot.Weekly,
                    _plotAllHistoryToggle.Checked);
            }

            using MemoryStream png = new();
            bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            png.Position = 0;
            DataObject clipboardData = new();
            clipboardData.SetData(DataFormats.Bitmap, true, bitmap);
            clipboardData.SetData("PNG", false, png);
            Clipboard.SetDataObject(clipboardData, true);
        }
        catch (Exception exception) when (
            exception is IOException
            or ArgumentException
            or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(
                $"Could not copy the graph: {exception.Message}",
                "Codex Usage Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Saves the retained weekly time-series data to a user-selected CSV file.
    /// </summary>
    private void DownloadHistoryCsv()
    {
        using SaveFileDialog dialog = new()
        {
            AddExtension = true,
            DefaultExt = "csv",
            FileName = $"codex-usage-history-{DateTime.Now:yyyy-MM-dd}.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Download Codex usage history",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, BuildHistoryCsv(_history));
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"Could not save the history: {exception.Message}",
                "Codex Usage Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Predicts when weekly remaining usage reaches zero from its average observed decline.
    /// </summary>
    /// <param name="history">Chronologically ordered weekly remaining-percentage samples.</param>
    /// <returns>The predicted zero timestamp, or null without a measurable decline.</returns>
    internal static DateTimeOffset? PredictZeroAt(IReadOnlyList<UsageHistoryPoint> history)
    {
        if (history.Count < 2)
        {
            return null;
        }

        UsageHistoryPoint latest = history[^1];
        UsageHistoryPoint? baseline = history.FirstOrDefault(
            point => point.RecordedAt < latest.RecordedAt
                && point.RemainingPercent > latest.RemainingPercent);
        if (baseline is null)
        {
            return null;
        }

        double elapsedSeconds = (latest.RecordedAt - baseline.RecordedAt).TotalSeconds;
        double usedPercent = baseline.RemainingPercent - latest.RemainingPercent;
        double secondsToZero = elapsedSeconds / usedPercent * latest.RemainingPercent;
        return latest.RecordedAt.AddSeconds(secondsToZero);
    }

    /// <summary>
    /// Renders the current snapshot and live reset countdowns.
    /// </summary>
    private void Render()
    {
        RenderLimit(_fiveHourValue, _fiveHourProgress, _fiveHourReset, "5-hour", _snapshot.FiveHour);
        RenderLimit(_weeklyValue, _weeklyProgress, _weeklyReset, "Weekly", _snapshot.Weekly);
        _weeklyDailyRates.Text = FormatWeeklyDailyRates(_snapshot.Weekly, DateTimeOffset.Now);
        _weeklyEndOfDay.Text = FormatWeeklyEndOfDayTarget(_snapshot.Weekly, DateTimeOffset.Now);
        _weeklyLeft.Text = FormatWeeklyLeft(_snapshot.Weekly, DateTimeOffset.Now);
        _status.Text = _refreshing
            ? "Refreshing…"
            : _snapshot.ErrorMessage is not null
                ? $"Refresh failed {_snapshot.RefreshedAt.ToLocalTime():HH:mm:ss}"
                : $"Updated {_snapshot.RefreshedAt.ToLocalTime():HH:mm:ss}";
        _error.Text = _snapshot.ErrorMessage ?? string.Empty;
        _error.Visible = !string.IsNullOrWhiteSpace(_snapshot.ErrorMessage);
    }

    /// <summary>
    /// Calculates average weekly usage per elapsed day and remaining allowance per remaining day.
    /// </summary>
    /// <param name="reading">The normalized weekly limit reading.</param>
    /// <param name="now">The timestamp used for the calculation.</param>
    /// <returns>A short per-day usage description.</returns>
    internal static string FormatWeeklyDailyRates(LimitReading reading, DateTimeOffset now)
    {
        if (reading.State != LimitState.Available
            || reading.RemainingPercent is not int remainingPercent
            || reading.ResetsAt is not DateTimeOffset resetsAt)
        {
            return "Per-day rates unavailable";
        }

        double windowDays = TimeSpan.FromMinutes(CodexRateLimitReader.WeeklyMinutes).TotalDays;
        double daysLeft = Math.Clamp((resetsAt - now).TotalDays, 0, windowDays);
        double daysElapsed = windowDays - daysLeft;
        if (daysElapsed <= 0 || daysLeft <= 0)
        {
            return "Per-day rates unavailable";
        }

        double remaining = Math.Clamp(remainingPercent, 0, 100);
        double usedPerDay = (100 - remaining) / daysElapsed;
        double leftPerDay = remaining / daysLeft;
        return FormattableString.Invariant(
            $"Per day: {usedPerDay:F1}% used · {leftPerDay:F1}% left");
    }

    /// <summary>
    /// Calculates the allowance target at the next whole-day weekly reset boundary.
    /// </summary>
    /// <param name="reading">The normalized weekly limit reading.</param>
    /// <param name="now">The timestamp used for the calculation.</param>
    /// <returns>A short target and countdown description.</returns>
    internal static string FormatWeeklyEndOfDayTarget(LimitReading reading, DateTimeOffset now)
    {
        if (!TryGetWeeklyDayBoundary(
                reading,
                now,
                out double targetPercent,
                out int minutesUntilBoundary))
        {
            return "End of day target unavailable";
        }

        int hoursUntilBoundary = minutesUntilBoundary / 60;
        int remainingMinutes = minutesUntilBoundary % 60;
        string when = minutesUntilBoundary <= 0
            ? "now"
            : hoursUntilBoundary > 0
                ? remainingMinutes > 0
                    ? $"in {hoursUntilBoundary}h {remainingMinutes}m"
                    : $"in {hoursUntilBoundary}h"
                : $"in {minutesUntilBoundary}m";
        return FormattableString.Invariant(
            $"End of day: {targetPercent:F1}% left {when}");
    }

    /// <summary>
    /// Calculates the current weekly allowance remaining above the next whole-day target.
    /// </summary>
    /// <param name="reading">The normalized weekly limit reading.</param>
    /// <param name="now">The timestamp used for the calculation.</param>
    /// <returns>The allowance left before reaching the target.</returns>
    internal static string FormatWeeklyLeft(LimitReading reading, DateTimeOffset now)
    {
        double? left = CalculateWeeklyLeft(reading, now);
        return left is double value
            ? FormattableString.Invariant($"Left: {value:F1}%")
            : "Left unavailable";
    }

    /// <summary>
    /// Calculates the numeric weekly allowance remaining above the next whole-day target.
    /// </summary>
    /// <param name="reading">The normalized weekly limit reading.</param>
    /// <param name="now">The timestamp used for the calculation.</param>
    /// <returns>The percentage left, or null when required data is unavailable.</returns>
    internal static double? CalculateWeeklyLeft(LimitReading reading, DateTimeOffset now)
    {
        if (reading.RemainingPercent is not int remainingPercent
            || !TryGetWeeklyDayBoundary(reading, now, out double targetPercent, out _))
        {
            return null;
        }

        return Math.Clamp(remainingPercent, 0, 100) - targetPercent;
    }

    /// <summary>
    /// Gets the target and delay for the next whole-day weekly reset boundary.
    /// </summary>
    /// <param name="reading">The normalized weekly limit reading.</param>
    /// <param name="now">The timestamp used for the calculation.</param>
    /// <param name="targetPercent">The percentage that should remain at the boundary.</param>
    /// <param name="minutesUntilBoundary">The rounded-up minutes until the boundary.</param>
    /// <returns>True when the weekly reset timing is available.</returns>
    private static bool TryGetWeeklyDayBoundary(
        LimitReading reading,
        DateTimeOffset now,
        out double targetPercent,
        out int minutesUntilBoundary)
    {
        targetPercent = 0;
        minutesUntilBoundary = 0;
        if (reading.State != LimitState.Available
            || reading.ResetsAt is not DateTimeOffset resetsAt)
        {
            return false;
        }

        double windowDays = TimeSpan.FromMinutes(CodexRateLimitReader.WeeklyMinutes).TotalDays;
        double daysLeft = Math.Clamp((resetsAt - now).TotalDays, 0, windowDays);
        int wholeDaysLeft = (int)Math.Floor(daysLeft);
        targetPercent = 100d * wholeDaysLeft / windowDays;
        minutesUntilBoundary = (int)Math.Ceiling(
            TimeSpan.FromDays(daysLeft - wholeDaysLeft).TotalMinutes);
        return true;
    }

    /// <summary>
    /// Renders one normalized limit reading.
    /// </summary>
    /// <param name="valueLabel">The heading/value label.</param>
    /// <param name="progress">The remaining-percentage progress bar.</param>
    /// <param name="resetLabel">The reset-countdown label.</param>
    /// <param name="name">The display name.</param>
    /// <param name="reading">The normalized limit reading.</param>
    private static void RenderLimit(
        Label valueLabel,
        ProgressBar progress,
        Label resetLabel,
        string name,
        LimitReading reading)
    {
        bool available = reading.State == LimitState.Available && reading.RemainingPercent is int;
        int remaining = available ? Math.Clamp(reading.RemainingPercent!.Value, 0, 100) : 0;
        valueLabel.Text = available ? $"{name}: {remaining}% remaining" : $"{name}: N/A";
        progress.Value = remaining;
        resetLabel.Text = available ? FormatReset(reading.ResetsAt) : "Reset unavailable";
    }

    /// <summary>
    /// Formats a reset timestamp as a live countdown.
    /// </summary>
    /// <param name="resetsAt">The local reset timestamp, if supplied by Codex.</param>
    /// <returns>A short reset description.</returns>
    private static string FormatReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
        {
            return "Reset unavailable";
        }

        TimeSpan remaining = resetsAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "Reset due now";
        }

        return remaining.TotalDays >= 1
            ? $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h"
            : $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
    }
}
