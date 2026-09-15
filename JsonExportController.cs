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
    internal void Save(UsageSnapshot snapshot)
    {
        if (Settings.Enabled)
        {
            new JsonSnapshotStore(Settings.OutputPath).Save(snapshot);
        }
    }
}
