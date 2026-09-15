using System.Text.Json;

namespace CodexUsageTray;

internal sealed record JsonExportSettings(bool Enabled, string OutputPath)
{
    internal static JsonExportSettings Default => new(false, JsonSnapshotStore.DefaultFilePath);
}

internal sealed class JsonExportSettingsStore
{
    private readonly string filePath;

    internal JsonExportSettingsStore(string? filePath = null)
    {
        this.filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexUsageTray",
            "json-export-settings.json");
    }

    internal JsonExportSettings Load()
    {
        try
        {
            JsonExportSettings? settings = File.Exists(filePath)
                ? JsonSerializer.Deserialize<JsonExportSettings>(File.ReadAllText(filePath))
                : null;
            return settings is not null && IsValid(settings)
                ? settings
                : JsonExportSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Missing, unreadable, or corrupt preferences must never opt the user in.
            return JsonExportSettings.Default;
        }
    }

    internal static bool IsValid(JsonExportSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OutputPath) || !Path.IsPathFullyQualified(settings.OutputPath))
        {
            return false;
        }

        try
        {
            string name = Path.GetFileName(Path.GetFullPath(settings.OutputPath));
            return !string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    internal bool TrySave(JsonExportSettings settings)
    {
        if (!IsValid(settings))
        {
            return false;
        }

        string temporaryPath = filePath + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
            if (File.Exists(filePath))
            {
                File.Replace(temporaryPath, filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A future preference change can overwrite a leftover temporary file.
            }
        }
    }
}
