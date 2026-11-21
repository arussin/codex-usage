using System.Text.Json;

namespace CodexUsageTray;

/// <summary>
/// The quota windows that can each be shown as their own tray number.
/// </summary>
internal enum LimitWindow
{
    Weekly,
    FiveHour,
    Scoped,
}

/// <summary>
/// One independent usage source. Each has its own reader, tray numbers, popup, history and export file,
/// so a source can be switched off, or deleted from <see cref="UsageSources.All"/>, without touching another.
/// </summary>
/// <param name="Key">The stable lower-case name used in settings.</param>
/// <param name="DisplayName">The user-facing name.</param>
/// <param name="CreateReader">Creates the source's usage reader.</param>
/// <param name="FrameColor">The colour of the frame drawn around this source's tray numbers.</param>
/// <param name="FrameColorName">That colour in words, for the menu.</param>
/// <param name="HistoryFileName">The weekly history file kept in local application data.</param>
/// <param name="ExportFileName">The JSON file written beside the configured export file, or null for that file itself.</param>
/// <param name="AlertKeyPrefix">The prefix of this source's alert keys, empty for the original source.</param>
/// <param name="ScopedRows">How many scoped weekly limits the popup has room for.</param>
/// <param name="DefaultNumbers">The tray numbers shown before the user chooses.</param>
internal sealed record UsageSource(
    string Key,
    string DisplayName,
    Func<IUsageReader> CreateReader,
    Color FrameColor,
    string FrameColorName,
    string HistoryFileName,
    string? ExportFileName,
    string AlertKeyPrefix,
    int ScopedRows,
    IReadOnlyList<LimitWindow> DefaultNumbers)
{
    /// <summary>
    /// Gets the windows this source can show as tray numbers, in the order their icons are created.
    /// </summary>
    internal IReadOnlyList<LimitWindow> Windows =>
        ScopedRows > 0
            ? [LimitWindow.Weekly, LimitWindow.FiveHour, LimitWindow.Scoped]
            : [LimitWindow.Weekly, LimitWindow.FiveHour];

    /// <summary>
    /// Names one window for menus and tooltips.
    /// </summary>
    /// <param name="window">The window to name.</param>
    /// <param name="scopedLabel">The provider's label for a scoped limit, when known.</param>
    /// <returns>A short window name without the source name.</returns>
    internal static string WindowName(LimitWindow window, string? scopedLabel = null) => window switch
    {
        LimitWindow.FiveHour => "5-hour",
        LimitWindow.Weekly => "weekly",
        _ => scopedLabel is null ? "model weekly" : $"{scopedLabel} weekly",
    };
}

internal static class UsageSources
{
    // Existing installs keep their history, alert state, export file and tray icon unchanged.
    internal static UsageSource Codex { get; } = new(
        "codex",
        "Codex",
        () => new CodexRateLimitReader(),
        Color.FromArgb(0x2D, 0xB8, 0xA0),
        "teal",
        "usage-history.json",
        ExportFileName: null,
        AlertKeyPrefix: string.Empty,
        ScopedRows: 0,
        DefaultNumbers: [LimitWindow.Weekly]);

    // Off until the user ticks a Claude tray number.
    internal static UsageSource Claude { get; } = new(
        "claude",
        "Claude",
        () => new ClaudeUsageReader(),
        Color.FromArgb(0xD9, 0x77, 0x57),
        "orange",
        "claude-usage-history.json",
        ExportFileName: "claude.json",
        AlertKeyPrefix: "claude-",
        ScopedRows: 2,
        DefaultNumbers: []);

    /// <summary>
    /// Gets every source the tray offers. To remove one for good, delete its entry here and its reader file.
    /// </summary>
    internal static IReadOnlyList<UsageSource> All { get; } = [Codex, Claude];
}

/// <summary>
/// Persists which quota windows are shown as tray numbers. A source with no numbers is not polled.
/// </summary>
internal sealed class TrayNumberStore
{
    private readonly string filePath;
    private readonly Dictionary<string, HashSet<LimitWindow>> numbers = [];

    /// <summary>
    /// Loads the saved choice, using each source's defaults when it is missing or unreadable.
    /// </summary>
    /// <param name="sources">The sources the tray offers.</param>
    /// <param name="filePath">An optional file path used by self-tests.</param>
    internal TrayNumberStore(IReadOnlyList<UsageSource> sources, string? filePath = null)
    {
        this.filePath = filePath ?? Path.Combine(PhoneNotificationFiles.DefaultDirectory, "tray-numbers.json");
        Dictionary<string, string[]> saved =
            PhoneNotificationFiles.Load(this.filePath, new Dictionary<string, string[]>());
        foreach (UsageSource source in sources)
        {
            numbers[source.Key] = saved.TryGetValue(source.Key, out string[]? names) && names is not null
                ? names
                    .Select(name => Enum.TryParse(name, out LimitWindow window) ? window : (LimitWindow?)null)
                    .Where(window => window is LimitWindow known && source.Windows.Contains(known))
                    .Select(window => window!.Value)
                    .ToHashSet()
                : [.. source.DefaultNumbers];
        }

        // The tray menu is only reachable through an icon, so never start with none.
        if (sources.Count > 0 && numbers.Values.All(windows => windows.Count == 0))
        {
            numbers[sources[0].Key].Add(LimitWindow.Weekly);
        }
    }

    /// <summary>
    /// Reports whether a window is shown as a tray number.
    /// </summary>
    /// <param name="source">The source that owns the window.</param>
    /// <param name="window">The window.</param>
    /// <returns><see langword="true"/> when the number is shown.</returns>
    internal bool IsShown(UsageSource source, LimitWindow window) =>
        numbers.TryGetValue(source.Key, out HashSet<LimitWindow>? windows) && windows.Contains(window);

    /// <summary>
    /// Gets how many tray numbers are shown across all sources.
    /// </summary>
    internal int ShownCount => numbers.Values.Sum(windows => windows.Count);

    /// <summary>
    /// Reports whether a source has any tray number and is therefore polled.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns><see langword="true"/> when at least one of its numbers is shown.</returns>
    internal bool IsActive(UsageSource source) =>
        numbers.TryGetValue(source.Key, out HashSet<LimitWindow>? windows) && windows.Count > 0;

    /// <summary>
    /// Shows or hides one tray number. The last remaining number cannot be hidden.
    /// </summary>
    /// <param name="source">The source that owns the window.</param>
    /// <param name="window">The window.</param>
    /// <param name="shown">Whether the number should be shown.</param>
    /// <returns><see langword="true"/> when the choice changed and was saved.</returns>
    internal bool TrySet(UsageSource source, LimitWindow window, bool shown)
    {
        if (!numbers.TryGetValue(source.Key, out HashSet<LimitWindow>? windows)
            || !source.Windows.Contains(window)
            || windows.Contains(window) == shown
            || (!shown && ShownCount <= 1))
        {
            return false;
        }

        HashSet<LimitWindow> previous = [.. windows];
        _ = shown ? windows.Add(window) : windows.Remove(window);
        Dictionary<string, string[]> document = numbers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Order().Select(value => value.ToString()).ToArray());
        if (PhoneNotificationFiles.Save(filePath, document))
        {
            return true;
        }

        numbers[source.Key] = previous;
        return false;
    }
}
