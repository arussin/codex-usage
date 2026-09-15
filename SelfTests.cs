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
            DateTimeOffset now = DateTimeOffset.Now;
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

            TestWeeklyDailyRates(now);
            TestWeeklyEndOfDayTarget(now);
            TestWeeklyLeft(now);
            TestUsageHistoryPersistence(now);
            TestJsonSnapshotPersistence(now);
            TestJsonExportConsent(now);
            TestPlotHistorySelection(now);
            TestUsagePrediction(now);
            TestHistoryCsv(now);
            TestPopupReopen();

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
        ApplicationConfiguration.Initialize();
        using UsagePopup popup = new();
        popup.Show();
        Size shownSize = popup.Size;
        foreach (Label label in popup.Controls.OfType<Label>())
        {
            Check(label.Height >= label.PreferredHeight, "Popup text was clipped at the current display DPI.");
            Check(popup.ClientRectangle.Contains(label.Bounds), "Popup text extended outside the window.");
        }

        popup.Close();
        Check(!popup.IsDisposed, "Closing the popup disposed it.");
        popup.Show();
        Check(popup.Size == shownSize, "Reopening the popup changed its scaled size.");
        popup.Hide();
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
