namespace CodexUsageTray;

internal enum LimitState
{
    Available,
    Unavailable,
    Error,
}

internal sealed record LimitReading(
    LimitState State,
    int? RemainingPercent,
    DateTimeOffset? ResetsAt);

/// <summary>
/// A weekly limit that applies to one model or surface, such as a per-model weekly limit.
/// </summary>
/// <param name="Label">The provider's display label for what the limit covers.</param>
/// <param name="Reading">The normalized limit reading.</param>
internal sealed record ScopedLimit(
    string Label,
    LimitReading Reading);

internal sealed record UsageSnapshot(
    LimitReading FiveHour,
    LimitReading Weekly,
    DateTimeOffset RefreshedAt,
    string? ErrorMessage)
{
    /// <summary>
    /// Gets the scoped weekly limits a source reports beside its general windows. Empty for most sources.
    /// </summary>
    internal IReadOnlyList<ScopedLimit> Scoped { get; init; } = [];

    /// <summary>
    /// Creates a snapshot where both windows are unavailable because a refresh failed.
    /// </summary>
    /// <param name="message">The actionable failure message.</param>
    /// <returns>An error snapshot.</returns>
    internal static UsageSnapshot Error(string message) => new(
        new LimitReading(LimitState.Error, null, null),
        new LimitReading(LimitState.Error, null, null),
        DateTimeOffset.Now,
        message);

    /// <summary>
    /// Creates an initial snapshot before the first refresh completes.
    /// </summary>
    /// <returns>An unavailable snapshot.</returns>
    internal static UsageSnapshot Initial() => new(
        new LimitReading(LimitState.Unavailable, null, null),
        new LimitReading(LimitState.Unavailable, null, null),
        DateTimeOffset.Now,
        null);
}

/// <summary>
/// Durations of the two general rate-limit windows every source reports.
/// </summary>
internal static class LimitWindows
{
    internal const long FiveHourMinutes = 300;
    internal const long WeeklyMinutes = 10_080;
}

/// <summary>
/// Reads one source's current usage through that source's own signed-in command-line tool.
/// </summary>
internal interface IUsageReader
{
    /// <summary>
    /// Reads the current normalized snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for application shutdown.</param>
    /// <returns>The normalized snapshot.</returns>
    Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default);
}
