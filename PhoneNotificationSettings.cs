using System.Text.Json;
using System.Text.RegularExpressions;
namespace CodexUsageTray;
internal sealed record PhoneNotificationSettings
{
    public bool Enabled { get; init; }
    public bool Weekly { get; init; } = true;
    public bool FiveHour { get; init; }
    public bool Recovery { get; init; } = true;
    public bool Low { get; init; }
    public string Server { get; init; } = "https://ntfy.sh";
    public string Topic { get; init; } = "";
    internal static string NewTopic() => "codex-" + Guid.NewGuid().ToString("N");
    internal bool HasDestination =>
        Uri.TryCreate(Server, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && Topic is not null && Regex.IsMatch(Topic, "\\A[A-Za-z0-9_-]{1,64}\\z");
    internal bool IsValid => HasDestination || (!Enabled && string.IsNullOrEmpty(Topic));
}
// Separate from the public JSON export. Missing/corrupt preferences never opt in.
internal static class PhoneNotificationFiles
{
    internal static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageTray");
    internal static T Load<T>(string path, T fallback)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? fallback : fallback; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return fallback; }
    }
    internal static bool Save<T>(string path, T value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
