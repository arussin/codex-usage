namespace CodexUsageTray;

internal static class SelfTests
{
    /// <summary>
    /// Runs deterministic package-free checks followed by one live Codex CLI smoke read.
    /// </summary>
    /// <returns>Zero when every check succeeds; otherwise one.</returns>
    internal static async Task<int> RunAsync()
    {
        try
        {
            // Display scaling must be configured before any test draws an icon or creates a window.
            ApplicationConfiguration.Initialize();
            DateTimeOffset now = DateTimeOffset.Now;
            TestCodexParsing(now);
            TestClaudeParsing(now);
            TestTrayNumbers(now);
            TestWeeklyDailyRates(now);
            TestWeeklyEndOfDayTarget(now);
            TestWeeklyLeft(now);
            TestUsageHistoryPersistence(now);
            TestJsonSnapshotPersistence(now);
            TestJsonExportConsent(now);
            TestJsonSiblingExport(now);
            TestPlotHistorySelection(now);
            TestUsagePrediction(now);
            TestHistoryCsv(now);
            TestPopupReopen();
            TestPhoneNotificationDialog();

            using (CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(20)))
            {
                await ExpectThrowsAsync<OperationCanceledException>(
                    () => CodexRateLimitReader.WaitForResponseAsync(
                        new BlockingTextReader(),
                        2,
                        timeout.Token),
                    "Cancellation did not stop a pending protocol read.").ConfigureAwait(false);
            }

            TestAlertPersistence(now);
            TestStartupRegistration();
            TestDuplicateMutex();

            UsageSnapshot live = await new CodexRateLimitReader().FetchAsync().ConfigureAwait(false);
            Check(
                live.FiveHour.State == LimitState.Available || live.Weekly.State == LimitState.Available,
                "The live Codex smoke read returned no available usage window.");
            await ReportClaudeLiveReadAsync().ConfigureAwait(false);

            Console.WriteLine("Self-tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Self-test failed: {exception.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Reports one live Claude read without failing the run: Claude is an optional source,
    /// and a machine where Claude Code is absent or signed out must still pass.
    /// </summary>
    private static async Task ReportClaudeLiveReadAsync()
    {
        try
        {
            UsageSnapshot claude = await new ClaudeUsageReader().FetchAsync().ConfigureAwait(false);
            Console.WriteLine(
                $"Claude live read: 5-hour {claude.FiveHour.State}, weekly {claude.Weekly.State}, "
                + $"{claude.Scoped.Count} scoped.");
        }
        catch (ClaudeUsageException exception)
        {
            Console.WriteLine($"Claude live read skipped: {exception.Message}");
        }
    }

    /// <summary>
    /// Verifies normalization of the Claude Code /usage report and its failure cases.
    /// </summary>
    /// <param name="now">The refresh timestamp.</param>
    private static void TestClaudeParsing(DateTimeOffset now)
    {
        const string Init = """{"type":"system","subtype":"init","session_id":"s"}""";
        const string Result = """{"type":"result","subtype":"success","result":"Current session"}""";
        const string Limits =
            """{"kind":"session","group":"session","percent":68,"resets_at":"2026-11-21T23:20:00.924Z","severity":"normal","is_active":true},"""
            + """{"kind":"weekly_all","group":"weekly","percent":5.6,"resets_at":"2026-11-23T15:00:00Z","severity":"normal","is_active":false},"""
            + """{"kind":"weekly_scoped","group":"weekly","percent":12,"resets_at":null,"scope":{"model":{"display_name":"Fable"}},"severity":"normal","is_active":false},"""
            + """{"kind":"future_meter","group":"other","percent":50,"resets_at":null,"severity":"normal","is_active":false},"""
            + """{"kind":"session","group":"session","percent":"broken","resets_at":null}""";

        UsageSnapshot snapshot = ClaudeUsageReader.ParseUsageOutput(
            string.Join('\n', "not json", "{broken", Init, ClaudeReport(Limits), Result),
            now);
        Check(snapshot.ErrorMessage is null && snapshot.RefreshedAt == now, "Claude snapshot metadata was incorrect.");
        Check(snapshot.FiveHour.RemainingPercent == 32, "Claude five-hour remaining percentage was incorrect.");
        Check(
            snapshot.FiveHour.ResetsAt == DateTimeOffset.Parse(
                "2026-11-21T23:20:00.924Z",
                System.Globalization.CultureInfo.InvariantCulture),
            "Claude five-hour reset time was incorrect.");
        Check(snapshot.Weekly.RemainingPercent == 94, "Claude fractional weekly usage was not rounded.");
        Check(
            snapshot.Scoped.Count == 1
                && snapshot.Scoped[0].Label == "Fable"
                && snapshot.Scoped[0].Reading.RemainingPercent == 88
                && snapshot.Scoped[0].Reading.ResetsAt is null,
            "Claude scoped weekly limit was incorrect.");
        Check(
            UsagePopup.FormatScopedLimit(snapshot.Scoped[0]) == "Fable weekly: 88% remaining · reset unavailable",
            "Claude scoped weekly limit text was incorrect.");

        UsageSnapshot clamped = ClaudeUsageReader.ParseUsageOutput(
            ClaudeReport(
                """{"kind":"session","percent":150,"resets_at":null},{"kind":"weekly_all","percent":-10,"resets_at":null}"""),
            now);
        Check(clamped.FiveHour.RemainingPercent == 0, "Excess Claude usage was not clamped.");
        Check(clamped.Weekly.RemainingPercent == 100, "Negative Claude usage was not clamped.");

        UsageSnapshot empty = ClaudeUsageReader.ParseUsageOutput(ClaudeReport(string.Empty), now);
        Check(
            empty.FiveHour.State == LimitState.Unavailable
                && empty.Weekly.State == LimitState.Unavailable
                && empty.Scoped.Count == 0,
            "A Claude report with no meters was not unavailable.");

        // A signed-out CLI answers /usage with session costs and no plan report.
        ExpectThrows<ClaudeUsageException>(
            () => ClaudeUsageReader.ParseUsageOutput(
                string.Join('\n', Init, """{"type":"assistant","message":{"content":[]}}""", Result),
                now),
            "A Claude answer without a plan report was accepted.");
        ExpectThrows<ClaudeUsageException>(
            () => ClaudeUsageReader.ParseUsageOutput(string.Join('\n', Init, Result), now),
            "Claude output without an answer was accepted.");
        ExpectThrows<ClaudeUsageException>(
            () => ClaudeUsageReader.ParseUsageOutput(
                """{"type":"assistant","usage_report":{"rate_limits":null}}""",
                now),
            "A Claude report without plan limits was accepted.");
        ExpectThrows<ClaudeUsageException>(
            () => ClaudeUsageReader.ParseUsageOutput(
                """{"type":"assistant","usage_report":{"rate_limits":{"limits":null}}}""",
                now),
            "A Claude report with a failed limits fetch was accepted.");

        Check(
            ClaudeUsageReader.IsHostSessionVariable("CLAUDE_CODE_SESSION_ID")
                && ClaudeUsageReader.IsHostSessionVariable("CLAUDECODE")
                && !ClaudeUsageReader.IsHostSessionVariable("CLAUDE_CONFIG_DIR")
                && !ClaudeUsageReader.IsHostSessionVariable("PATH"),
            "Claude host-session environment filtering was incorrect.");
    }

    /// <summary>
    /// Builds the stream-json line that carries a /usage report.
    /// </summary>
    /// <param name="limits">The serialized usage rows, without the surrounding array.</param>
    /// <returns>The serialized assistant message.</returns>
    private static string ClaudeReport(string limits) =>
        """{"type":"assistant","message":{"role":"assistant","content":[]},"usage_report":{"session":{"total_cost_usd":0},"rate_limits":{"limits":["""
        + limits
        + """],"extra_usage":null}}}""";

    /// <summary>
    /// Verifies which tray numbers are shown, that a source can be switched off, and their icons.
    /// </summary>
    /// <param name="now">The timestamp used for deterministic readings.</param>
    private static void TestTrayNumbers(DateTimeOffset now)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "tray-numbers.json");
        UsageSource codex = UsageSources.Codex;
        UsageSource claude = UsageSources.Claude;
        try
        {
            TrayNumberStore store = new(UsageSources.All, path);
            Check(
                store.IsShown(codex, LimitWindow.Weekly) && store.ShownCount == 1 && !store.IsActive(claude),
                "A fresh install did not show only the Codex weekly number.");
            Check(!File.Exists(path), "Loading default tray numbers wrote settings.");
            Check(!store.TrySet(codex, LimitWindow.Weekly, false), "The last tray number was hidden.");
            Check(!store.TrySet(codex, LimitWindow.Scoped, true), "A window the source lacks was shown.");
            Check(
                store.TrySet(claude, LimitWindow.FiveHour, true) && store.IsActive(claude),
                "A Claude tray number could not be shown.");
            Check(
                store.TrySet(codex, LimitWindow.Weekly, false) && !store.IsActive(codex),
                "Codex could not be switched off.");

            TrayNumberStore restarted = new(UsageSources.All, path);
            Check(
                restarted.IsShown(claude, LimitWindow.FiveHour) && !restarted.IsActive(codex),
                "Tray numbers did not survive restart.");
            File.WriteAllText(path, "{broken");
            Check(
                new TrayNumberStore(UsageSources.All, path).IsShown(codex, LimitWindow.Weekly),
                "Corrupt tray number settings did not fall back to defaults.");
            File.WriteAllText(path, """{"codex":[],"claude":["Nonsense"]}""");
            Check(
                new TrayNumberStore(UsageSources.All, path).ShownCount == 1,
                "Settings with no tray numbers left the menu unreachable.");

            Check(
                TrayApplicationContext.ResolveFrame(codex, LimitWindow.Weekly) == TrayFrame.None
                    && TrayApplicationContext.ResolveFrame(codex, LimitWindow.FiveHour) == TrayFrame.Circle
                    && TrayApplicationContext.ResolveFrame(claude, LimitWindow.Weekly) == TrayFrame.Square
                    && TrayApplicationContext.ResolveFrame(claude, LimitWindow.FiveHour) == TrayFrame.Circle
                    && TrayApplicationContext.ResolveFrame(claude, LimitWindow.Scoped) == TrayFrame.Flagged
                    && codex.FrameColor != claude.FrameColor,
                "Tray numbers were not distinguishable.");
            Check(
                TrayApplicationContext.DescribeFrame(codex, LimitWindow.Weekly) == "no frame"
                    && TrayApplicationContext.DescribeFrame(claude, LimitWindow.Weekly) == "orange square"
                    && TrayApplicationContext.DescribeFrame(claude, LimitWindow.FiveHour) == "orange circle",
                "The tray number legend was incorrect.");
            LimitReading reading = new(LimitState.Available, 32, now.AddHours(2));
            Check(
                TrayIconRenderer.ResolveShortWindowColor(reading) == Color.LimeGreen
                    && TrayIconRenderer.ResolveShortWindowColor(reading with { RemainingPercent = 20 }) == Color.Red
                    && TrayIconRenderer.ResolveShortWindowColor(reading with { State = LimitState.Unavailable })
                        == Color.Gray,
                "Five-hour tray text colour was incorrect.");
            foreach (TrayFrame frame in Enum.GetValues<TrayFrame>())
            {
                using Icon icon = TrayIconRenderer.Render(reading, Color.LimeGreen, frame, claude.FrameColor);
                Check(icon.Width > 0, "A tray number could not be rendered.");
                SavePreview(icon, $"tray-number-{frame}".ToLowerInvariant());
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that an additional source exports beside the configured file under the same consent.
    /// </summary>
    /// <param name="now">The timestamp used for deterministic snapshots.</param>
    private static void TestJsonSiblingExport(DateTimeOffset now)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-{Guid.NewGuid():N}");
        string configured = Path.Combine(directory, "phone", "usage.json");
        string sibling = Path.Combine(directory, "phone", "claude.json");
        UsageSnapshot codex = new(
            new LimitReading(LimitState.Unavailable, null, null),
            new LimitReading(LimitState.Available, 44, now.AddDays(4)),
            now,
            null);
        UsageSnapshot claude = new(
            new LimitReading(LimitState.Available, 32, now.AddHours(3)),
            new LimitReading(LimitState.Available, 94, now.AddDays(2)),
            now.AddSeconds(1),
            null)
        {
            Scoped = [new ScopedLimit("Fable", new LimitReading(LimitState.Available, 88, now.AddDays(2)))],
        };
        try
        {
            JsonExportController export = new(new JsonExportSettingsStore(Path.Combine(directory, "settings.json")));
            Check(export.TryConfigure(new(false, configured)), "Could not configure a disabled destination.");
            export.Save(claude, "claude.json", includeScoped: true);
            Check(!File.Exists(sibling), "Disabled export created an additional source's snapshot.");
            Check(export.TryConfigure(new(true, configured)), "Could not enable export.");
            export.Save(codex);
            string codexJson = File.ReadAllText(configured);
            export.Save(claude, "claude.json", includeScoped: true);
            Check(File.ReadAllText(configured) == codexJson, "An additional source changed the configured file.");
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(sibling)))
            {
                System.Text.Json.JsonElement root = document.RootElement;
                CheckJsonFields(root, "fiveHour", "weekly", "weeklyScoped", "refreshedAt");
                System.Text.Json.JsonElement scoped = root.GetProperty("weeklyScoped");
                CheckJsonFields(scoped, "label", "available", "remaining", "resetsAt");
                Check(
                    scoped.GetProperty("label").GetString() == "Fable"
                        && scoped.GetProperty("available").GetBoolean()
                        && scoped.GetProperty("remaining").GetInt32() == 88
                        && scoped.GetProperty("resetsAt").GetDateTimeOffset().EqualsExact(now.AddDays(2)),
                    "The scoped weekly limit differed from the UI.");
                Check(root.GetProperty("fiveHour").GetProperty("remaining").GetInt32() == 32, "Additional five-hour quota differed from the UI.");
                Check(root.GetProperty("weekly").GetProperty("remaining").GetInt32() == 94, "Additional weekly quota differed from the UI.");
                Check(root.GetProperty("refreshedAt").GetDateTimeOffset().EqualsExact(now.AddSeconds(1)), "Additional refresh timestamp changed.");
            }

            // Two scoped limits publish the tighter one; none publishes an unavailable entry, never a stale one.
            export.Save(
                claude with
                {
                    RefreshedAt = now.AddSeconds(2),
                    Scoped =
                    [
                        new ScopedLimit("Fable", new LimitReading(LimitState.Available, 88, now.AddDays(2))),
                        new ScopedLimit("Other\u0007 model", new LimitReading(LimitState.Available, 12, null)),
                    ],
                },
                "claude.json",
                includeScoped: true);
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(sibling)))
            {
                System.Text.Json.JsonElement scoped = document.RootElement.GetProperty("weeklyScoped");
                Check(
                    scoped.GetProperty("label").GetString() == "Other model"
                        && scoped.GetProperty("remaining").GetInt32() == 12
                        && scoped.GetProperty("resetsAt").ValueKind == System.Text.Json.JsonValueKind.Null,
                    "The tightest scoped weekly limit was not the one published.");
            }

            export.Save(claude with { RefreshedAt = now.AddSeconds(1), Scoped = [] }, "claude.json", includeScoped: true);
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(sibling)))
            {
                System.Text.Json.JsonElement scoped = document.RootElement.GetProperty("weeklyScoped");
                Check(
                    !scoped.GetProperty("available").GetBoolean()
                        && scoped.GetProperty("label").ValueKind == System.Text.Json.JsonValueKind.Null
                        && scoped.GetProperty("remaining").ValueKind == System.Text.Json.JsonValueKind.Null
                        && scoped.GetProperty("resetsAt").ValueKind == System.Text.Json.JsonValueKind.Null,
                    "A missing scoped weekly limit was not published as unavailable.");
            }

            export.Save(UsageSnapshot.Error("Sensitive error metadata must never be exported."), "claude.json", includeScoped: true);
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(sibling)))
            {
                Check(
                    document.RootElement.GetProperty("refreshedAt").GetDateTimeOffset().EqualsExact(now.AddSeconds(1)),
                    "A failed additional read replaced its last good snapshot.");
            }

            Check(
                JsonExportController.SiblingPath(sibling, "CLAUDE.json")
                    == Path.Combine(directory, "phone", "CLAUDE-usage.json"),
                "An additional source could overwrite the configured file.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies normalization of Codex rate-limit responses and protocol errors.
    /// </summary>
    /// <param name="now">The refresh timestamp.</param>
    private static void TestCodexParsing(DateTimeOffset now)
    {
        long reset = now.AddHours(1).ToUnixTimeSeconds();

        UsageSnapshot both = Parse(
            Window(25, CodexRateLimitReader.FiveHourMinutes, reset),
            Window(60, CodexRateLimitReader.WeeklyMinutes, reset),
            now);
        Check(both.FiveHour.RemainingPercent == 75, "Five-hour remaining percentage was incorrect.");
        Check(both.Weekly.RemainingPercent == 40, "Weekly remaining percentage was incorrect.");

        UsageSnapshot single = Parse(
            Window(10, CodexRateLimitReader.FiveHourMinutes, reset),
            null,
            now);
        Check(single.FiveHour.State == LimitState.Available, "Single five-hour window was not available.");
        Check(single.Weekly.State == LimitState.Unavailable, "Missing weekly window was not unavailable.");

        UsageSnapshot weeklyOnly = Parse(
            Window(15, CodexRateLimitReader.WeeklyMinutes, reset),
            null,
            now);
        Check(weeklyOnly.FiveHour.State == LimitState.Unavailable, "Missing five-hour window was not unavailable.");
        Check(weeklyOnly.Weekly.State == LimitState.Available, "Single weekly window was not available.");

        UsageSnapshot reversed = Parse(
            Window(30, CodexRateLimitReader.WeeklyMinutes, reset),
            Window(40, CodexRateLimitReader.FiveHourMinutes, reset),
            now);
        Check(reversed.FiveHour.RemainingPercent == 60, "Reversed five-hour window was misclassified.");
        Check(reversed.Weekly.RemainingPercent == 70, "Reversed weekly window was misclassified.");

        UsageSnapshot clamped = Parse(
            Window(-10, CodexRateLimitReader.FiveHourMinutes, reset),
            Window(150, CodexRateLimitReader.WeeklyMinutes, reset),
            now);
        Check(clamped.FiveHour.RemainingPercent == 100, "Negative usage was not clamped.");
        Check(clamped.Weekly.RemainingPercent == 0, "Excess usage was not clamped.");

        ExpectThrows<CodexUsageException>(
            () => CodexRateLimitReader.ParseUsageResponse("{", now),
            "Malformed JSON was accepted.");
        ExpectThrows<CodexUsageException>(
            () => CodexRateLimitReader.ThrowIfProtocolError(
                """{"id":2,"error":{"message":"denied"}}"""),
            "Protocol error was accepted.");
    }

    /// <summary>
    /// Verifies the graph history selector switches between current-window and all-data modes.
    /// </summary>
    /// <param name="now">The timestamp used for deterministic history samples.</param>
    private static void TestPlotHistorySelection(DateTimeOffset now)
    {
        DateTimeOffset windowStart = now.AddDays(-7);
        DateTimeOffset windowEnd = now;
        UsageHistoryPoint oldPoint = new(now.AddDays(-8), 90);
        UsageHistoryPoint currentPoint = new(now.AddDays(-1), 70);
        UsageHistoryPoint[] history = [currentPoint, oldPoint];

        UsageHistoryPoint[] weekly = UsagePopup.SelectPlotHistory(
            history,
            windowStart,
            windowEnd,
            includeAllHistory: false);
        UsageHistoryPoint[] all = UsagePopup.SelectPlotHistory(
            history,
            windowStart,
            windowEnd,
            includeAllHistory: true);

        Check(weekly.Length == 1 && weekly[0] == currentPoint, "Weekly plot history selection was incorrect.");
        Check(all.Length == 2 && all[0] == oldPoint, "All-data plot history selection was incorrect.");
    }

    /// <summary>
    /// Verifies the downloadable weekly time-series CSV.
    /// </summary>
    /// <param name="now">The timestamp used for deterministic history samples.</param>
    private static void TestHistoryCsv(DateTimeOffset now)
    {
        string csv = UsagePopup.BuildHistoryCsv(
        [
            new UsageHistoryPoint(now.AddMinutes(-5), 80),
            new UsageHistoryPoint(now, 75),
        ]);
        Check(
            csv.StartsWith(
                "recordedAt,remainingPercent",
                StringComparison.Ordinal),
            "Usage-history CSV header was incorrect.");
        Check(csv.Contains(",75", StringComparison.Ordinal), "Usage-history CSV data was incorrect.");
    }

    /// <summary>
    /// Verifies the zero-usage prediction from declining weekly samples.
    /// </summary>
    /// <param name="now">The timestamp used for deterministic history samples.</param>
    private static void TestUsagePrediction(DateTimeOffset now)
    {
        UsageHistoryPoint[] declining =
        [
            new UsageHistoryPoint(now.AddHours(-2), 80),
            new UsageHistoryPoint(now, 60),
        ];
        Check(
            UsagePopup.PredictZeroAt(declining) == now.AddHours(6),
            "Weekly zero prediction was incorrect.");
        Check(
            UsagePopup.PredictZeroAt(
            [
                new UsageHistoryPoint(now.AddHours(-1), 60),
                new UsageHistoryPoint(now, 60),
            ]) is null,
            "Flat weekly history produced a zero prediction.");
    }

    /// <summary>
    /// Verifies weekly usage-history retention and persistence.
    /// </summary>
    /// <param name="now">The timestamp used to create retained and expired samples.</param>
    private static void TestUsageHistoryPersistence(DateTimeOffset now)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "usage-history.json");
        try
        {
            UsageHistoryStore store = new(path);
            LimitReading reading = new(LimitState.Available, 80, now.AddDays(1));
            store.Record(reading, now.AddDays(-8));
            store.Record(reading with { RemainingPercent = 55 }, now);

            Check(store.Points.Count == 1, "Expired weekly usage history was retained.");
            Check(store.Points[0].RemainingPercent == 55, "Weekly usage history value was incorrect.");
            Check(new UsageHistoryStore(path).Points.Count == 1, "Weekly usage history was not persisted.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies weekly per-day rates and unavailable reset handling.
    /// </summary>
    /// <param name="now">The timestamp used for deterministic rate calculations.</param>
    private static void TestWeeklyDailyRates(DateTimeOffset now)
    {
        LimitReading reading = new(LimitState.Available, 55, now.AddDays(4));
        Check(
            UsagePopup.FormatWeeklyDailyRates(reading, now)
                == "Per day: 15.0% used · 13.8% left",
            "Weekly per-day rates were incorrect.");
        Check(
            UsagePopup.FormatWeeklyDailyRates(reading with { ResetsAt = null }, now)
                == "Per-day rates unavailable",
            "Missing weekly reset time did not make per-day rates unavailable.");
    }

    /// <summary>
    /// Verifies the allowance target at the next whole-day weekly boundary.
    /// </summary>
    /// <param name="now">The timestamp used for deterministic target calculations.</param>
    private static void TestWeeklyEndOfDayTarget(DateTimeOffset now)
    {
        LimitReading reading = new(LimitState.Available, 55, now.AddDays(5).AddHours(11));
        Check(
            UsagePopup.FormatWeeklyEndOfDayTarget(reading, now)
                == "End of day: 71.4% left in 11h",
            "Weekly end-of-day target was incorrect.");
        Check(
            UsagePopup.FormatWeeklyEndOfDayTarget(reading with { ResetsAt = now.AddDays(5) }, now)
                == "End of day: 71.4% left now",
            "Whole-day weekly target was not due now.");
        Check(
            UsagePopup.FormatWeeklyEndOfDayTarget(reading with { ResetsAt = null }, now)
                == "End of day target unavailable",
            "Missing weekly reset time did not make the end-of-day target unavailable.");
    }

    /// <summary>
    /// Verifies the weekly allowance remaining above the next whole-day target.
    /// </summary>
    /// <param name="now">The timestamp used for deterministic target calculations.</param>
    private static void TestWeeklyLeft(DateTimeOffset now)
    {
        LimitReading reading = new(LimitState.Available, 55, now.AddDays(5).AddHours(11));
        double? left = UsagePopup.CalculateWeeklyLeft(reading, now);
        Check(left is < 0, "Weekly allowance left was not negative.");
        Check(
            UsagePopup.FormatWeeklyLeft(reading, now) == "Left: -16.4%",
            "Weekly allowance left was incorrect.");
        Check(
            TrayIconRenderer.ResolveTextColor(left) == Color.Red,
            "Negative weekly allowance left did not produce red tray text.");
        Check(
            TrayIconRenderer.ResolveTextColor(0) == Color.LimeGreen,
            "Non-negative weekly allowance left did not produce green tray text.");
        Check(
            TrayIconRenderer.ResolveTextColor(null) == Color.Gray,
            "Unavailable weekly allowance left did not produce gray tray text.");
        Check(
            UsagePopup.FormatWeeklyLeft(reading with { RemainingPercent = null }, now)
                == "Left unavailable",
            "Missing weekly percentage did not make allowance left unavailable.");
    }

    /// <summary>
    /// Verifies readable scaled labels and stable sizing when reopening the reusable popup.
    /// </summary>
    private static void TestPopupReopen()
    {
        TestPopupReopen(new UsagePopup());
        TestPopupReopen(new UsagePopup(UsageSources.Claude.DisplayName, UsageSources.Claude.ScopedRows));
    }

    /// <summary>
    /// Verifies one source's popup, including the extra rows a source with scoped limits adds.
    /// </summary>
    /// <param name="popup">The popup under test, disposed before returning.</param>
    private static void TestPopupReopen(UsagePopup popup)
    {
        using UsagePopup owned = popup;
        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset weeklyReset = now.AddDays(2).AddHours(5);
        popup.UpdateSnapshot(
            new UsageSnapshot(
                new LimitReading(LimitState.Available, 32, now.AddHours(3)),
                new LimitReading(LimitState.Available, 61, weeklyReset),
                now,
                null)
            {
                Scoped = [new ScopedLimit("Fable", new LimitReading(LimitState.Available, 48, weeklyReset))],
            },
            refreshing: false,
            [
                new UsageHistoryPoint(now.AddDays(-4), 96),
                new UsageHistoryPoint(now.AddDays(-2), 80),
                new UsageHistoryPoint(now, 61),
            ]);
        popup.Show();
        SavePreview(popup);
        Size shownSize = popup.Size;
        foreach (Label label in popup.Controls.OfType<Label>())
        {
            Check(
                label.Height >= label.PreferredHeight,
                $"Popup text was clipped at the current display DPI: \"{label.Text}\" in {popup.Text} is "
                + $"{label.Height} high and needs {label.PreferredHeight} at {popup.DeviceDpi} DPI.");
            Check(
                popup.ClientRectangle.Contains(label.Bounds),
                $"Popup text extended outside the window: \"{label.Text}\" in {popup.Text}.");
        }

        popup.Close();
        Check(!popup.IsDisposed, "Closing the popup disposed it.");
        popup.Show();
        Check(popup.Size == shownSize, "Reopening the popup changed its scaled size.");
        popup.Hide();
    }

    /// <summary>
    /// Verifies that every choice in the phone notification dialog is fully visible at the current display DPI.
    /// </summary>
    private static void TestPhoneNotificationDialog()
    {
        // Shown off screen, with a placeholder topic: the real settings are never loaded here.
        using PhoneNotificationDialog dialog = new(new PhoneNotificationSettings { Topic = "test-only" })
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false,
        };
        dialog.Show();
        SavePreview(dialog);
        List<Control> controls = [];
        void Collect(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                controls.Add(child);
                Collect(child);
            }
        }

        Collect(dialog);
        Check(controls.OfType<CheckBox>().Any(), "The phone notification dialog has no choices to check.");
        foreach (Control control in controls)
        {
            Check(
                control.Parent!.ClientRectangle.Contains(control.Bounds),
                $"A phone notification choice was cut off: \"{control.Text}\" ({control.GetType().Name}) "
                + $"at {dialog.DeviceDpi} DPI.");
        }

        dialog.Close();
    }

    /// <summary>
    /// Saves a picture of a window when CODEX_USAGE_TRAY_PREVIEW_DIR names a folder, so its layout
    /// can be reviewed, or a documentation screenshot refreshed, without a running tray.
    /// </summary>
    /// <param name="popup">The shown window holding sample readings or settings.</param>
    private static void SavePreview(Form popup)
    {
        string? directory = Environment.GetEnvironmentVariable("CODEX_USAGE_TRAY_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using Bitmap bitmap = new(popup.Width, popup.Height);
        popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, popup.Size));
        bitmap.Save(
            Path.Combine(directory, popup.Text.Replace(' ', '-').ToLowerInvariant() + ".png"),
            System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>
    /// Saves a picture of a tray number when CODEX_USAGE_TRAY_PREVIEW_DIR names a folder.
    /// </summary>
    /// <param name="icon">The rendered tray icon.</param>
    /// <param name="name">The file name without its extension.</param>
    private static void SavePreview(Icon icon, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("CODEX_USAGE_TRAY_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using Bitmap bitmap = icon.ToBitmap();
        bitmap.Save(Path.Combine(directory, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>
    /// Builds and parses a general Codex response containing up to two windows.
    /// </summary>
    /// <param name="primary">The primary window JSON, or null.</param>
    /// <param name="secondary">The secondary window JSON, or null.</param>
    /// <param name="now">The refresh timestamp.</param>
    /// <returns>The normalized usage snapshot.</returns>
    private static UsageSnapshot Parse(string? primary, string? secondary, DateTimeOffset now)
    {
        List<string> properties = [];
        if (primary is not null)
        {
            properties.Add($"\"primary\":{primary}");
        }

        if (secondary is not null)
        {
            properties.Add($"\"secondary\":{secondary}");
        }

        string response =
            "{\"id\":2,\"result\":{\"rateLimitsByLimitId\":{\"codex\":{"
            + string.Join(',', properties)
            + "}}}}";
        return CodexRateLimitReader.ParseUsageResponse(response, now);
    }

    /// <summary>
    /// Builds one rate-limit window JSON object.
    /// </summary>
    /// <param name="usedPercent">The reported used percentage.</param>
    /// <param name="durationMinutes">The window duration in minutes.</param>
    /// <param name="reset">The Unix reset timestamp.</param>
    /// <returns>The serialized window.</returns>
    private static string Window(int usedPercent, long durationMinutes, long reset) =>
        $"{{\"usedPercent\":{usedPercent},\"windowDurationMins\":{durationMinutes},\"resetsAt\":{reset}}}";

    /// <summary>
    /// Verifies alert thresholds and deduplication across store instances.
    /// </summary>
    /// <param name="now">The timestamp used to create reset cycles.</param>
    private static void TestAlertPersistence(DateTimeOffset now)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "alerts.json");
        try
        {
            LimitReading low = new(LimitState.Available, 20, now.AddHours(1));
            Check(new AlertStateStore(path).ShouldAlert("five-hour", low), "First low alert was suppressed.");
            Check(!new AlertStateStore(path).ShouldAlert("five-hour", low), "Alert was not persisted.");
            Check(
                new AlertStateStore(path).ShouldAlert(
                    "five-hour",
                    low with { ResetsAt = now.AddHours(2) }),
                "A new reset cycle was suppressed.");
            Check(
                !new AlertStateStore(path).ShouldAlert(
                    "weekly",
                    new LimitReading(LimitState.Available, 21, now.AddDays(1))),
                "An alert above the threshold was emitted.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies reversible per-user startup registration with an isolated registry value.
    /// </summary>
    private static void TestStartupRegistration()
    {
        string valueName = $"CodexUsageTray.SelfTest.{Guid.NewGuid():N}";
        StartupRegistration registration = new(valueName);
        try
        {
            registration.SetEnabled(false);
            Check(!registration.IsEnabled(), "Startup registration began enabled.");
            registration.SetEnabled(true);
            Check(registration.IsEnabled(), "Startup registration was not enabled.");
            registration.SetEnabled(false);
            Check(!registration.IsEnabled(), "Startup registration was not disabled.");
        }
        finally
        {
            registration.SetEnabled(false);
        }
    }

    /// <summary>
    /// Verifies that a second named mutex detects an existing application instance.
    /// </summary>
    private static void TestDuplicateMutex()
    {
        string name = $"Local\\CodexUsageTray.SelfTest.{Guid.NewGuid():N}";
        using Mutex first = new(true, name, out bool firstCreated);
        using Mutex second = new(true, name, out bool secondCreated);
        Check(firstCreated && !secondCreated, "The duplicate-instance mutex was not detected.");
        first.ReleaseMutex();
    }

    /// <summary>
    /// Throws when a self-test condition is false.
    /// </summary>
    /// <param name="condition">The condition under test.</param>
    /// <param name="message">The failure message.</param>
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// Verifies that an action throws the requested exception type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="action">The action under test.</param>
    /// <param name="message">The failure message.</param>
    private static void ExpectThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Verifies that an asynchronous action throws the requested exception type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="action">The asynchronous action under test.</param>
    /// <param name="message">The failure message.</param>
    private static async Task ExpectThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Verifies the phone JSON contract, restart behavior, failure isolation, and atomic reads.
    /// </summary>
    /// <param name="now">The timestamp used for deterministic snapshots.</param>
    private static void TestJsonSnapshotPersistence(DateTimeOffset now)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "phone", "usage.json");
        UsageSnapshot snapshot = new(
            new LimitReading(LimitState.Available, 68, now.AddHours(2)),
            new LimitReading(LimitState.Available, 44, now.AddDays(4)),
            now,
            null);
        try
        {
            JsonSnapshotStore store = new(path);
            Check(!File.Exists(path), "Constructing the phone store wrote an initial snapshot.");
            store.Save(snapshot);
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
            {
                System.Text.Json.JsonElement root = document.RootElement;
                CheckJsonFields(root, "fiveHour", "weekly", "refreshedAt");
                Check(root.GetProperty("refreshedAt").GetDateTimeOffset().EqualsExact(now), "Phone refresh timestamp changed.");
                foreach (string key in new[] { "fiveHour", "weekly" })
                {
                    CheckJsonFields(root.GetProperty(key), "available", "remaining", "resetsAt");
                    Check(root.GetProperty(key).GetProperty("available").GetBoolean(), "Available phone limit was hidden.");
                }

                Check(root.GetProperty("fiveHour").GetProperty("remaining").GetInt32() == 68, "Phone five-hour quota differed from the UI.");
                Check(root.GetProperty("weekly").GetProperty("remaining").GetInt32() == 44, "Phone weekly quota differed from the UI.");
                Check(root.GetProperty("fiveHour").GetProperty("resetsAt").GetDateTimeOffset().EqualsExact(now.AddHours(2)), "Phone five-hour reset changed.");
                Check(root.GetProperty("weekly").GetProperty("resetsAt").GetDateTimeOffset().EqualsExact(now.AddDays(4)), "Phone weekly reset changed.");
            }

            string lastGood = File.ReadAllText(path);
            JsonSnapshotStore restarted = new(path);
            Check(File.ReadAllText(path) == lastGood, "Restart discarded the last phone snapshot.");
            restarted.Save(UsageSnapshot.Error("Sensitive error metadata must never be exported."));
            restarted.Save(snapshot with { Weekly = new LimitReading(LimitState.Error, null, null) });
            Check(File.ReadAllText(path) == lastGood, "An error snapshot replaced the last good phone JSON.");

            // An incomplete temporary file left by a terminated process must never be promoted.
            File.WriteAllText(path + ".tmp", "{incomplete");
            restarted.Save(snapshot with { RefreshedAt = now.AddMinutes(5) });
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
            {
                Check(document.RootElement.GetProperty("refreshedAt").GetDateTimeOffset().EqualsExact(now.AddMinutes(5)), "Export timestamp did not advance after restart.");
            }

            Check(!File.Exists(path + ".tmp"), "JSON temporary file remained after success.");

            // Test both directions of a missing limit, including inconsistent nullable input.
            foreach (bool missingWeekly in new[] { true, false })
            {
                LimitReading available = new(LimitState.Available, 0, null);
                LimitReading unavailable = missingWeekly
                    ? new(LimitState.Unavailable, 99, now)
                    : new(LimitState.Available, null, now);
                store.Save(snapshot with
                {
                    FiveHour = missingWeekly ? available : unavailable,
                    Weekly = missingWeekly ? unavailable : available,
                });
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                System.Text.Json.JsonElement present = document.RootElement.GetProperty(missingWeekly ? "fiveHour" : "weekly");
                System.Text.Json.JsonElement missing = document.RootElement.GetProperty(missingWeekly ? "weekly" : "fiveHour");
                Check(present.GetProperty("available").GetBoolean() && present.GetProperty("remaining").GetInt32() == 0, "An exhausted phone limit was marked unavailable.");
                Check(present.GetProperty("resetsAt").ValueKind == System.Text.Json.JsonValueKind.Null, "A missing reset was invented.");
                Check(!missing.GetProperty("available").GetBoolean(), "A missing phone limit was marked available.");
                Check(missing.GetProperty("remaining").ValueKind == System.Text.Json.JsonValueKind.Null, "A missing phone limit became a percentage.");
                Check(missing.GetProperty("resetsAt").ValueKind == System.Text.Json.JsonValueKind.Null, "An unavailable phone limit exposed stale reset data.");
            }

            store.Save(snapshot with
            {
                FiveHour = snapshot.FiveHour with { RemainingPercent = -10 },
                Weekly = snapshot.Weekly with { RemainingPercent = 150 },
            });
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
            {
                Check(document.RootElement.GetProperty("fiveHour").GetProperty("remaining").GetInt32() == 0, "Export quota was not clamped at zero.");
                Check(document.RootElement.GetProperty("weekly").GetProperty("remaining").GetInt32() == 100, "Export quota was not clamped at one hundred.");
            }

            store.Save(snapshot);
            lastGood = File.ReadAllText(path);
            using (FileStream lockedDestination = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                store.Save(snapshot with { RefreshedAt = now.AddMinutes(10) });
                Check(File.ReadAllText(path) == lastGood, "A blocked replacement damaged the last good phone JSON.");
            }

            Check(!File.Exists(path + ".tmp"), "A failed replacement left temporary JSON behind.");
            using (FileStream lockedTemporary = new(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                store.Save(snapshot with { RefreshedAt = now.AddMinutes(10) });
                Check(File.ReadAllText(path) == lastGood, "A failed temporary write damaged the last good phone JSON.");
            }

            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                store.Save(snapshot with { RefreshedAt = now.AddMinutes(10) });
                Check(File.ReadAllText(path) == lastGood, "A denied export changed the last good phone JSON.");
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            string blockedDirectory = Path.Combine(directory, "blocked");
            File.WriteAllText(blockedDirectory, "A file blocks directory creation.");
            new JsonSnapshotStore(Path.Combine(blockedDirectory, "usage.json")).Save(snapshot);
            Check(File.ReadAllText(blockedDirectory) == "A file blocks directory creation.", "Failed directory creation modified an unrelated file.");

            // Keep a phone-like reader open across each replacement. It must see a complete
            // old document while a new reader sees the complete new document.
            for (int index = 1; index <= 50; index++)
            {
                string before = File.ReadAllText(path);
                using FileStream openRead = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                UsageSnapshot next = snapshot with { RefreshedAt = now.AddMinutes(20 + index) };
                store.Save(next);
                using StreamReader reader = new(openRead);
                Check(reader.ReadToEnd() == before, "An in-flight phone read saw a modified document.");
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                Check(document.RootElement.GetProperty("refreshedAt").GetDateTimeOffset().EqualsExact(next.RefreshedAt), "JSON replacement failed to recover or publish the new timestamp.");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Rejects extra phone JSON fields, including accidental snapshot or account metadata.
    /// </summary>
    /// <param name="element">The serialized JSON object.</param>
    /// <param name="names">The only permitted property names.</param>
    private static void CheckJsonFields(System.Text.Json.JsonElement element, params string[] names)
    {
        Check(
            element.EnumerateObject().Select(property => property.Name).Order()
                .SequenceEqual(names.Order()),
            "Export JSON contained missing, extra, or incorrectly named fields.");
    }


    /// <summary>
    /// Verifies default-off consent, persisted destination changes, restart, and settings failures.
    /// </summary>
    private static void TestJsonExportConsent(DateTimeOffset now)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-{Guid.NewGuid():N}");
        string preferences = Path.Combine(directory, "settings.json");
        string firstPath = Path.Combine(directory, "first", "usage.json");
        string secondPath = Path.Combine(directory, "second", "usage.json");
        UsageSnapshot snapshot = new(
            new LimitReading(LimitState.Available, 68, now.AddHours(2)),
            new LimitReading(LimitState.Available, 44, now.AddDays(4)), now, null);
        try
        {
            JsonExportSettingsStore settings = new(preferences);
            JsonExportController export = new(settings);
            Check(!export.Settings.Enabled, "A fresh install enabled JSON export.");
            Check(!File.Exists(preferences), "Loading defaults wrote preferences.");
            Check(export.TryConfigure(new(false, firstPath)), "Could not configure a disabled destination.");
            export.Save(snapshot);
            Check(!File.Exists(firstPath), "Disabled export created a snapshot.");
            Check(!Directory.Exists(Path.GetDirectoryName(firstPath)), "Disabled export created the output directory.");
            Check(export.TryConfigure(new(true, firstPath)), "Could not enable export.");
            Check(!File.Exists(firstPath), "Enabling export published a cached snapshot.");
            export.Save(snapshot);
            string lastGood = File.ReadAllText(firstPath);
            JsonExportController restarted = new(new JsonExportSettingsStore(preferences));
            Check(restarted.Settings.Enabled && restarted.Settings.OutputPath == firstPath, "Export preferences did not survive restart.");
            Check(restarted.TryConfigure(new(true, secondPath)), "Could not change the export destination.");
            restarted.Save(snapshot with { RefreshedAt = now.AddMinutes(5) });
            Check(File.Exists(secondPath), "Configured output was not written.");
            Check(File.ReadAllText(firstPath) == lastGood, "Changing output modified the old snapshot.");
            Check(restarted.TryConfigure(restarted.Settings with { Enabled = false }), "Could not disable export.");
            string secondGood = File.ReadAllText(secondPath);
            restarted.Save(snapshot with { RefreshedAt = now.AddMinutes(10) });
            Check(File.ReadAllText(secondPath) == secondGood, "Disabling changed the last published snapshot.");
            Check(!new JsonExportController(settings).Settings.Enabled, "Disabling did not survive restart.");

            // A blocked preference write must never grant new consent or switch the output path.
            using (FileStream locked = new(preferences, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(!restarted.TryConfigure(new(true, firstPath)), "A blocked settings write reported success.");
                Check(!restarted.Settings.Enabled, "Failed preference persistence enabled export.");
            }

            Check(restarted.TryConfigure(new(true, secondPath)), "Could not re-enable export after recovery.");
            using (FileStream locked = new(preferences, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(!restarted.TryConfigure(new(true, firstPath)), "A blocked path change reported success.");
                Check(restarted.Settings.OutputPath == secondPath, "A failed settings write changed the destination.");
                Check(!restarted.TryConfigure(restarted.Settings with { Enabled = false }), "A blocked disable preference reported success.");
                Check(!restarted.Settings.Enabled, "Failed persistence did not disable export for this session.");
                restarted.Save(snapshot with { RefreshedAt = now.AddMinutes(15) });
                Check(File.ReadAllText(secondPath) == secondGood, "A session-disabled export still wrote data.");
            }

            Check(restarted.TryConfigure(restarted.Settings with { Enabled = false }), "Could not persist disable after recovery.");
            Check(!new JsonExportController(settings).Settings.Enabled, "Recovered disable was lost on restart.");
            Check(!restarted.TryConfigure(new(true, "relative.json")), "A relative export destination was accepted.");
            Check(!restarted.TryConfigure(new(true, firstPath + "\0")), "An invalid export destination was accepted.");
            File.WriteAllText(preferences, "{broken");
            Check(!new JsonExportController(settings).Settings.Enabled, "Corrupt settings enabled export.");
            File.WriteAllText(preferences, "{\"Enabled\":true,\"OutputPath\":null}");
            Check(!new JsonExportController(settings).Settings.Enabled, "Invalid settings enabled export.");
            using (FileStream locked = new(preferences, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Check(!new JsonExportController(settings).Settings.Enabled, "Unreadable settings enabled export.");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class BlockingTextReader : TextReader
    {
        /// <summary>
        /// Waits indefinitely until the supplied cancellation token is canceled.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token under test.</param>
        /// <returns>No line; this operation completes only through cancellation.</returns>
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }
}
