using System.Text.Json;

namespace CodexUsageTray;

internal sealed record UsageHistoryPoint(
    DateTimeOffset RecordedAt,
    int RemainingPercent);

internal sealed class UsageHistoryStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly string filePath;
    private readonly List<UsageHistoryPoint> points;

    /// <summary>
    /// Creates the weekly usage-history store at the standard local application-data path.
    /// </summary>
    /// <param name="filePath">An optional file path used by self-tests.</param>
    internal UsageHistoryStore(string? filePath = null)
    {
        this.filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexUsageTray",
            "usage-history.json");
        points = Load(this.filePath);
        Prune(DateTimeOffset.Now);
    }

    /// <summary>
    /// Gets the retained weekly usage samples.
    /// </summary>
    internal IReadOnlyList<UsageHistoryPoint> Points => points;

    /// <summary>
    /// Records one available weekly remaining-percentage sample.
    /// </summary>
    /// <param name="reading">The normalized weekly limit reading.</param>
    /// <param name="recordedAt">The sample timestamp.</param>
    internal void Record(LimitReading reading, DateTimeOffset recordedAt)
    {
        if (reading.State != LimitState.Available
            || reading.RemainingPercent is not int remainingPercent)
        {
            return;
        }

        points.Add(new UsageHistoryPoint(recordedAt, Math.Clamp(remainingPercent, 0, 100)));
        Prune(recordedAt);
        Save();
    }

    /// <summary>
    /// Removes samples older than the retention period.
    /// </summary>
    /// <param name="now">The timestamp defining the retention window.</param>
    private void Prune(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - Retention;
        points.RemoveAll(point => point.RecordedAt < cutoff);
        points.Sort((left, right) => left.RecordedAt.CompareTo(right.RecordedAt));
    }

    /// <summary>
    /// Loads persisted samples, treating missing or invalid state as empty.
    /// </summary>
    /// <param name="path">The usage-history JSON file.</param>
    /// <returns>The persisted samples.</returns>
    private static List<UsageHistoryPoint> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<UsageHistoryPoint>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Persists the retained samples to local application data.
    /// </summary>
    private void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(points));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // History persistence must not hide otherwise valid usage data.
        }
    }
}
