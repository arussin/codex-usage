namespace CodexUsageTray;
internal sealed partial class TrayApplicationContext
{
    private readonly PhoneNotifications _phoneNotifications = new();
    private void InitializePhoneNotifications()
    {
        ToolStripMenuItem menu = new("Phone notifications");
        ToolStripMenuItem enabled = new("Enable phone notifications");
        ToolStripMenuItem recovery = new("Notify when back above 90% remaining");
        ToolStripMenuItem low = new("Notify when below 10% remaining");
        ToolStripMenuItem weekly = new("Weekly quota");
        ToolStripMenuItem fiveHour = new("5-hour quota");
        ToolStripMenuItem configure = new("Configure ntfy…");
        ToolStripMenuItem copy = new("Copy subscription URL");
        ToolStripMenuItem test = new("Send test notification");
        ToolStripMenuItem status = new() { Enabled = false };
        enabled.Click += async (_, _) =>
        {
            if (!_phoneNotifications.Settings.HasDestination) await ConfigurePhoneNotificationsAsync();
            else await SavePhoneSettingsAsync(_phoneNotifications.Settings with { Enabled = !enabled.Checked });
        };
        recovery.Click += async (_, _) => await SavePhoneSettingsAsync(_phoneNotifications.Settings with { Recovery = !recovery.Checked });
        low.Click += async (_, _) => await SavePhoneSettingsAsync(_phoneNotifications.Settings with { Low = !low.Checked });
        weekly.Click += async (_, _) => await SavePhoneSettingsAsync(_phoneNotifications.Settings with { Weekly = !weekly.Checked });
        fiveHour.Click += async (_, _) => await SavePhoneSettingsAsync(_phoneNotifications.Settings with { FiveHour = !fiveHour.Checked });
        configure.Click += async (_, _) => await ConfigurePhoneNotificationsAsync();
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_phoneNotifications.Settings.Server.TrimEnd('/') + "/" + _phoneNotifications.Settings.Topic); }
            catch (System.Runtime.InteropServices.ExternalException)
            { MessageBox.Show("The clipboard is busy. Please try again.", "Codex Usage Tray"); }
        };
        test.Click += async (_, _) =>
        {
            test.Enabled = false;
            try
            {
                string result = await _phoneNotifications.TestAsync(_shutdown.Token);
                if (!_shutdown.IsCancellationRequested) MessageBox.Show(result, "Codex Usage Tray");
            }
            catch (OperationCanceledException) { }
            finally { if (!_shutdown.IsCancellationRequested) test.Enabled = true; }
        };
        menu.DropDownOpening += (_, _) =>
        {
            PhoneNotificationSettings settings = _phoneNotifications.Settings;
            enabled.Checked = settings.Enabled; recovery.Checked = settings.Recovery;
            low.Checked = settings.Low; weekly.Checked = settings.Weekly; fiveHour.Checked = settings.FiveHour;
            copy.Enabled = test.Enabled = settings.HasDestination; status.Text = _phoneNotifications.Status;
        };
        menu.DropDownItems.AddRange([enabled, configure, copy, test, new ToolStripSeparator(), recovery, low,
            new ToolStripSeparator(), weekly, fiveHour, new ToolStripSeparator(), status]);
        _menu.Items.Insert(2, menu);
    }
    private async Task ConfigurePhoneNotificationsAsync()
    {
        using PhoneNotificationDialog dialog = new(_phoneNotifications.Settings);
        if (dialog.ShowDialog() == DialogResult.OK) await SavePhoneSettingsAsync(dialog.Settings);
    }
    private async Task SavePhoneSettingsAsync(PhoneNotificationSettings settings)
    {
        bool saved = await _phoneNotifications.ConfigureAsync(settings);
        if (!saved)
        {
            MessageBox.Show(settings.Enabled ? "Could not save valid phone settings. The previous settings remain in effect."
                : "Notifications are disabled for this session, but saving failed. Check the setting before the next launch.",
                "Codex Usage Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else if (settings.Enabled) _ = RefreshAsync();
    }
}
internal sealed class PhoneNotificationDialog : Form
{
    internal PhoneNotificationSettings Settings { get; private set; }
    internal PhoneNotificationDialog(PhoneNotificationSettings settings)
    {
        Settings = settings; Text = "Codex phone notifications"; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(510, 375); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        TextBox server = new() { Text = settings.Server, Dock = DockStyle.Fill };
        TextBox topic = new() { Text = string.IsNullOrEmpty(settings.Topic) ? PhoneNotificationSettings.NewTopic() : settings.Topic, Dock = DockStyle.Fill };
        CheckBox enabled = new() { Text = "Enable phone notifications", Checked = settings.Enabled, AutoSize = true };
        CheckBox recovery = new() { Text = "Back above 90% remaining", Checked = settings.Recovery, AutoSize = true };
        CheckBox low = new() { Text = "Below 10% remaining", Checked = settings.Low, AutoSize = true };
        CheckBox weekly = new() { Text = "Weekly", Checked = settings.Weekly, AutoSize = true };
        CheckBox fiveHour = new() { Text = "5-hour", Checked = settings.FiveHour, AutoSize = true };
        FlowLayoutPanel alerts = new() { AutoSize = true, Dock = DockStyle.Fill }; alerts.Controls.AddRange([recovery, low]);
        FlowLayoutPanel windows = new() { AutoSize = true, Dock = DockStyle.Fill }; windows.Controls.AddRange([weekly, fiveHour]);
        Button random = new() { Text = "New random topic", AutoSize = true }; random.Click += (_, _) => topic.Text = PhoneNotificationSettings.NewTopic();
        Label help = new()
        {
            Text = "Subscribe to this server/topic in the ntfy phone app, then use Send test notification in the tray menu. "
                + "Anyone with the topic name can read or publish to it on an open server; keep it private. "
                + "Only short quota alerts are sent, never account credentials or your JSON feed.",
            AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 10, 0, 10),
        };
        Button save = new() { Text = "Save", AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([cancel, save]);
        save.Click += (_, _) =>
        {
            PhoneNotificationSettings value = new()
            {
                Enabled = enabled.Checked, Server = server.Text.Trim().TrimEnd('/'), Topic = topic.Text.Trim(),
                Recovery = recovery.Checked, Low = low.Checked, Weekly = weekly.Checked, FiveHour = fiveHour.Checked,
            };
            if (!value.HasDestination)
            {
                MessageBox.Show("Use an HTTPS server origin (no path, credentials or query) and a topic of 1–64 letters, digits, underscores or hyphens.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
            }
            Settings = value; DialogResult = DialogResult.OK;
        };
        layout.Controls.Add(new Label { Text = "Server", AutoSize = true }, 0, 0); layout.Controls.Add(server, 1, 0);
        layout.Controls.Add(new Label { Text = "Topic", AutoSize = true }, 0, 1); layout.Controls.Add(topic, 1, 1);
        layout.Controls.Add(random, 1, 2);
        layout.Controls.Add(enabled, 0, 3); layout.SetColumnSpan(enabled, 2);
        layout.Controls.Add(alerts, 0, 4); layout.SetColumnSpan(alerts, 2);
        layout.Controls.Add(windows, 0, 5); layout.SetColumnSpan(windows, 2);
        layout.Controls.Add(help, 0, 6); layout.SetColumnSpan(help, 2);
        layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
    }
}
