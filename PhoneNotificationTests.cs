using System.Net;
using System.Net.Http;
namespace CodexUsageTray;
// Deterministic: no real ntfy publication, Codex call or production state access.
internal static class PhoneNotificationTests
{
    internal static async Task<int> RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CodexPhoneTests-" + Guid.NewGuid().ToString("N"));
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
        PhoneNotificationSettings settings = new() { Enabled = true, Topic = "test-only", Low = true };
        try
        {
            FakeHandler http = new();
            using PhoneNotifications service = new(directory, http, () => now);
            UsageSnapshot Read(int percent, DateTimeOffset? reset = null, int? fiveHour = null) => new(
                fiveHour is int f ? new(LimitState.Available, f, reset ?? now.AddHours(5)) : new(LimitState.Unavailable, null, null),
                new(LimitState.Available, percent, reset ?? now.AddDays(7)), now, null);
            DateTimeOffset cycle = now.AddDays(7);
            async Task Tick(int percent, DateTimeOffset? reset = null, int? fiveHour = null)
            { now = now.AddMinutes(5); await service.ProcessAsync(Read(percent, reset ?? cycle, fiveHour)); }
            await Tick(20); await Tick(96);
            Check(http.Count == 0, "Disabled notifications performed network I/O.");
            Check(await service.ConfigureAsync(settings), "Could not configure test settings.");
            await Tick(96);
            Check(http.Count == 0, "First high reading caused a startup alert.");
            await Tick(90); await Tick(90);
            Check(http.Count == 0, "Exactly 90% caused recovery.");
            await Tick(96);
            Check(http.Count == 1 && http.Messages[^1].Contains("96%"), "Missed-100 recovery did not alert at 96%.");
            await Tick(89); await Tick(95); await Tick(99);
            Check(http.Count == 1, "Recovery repeated in the same reset cycle.");
            await Tick(10);
            Check(http.Count == 1, "Exactly 10% caused a low alert.");
            await Tick(9);
            Check(http.Count == 2 && http.Messages[^1].Contains("9%"), "Below-10 alert did not fire.");
            await Tick(11); await Tick(8);
            Check(http.Count == 2, "Low alert repeated in the same reset cycle.");
            Check(http.Requests.All(r => r == "https://ntfy.sh/test-only"), "Wrong destination.");
            FakeHandler restartedHttp = new();
            using (PhoneNotifications restarted = new(directory, restartedHttp, () => now))
            {
                now = now.AddMinutes(5); await restarted.ProcessAsync(Read(5, cycle));
                Check(restartedHttp.Count == 0, "Restart duplicated a delivered alert.");
            }
            now = cycle.AddMinutes(1); cycle = now.AddDays(7); await Tick(95);
            Check(http.Count == 3, "New cycle did not rearm recovery.");
            await Tick(94, cycle.AddSeconds(10));
            Check(http.Count == 3, "Reset timestamp jitter caused a duplicate.");
            now = cycle.AddMinutes(1); cycle = now.AddDays(7); await Tick(93);
            Check(http.Count == 4, "Reset while both readings were above 90% was missed.");
            await Tick(50); now = now.AddMinutes(5);
            await service.ProcessAsync(UsageSnapshot.Error("not fresh"));
            await service.ProcessAsync(Read(99, cycle) with { RefreshedAt = now.AddHours(-1) });
            Check(http.Count == 4, "An error or stale snapshot triggered a push.");
            await service.ProcessAsync(Read(5, cycle) with { Weekly = new(LimitState.Available, 5, null) });
            Check(http.Count == 4, "Missing reset timestamp triggered a push.");
            await service.ConfigureAsync(settings with { Recovery = false });
            await Tick(20); await Tick(99);
            Check(http.Count == 4, "Disabled recovery option still sent.");
            await Tick(9);
            Check(http.Count == 5, "Low option was incorrectly coupled to recovery.");
            await service.ConfigureAsync(settings with { Low = false });
            await Tick(20); await Tick(5);
            Check(http.Count == 5, "Disabled low option still sent.");
            await service.ConfigureAsync(settings with { Weekly = false, FiveHour = true });
            await Tick(50, cycle, 20); await Tick(50, cycle, 97);
            Check(http.Count == 6 && http.Titles[^1].Contains("5-hour"), "5-hour option failed.");
            await Tick(5, cycle, 95);
            Check(http.Count == 6, "Unselected weekly window sent a notification.");
            await service.ConfigureAsync(settings); await Tick(5);
            Check(http.Count == 6, "First low reading caused a startup alert.");
            http.Code = HttpStatusCode.ServiceUnavailable; await Tick(50); await Tick(96);
            Check(http.Count == 7, "Transient failure was not attempted.");
            await Tick(89);
            Check(http.Count == 7, "Stale recovery retried after quota fell below the region.");
            await Tick(95); await Tick(95); await Tick(95);
            Check(http.Count == 9, "Retry budget was not bounded to three attempts.");
            FakeHandler retryRestartHttp = new();
            using (PhoneNotifications restarted = new(directory, retryRestartHttp, () => now))
            {
                now = now.AddMinutes(5); await restarted.ProcessAsync(Read(99, cycle));
                Check(retryRestartHttp.Count == 0, "Restart reset the exhausted retry budget.");
            }
            http.Code = HttpStatusCode.OK; await service.ConfigureAsync(settings); await Tick(30);
            http.Code = HttpStatusCode.ServiceUnavailable; await Tick(96);
            http.Code = HttpStatusCode.OK; await Tick(94); await Tick(93);
            Check(http.Count == 11, "Successful retry did not stop future sends.");
            await service.ConfigureAsync(settings); await Tick(30); http.Code = HttpStatusCode.Forbidden;
            await Tick(96); await Tick(96);
            Check(http.Count == 12, "Permanent HTTP failure was repeatedly retried.");
            http.Code = HttpStatusCode.OK; await service.ConfigureAsync(settings with { Enabled = false });
            string test = await service.TestAsync();
            Check(http.Count == 13 && test.Contains("accepted"), "Explicit test should work with automatic alerts disabled.");
            Check(http.Messages[^1].Contains("not a quota change"), "Test message looked like a real quota event.");
            Check(!(settings with { Server = "http://ntfy.sh" }).IsValid, "Insecure HTTP was accepted.");
            Check(!(settings with { Topic = "x/other" }).IsValid, "Topic path injection was accepted.");
            Check(!(settings with { Topic = "x\nHeader: x" }).IsValid, "Topic header injection was accepted.");
            Check(!(settings with { Server = "https://user:secret@ntfy.sh" }).IsValid, "URL credentials were accepted.");
            Check(!(settings with { Server = "https://ntfy.sh?token=secret" }).IsValid, "Query credentials were accepted.");
            Check(!(settings with { Server = "https://ntfy.sh/path" }).IsValid, "Server path was accepted.");
            Check(PhoneNotificationSettings.NewTopic() != PhoneNotificationSettings.NewTopic(), "Random topics repeated.");
            Check(!File.ReadAllText(Path.Combine(directory, "phone-notification-state.json")).Contains("test-only"), "Secret topic was duplicated into state.");
            string corrupt = Path.Combine(directory, "corrupt"); Directory.CreateDirectory(corrupt);
            File.WriteAllText(Path.Combine(corrupt, "phone-notifications.json"), "{broken");
            using PhoneNotifications broken = new(corrupt, new FakeHandler(), () => now);
            Check(!broken.Settings.Enabled, "Corrupt preferences opted in.");
            string pending = Path.Combine(directory, "pending");
            FakeHandler fail = new() { Code = HttpStatusCode.ServiceUnavailable };
            using (PhoneNotifications first = new(pending, fail, () => now))
            {
                await first.ConfigureAsync(settings);
                now = now.AddMinutes(5); await first.ProcessAsync(Read(20, cycle));
                now = now.AddMinutes(5); await first.ProcessAsync(Read(96, cycle));
            }
            FakeHandler succeed = new();
            using (PhoneNotifications second = new(pending, succeed, () => now))
            {
                now = now.AddMinutes(5); await second.ProcessAsync(Read(94, cycle));
                now = now.AddMinutes(5); await second.ProcessAsync(Read(93, cycle));
                Check(fail.Count == 1 && succeed.Count == 1, "Pending retry did not survive restart exactly once.");
            }
            Console.WriteLine($"Phone notification self-tests passed ({checks} checks; no real notifications sent)."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Phone notification test failed: {ex.Message}"); return 1; }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        internal int Count => Messages.Count;
        internal HttpStatusCode Code = HttpStatusCode.OK;
        internal List<string> Messages = [];
        internal List<string> Titles = [];
        internal List<string> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!.AbsoluteUri);
            Titles.Add(string.Join(" ", request.Headers.GetValues("Title")));
            Messages.Add(await request.Content!.ReadAsStringAsync(token)); return new HttpResponseMessage(Code);
        }
    }
}
