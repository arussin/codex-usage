namespace CodexUsageTray;

internal sealed class JsonExportController
{
    private readonly JsonExportSettingsStore settingsStore;

    internal JsonExportSettings Settings { get; private set; }

    internal JsonExportController(JsonExportSettingsStore? settingsStore = null)
    {
        this.settingsStore = settingsStore ?? new JsonExportSettingsStore();
        Settings = this.settingsStore.Load();
    }

    /// <summary>
    /// Persists consent before enabling export or changing its destination.
    /// Disabling always takes effect for this session, even if persistence fails.
    /// </summary>
    internal bool TryConfigure(JsonExportSettings settings)
    {
        if (!JsonExportSettingsStore.IsValid(settings))
        {
            return false;
        }

        if (!settings.Enabled)
        {
            Settings = settings;
        }

        if (!settingsStore.TrySave(settings))
        {
            return false;
        }

        Settings = settings;
        return true;
    }

    /// <summary>
    /// Exports only after explicit consent; disabled export never touches snapshot files.
    /// </summary>
    /// <param name="snapshot">The already-normalized snapshot from a successful fetch.</param>
    /// <param name="siblingFileName">
    /// A file name beside the configured output for an additional source, or null for the configured file.
    /// </param>
    internal void Save(UsageSnapshot snapshot, string? siblingFileName = null)
    {
        if (Settings.Enabled)
        {
            string path = siblingFileName is null
                ? Settings.OutputPath
                : SiblingPath(Settings.OutputPath, siblingFileName);
            new JsonSnapshotStore(path).Save(snapshot);
        }
    }

    /// <summary>
    /// Places an additional source's file in the configured output directory without ever
    /// letting it replace the configured file.
    /// </summary>
    /// <param name="outputPath">The configured output file.</param>
    /// <param name="fileName">The additional source's file name.</param>
    /// <returns>The full path of the additional file.</returns>
    internal static string SiblingPath(string outputPath, string fileName)
    {
        string directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        string sibling = Path.Combine(directory, fileName);
        return string.Equals(sibling, outputPath, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(fileName) + "-usage" + Path.GetExtension(fileName))
            : sibling;
    }
}
