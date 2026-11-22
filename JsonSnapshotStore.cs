using System.Text.Json;

namespace CodexUsageTray;

internal sealed class JsonSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly string filePath;

    // Retain the existing directory so current private Serve mappings remain compatible.
    internal static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexUsagePhone",
        "usage.json");

    /// <summary>
    /// Creates the JSON snapshot store at the standard local application-data path.
    /// </summary>
    /// <param name="filePath">The configured destination, or the compatibility default when omitted.</param>
    internal JsonSnapshotStore(string? filePath = null)
    {
        this.filePath = filePath ?? DefaultFilePath;
    }

    /// <summary>
    /// Publishes a sanitized successful refresh without disturbing the last good file on failure.
    /// Calls are serialized by the tray's existing refresh guard and single-instance mutex.
    /// </summary>
    /// <param name="snapshot">The already-normalized snapshot from a successful fetch.</param>
    /// <param name="includeScoped">
    /// Whether to add the tightest scoped weekly limit as weeklyScoped. Sources without scoped
    /// limits leave it out, so their document keeps exactly its original three fields.
    /// </param>
    internal void Save(UsageSnapshot snapshot, bool includeScoped = false)
    {
        if (snapshot.ErrorMessage is not null
            || snapshot.FiveHour.State == LimitState.Error
            || snapshot.Weekly.State == LimitState.Error)
        {
            return;
        }

        string temporaryPath = filePath + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Explicitly allowlist fields; never serialize UsageSnapshot or its error metadata.
            object document = includeScoped
                ? new
                {
                    FiveHour = ToJsonLimit(snapshot.FiveHour),
                    Weekly = ToJsonLimit(snapshot.Weekly),
                    WeeklyScoped = ToJsonScoped(snapshot.TightestScoped()),
                    snapshot.RefreshedAt,
                }
                : new
                {
                    FiveHour = ToJsonLimit(snapshot.FiveHour),
                    Weekly = ToJsonLimit(snapshot.Weekly),
                    snapshot.RefreshedAt,
                };
            string json = JsonSerializer.Serialize(document, JsonOptions);
            File.WriteAllText(temporaryPath, json);

            // The completed temporary file is on the same volume. Never truncate usage.json.
            if (File.Exists(filePath))
            {
                File.Replace(temporaryPath, filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Like history and alert-state persistence, export must not fail the quota refresh.
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A later successful refresh will overwrite any leftover temporary file.
            }
        }
    }

    /// <summary>
    /// Uses the popup's availability and percentage rules, preserving nullable reset times.
    /// </summary>
    /// <param name="reading">One normalized limit reading.</param>
    /// <returns>Only the fields needed by the consumer.</returns>
    private static JsonLimit ToJsonLimit(LimitReading reading)
    {
        bool available = reading.State == LimitState.Available && reading.RemainingPercent is int;
        return new JsonLimit(
            available,
            available ? Math.Clamp(reading.RemainingPercent!.Value, 0, 100) : null,
            available ? reading.ResetsAt : null);
    }

    /// <summary>
    /// Publishes one scoped weekly limit with the label the source gave it, or an unavailable entry.
    /// </summary>
    /// <param name="scoped">The tightest scoped limit, or null when the source reports none.</param>
    /// <returns>The label and the same three fields every limit carries.</returns>
    private static JsonScopedLimit ToJsonScoped(ScopedLimit? scoped)
    {
        if (scoped is null)
        {
            return new JsonScopedLimit(null, false, null, null);
        }

        JsonLimit limit = ToJsonLimit(scoped.Reading);
        string label = new(scoped.Label.Where(character => !char.IsControl(character)).Take(MaxLabelLength).ToArray());
        return new JsonScopedLimit(limit.Available ? label.Trim() : null, limit.Available, limit.Remaining, limit.ResetsAt);
    }

    private const int MaxLabelLength = 32;

    private sealed record JsonLimit(bool Available, int? Remaining, DateTimeOffset? ResetsAt);

    private sealed record JsonScopedLimit(string? Label, bool Available, int? Remaining, DateTimeOffset? ResetsAt);
}
