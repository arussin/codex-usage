using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CodexUsageTray;
internal sealed class PhoneAlert
{
    public bool Pending { get; set; }
    public bool Delivered { get; set; }
    public int Attempts { get; set; }
}
internal sealed class PhoneWindowState
{
    public int? Previous { get; set; }
    public long Reset { get; set; }
    public PhoneAlert Recovery { get; set; } = new();
    public PhoneAlert Low { get; set; } = new();
}
internal sealed class PhoneNotificationState
{
    public string SettingsKey { get; set; } = "";
    public DateTimeOffset LastRead { get; set; }
    public Dictionary<string, PhoneWindowState> Windows { get; set; } = [];
}
// Reuses successful tray refreshes. No extra polling, server or phone-side timer.
internal sealed class PhoneNotifications : IDisposable
{
    private readonly string settingsPath;
    private readonly string statePath;
    private readonly HttpClient client;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource configurationChanged = new();
    private PhoneNotificationState state;
    internal PhoneNotificationSettings Settings { get; private set; }
    internal string Status { get; private set; } = "Not sent yet";
    internal PhoneNotifications(string? directory = null, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    {
        directory ??= PhoneNotificationFiles.DefaultDirectory;
        settingsPath = Path.Combine(directory, "phone-notifications.json");
        statePath = Path.Combine(directory, "phone-notification-state.json");
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(10) };
        Settings = PhoneNotificationFiles.Load(settingsPath, new PhoneNotificationSettings());
        if (!Settings.IsValid) Settings = new();
        state = PhoneNotificationFiles.Load(statePath, new PhoneNotificationState());
        if (state.Windows is null || state.Windows.Values.Any(w => w is null || w.Recovery is null || w.Low is null)
            || state.SettingsKey != Key(Settings)) state = FreshState(Settings);
    }
    private static string Key(PhoneNotificationSettings settings) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings))));
    private static PhoneNotificationState FreshState(PhoneNotificationSettings settings) => new() { SettingsKey = Key(settings) };
    internal async Task<bool> ConfigureAsync(PhoneNotificationSettings settings)
    {
        if (!settings.IsValid) return false;
        configurationChanged.Cancel();
        await gate.WaitAsync();
        try
        {
            bool saved = PhoneNotificationFiles.Save(settingsPath, settings);
            if (saved || !settings.Enabled)
            {
                Settings = settings;
                state = FreshState(settings);
                PhoneNotificationFiles.Save(statePath, state);
                Status = settings.Enabled ? "Waiting for a fresh baseline" : "Disabled";
            }
            return saved;
        }
        finally
        {
            configurationChanged.Dispose();
            configurationChanged = new();
            gate.Release();
        }
    }
    internal async Task ProcessAsync(UsageSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        try { if (cancellationToken.IsCancellationRequested || !await gate.WaitAsync(0, cancellationToken)) return; }
        catch (OperationCanceledException) { return; }
        try
        {
            if (!Settings.Enabled || !Settings.HasDestination || snapshot.ErrorMessage is not null
                || snapshot.RefreshedAt <= state.LastRead || snapshot.RefreshedAt < clock().AddMinutes(-10)
                || snapshot.RefreshedAt > clock().AddMinutes(1)) return;
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, configurationChanged.Token);
            state.LastRead = snapshot.RefreshedAt;
            if (Settings.Weekly) await ObserveAsync("weekly", snapshot.Weekly, snapshot.RefreshedAt, linked.Token);
            if (Settings.FiveHour) await ObserveAsync("5-hour", snapshot.FiveHour, snapshot.RefreshedAt, linked.Token);
            if (!PhoneNotificationFiles.Save(statePath, state)) Status = "Could not save notification state";
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Never leak the secret topic from HTTP exception text or disrupt quota export.
            Status = "Notification check failed; quota display is unaffected";
        }
        finally { gate.Release(); }
    }
    private async Task ObserveAsync(string window, LimitReading reading, DateTimeOffset at, CancellationToken token)
    {
        if (reading.State != LimitState.Available || reading.RemainingPercent is not int remaining
            || remaining is < 0 or > 100 || reading.ResetsAt is not DateTimeOffset resetsAt || resetsAt <= at) return;
        if (!state.Windows.TryGetValue(window, out PhoneWindowState? previous))
        {
            state.Windows[window] = new() { Previous = remaining, Reset = resetsAt.ToUnixTimeSeconds() };
            return; // The first fresh observation is a quiet baseline, not a quota event.
        }
        long reset = resetsAt.ToUnixTimeSeconds();
        bool newCycle = reset > previous.Reset && at.ToUnixTimeSeconds() >= previous.Reset;
        if (newCycle)
        {
            previous.Recovery = new(); previous.Low = new(); previous.Reset = reset;
        }
        bool recovered = remaining > 90 && (previous.Previous <= 90 || newCycle);
        bool low = remaining < 10 && (previous.Previous >= 10 || newCycle);
        previous.Previous = remaining;
        Queue(previous.Recovery, Settings.Recovery, remaining > 90, recovered);
        Queue(previous.Low, Settings.Low, remaining < 10, low);
        await DeliverAsync(previous.Recovery, window, remaining, recovery: true, token);
        await DeliverAsync(previous.Low, window, remaining, recovery: false, token);
    }
    private static void Queue(PhoneAlert alert, bool enabled, bool inRegion, bool crossed)
    {
        if (!enabled || !inRegion) alert.Pending = false;
        else if (crossed && !alert.Delivered && alert.Attempts < 3) alert.Pending = true;
    }
    private async Task DeliverAsync(PhoneAlert alert, string window, int remaining, bool recovery, CancellationToken token)
    {
        if (!alert.Pending || alert.Delivered || alert.Attempts >= 3) return;
        token.ThrowIfCancellationRequested();
        // Save a bounded attempt before HTTP. Mark delivered only after server acceptance.
        alert.Attempts++;
        if (!PhoneNotificationFiles.Save(statePath, state))
        {
            alert.Attempts--; Status = "Could not save notification state; nothing sent"; return;
        }
        string title = recovery ? $"Codex {window}: quota available again" : $"Codex {window}: quota below 10%";
        string message = recovery ? $"Back above 90%: {remaining}% remaining."
            : $"{remaining}% remaining. Consider slowing down until this window resets.";
        try
        {
            HttpStatusCode code = await PublishAsync(Settings, title, message, recovery ? "battery" : "warning", token);
            int status = (int)code;
            if (status is >= 200 and < 300)
            {
                alert.Delivered = true; alert.Pending = false;
                Status = $"Last accepted by ntfy: {clock().ToLocalTime():g}";
            }
            else
            {
                // Retry transient failures on later fresh reads, never from a stale queue timer.
                if (status < 500 && code is not HttpStatusCode.TooManyRequests and not HttpStatusCode.RequestTimeout) alert.Attempts = 3;
                Status = $"ntfy returned HTTP {status}";
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { Status = "ntfy timed out"; }
        catch (HttpRequestException) { Status = "Could not reach ntfy"; }
        finally
        {
            if (alert.Attempts >= 3 && !alert.Delivered)
            { alert.Pending = false; Status += "; retry limit reached for this alert"; }
            PhoneNotificationFiles.Save(statePath, state);
        }
    }
    private async Task<HttpStatusCode> PublishAsync(PhoneNotificationSettings settings, string title, string message, string tags, CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, settings.Server.TrimEnd('/') + "/" + settings.Topic);
        request.Headers.Add("Title", title); request.Headers.Add("Tags", tags);
        request.Content = new StringContent(message, Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        return response.StatusCode;
    }
    internal async Task<string> TestAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!Settings.HasDestination) return "Configure and save an ntfy topic first.";
            HttpStatusCode code = await PublishAsync(Settings, "Codex quota notifications: test",
                "Phone notifications are connected. This is a test, not a quota change.", "test_tube", token);
            return (int)code is >= 200 and < 300 ? "ntfy accepted the test. Check your phone to confirm delivery." : $"Test failed: HTTP {(int)code}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { return "Test could not reach ntfy. Check connectivity and the saved server."; }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        configurationChanged.Cancel(); client.Dispose();
        // An in-flight refresh may still release gate; do not dispose it here.
    }
}
