using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodexUsageTray;

internal sealed class ClaudeUsageException : Exception
{
    /// <summary>
    /// Creates an exception for an actionable Claude usage-read failure.
    /// </summary>
    /// <param name="message">The failure message.</param>
    internal ClaudeUsageException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception for an actionable Claude usage-read failure with an inner error.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying error.</param>
    internal ClaudeUsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reads Claude plan usage by asking the signed-in Claude Code CLI for its /usage report.
/// The command runs locally in the CLI, makes no model request, and never exposes credentials to this app.
/// </summary>
internal sealed class ClaudeUsageReader : IUsageReader
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    // Print mode with no saved session, no user settings (so no hooks or plugins run) and no MCP servers.
    private static readonly string[] Arguments =
    [
        "-p",
        "/usage",
        "--output-format",
        "stream-json",
        "--verbose",
        "--no-session-persistence",
        "--strict-mcp-config",
        "--setting-sources",
        string.Empty,
    ];

    /// <summary>
    /// Reads the current Claude plan usage through the installed Claude Code CLI.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for application shutdown.</param>
    /// <returns>The normalized five-hour, weekly and scoped weekly snapshot.</returns>
    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(RequestTimeout);

        try
        {
            return await FetchCoreAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ClaudeUsageException(
                "Claude usage request timed out. Check that Claude Code is installed and signed in.",
                exception);
        }
        catch (Win32Exception exception)
        {
            throw new ClaudeUsageException(
                "Could not start Claude Code. Install the Claude desktop app or the Claude Code CLI.",
                exception);
        }
        catch (IOException exception)
        {
            throw new ClaudeUsageException(
                "Could not communicate with Claude Code. Try again.",
                exception);
        }
    }

    /// <summary>
    /// Runs one /usage command and always terminates the child process.
    /// </summary>
    /// <param name="cancellationToken">Timeout and shutdown cancellation token.</param>
    /// <returns>The normalized usage snapshot.</returns>
    private static async Task<UsageSnapshot> FetchCoreAsync(CancellationToken cancellationToken)
    {
        string claude = FindClaude()
            ?? throw new ClaudeUsageException(
                "Claude Code was not found. Install the Claude desktop app or the Claude Code CLI.");
        using Process process = new() { StartInfo = CreateStartInfo(claude) };
        if (!process.Start())
        {
            throw new ClaudeUsageException("Could not start Claude Code.");
        }

        try
        {
            // The CLI treats piped standard input as extra prompt text; close it so the command runs at once.
            process.StandardInput.Close();
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await errorTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return ParseUsageOutput(output, DateTimeOffset.Now);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>
    /// Builds the hidden print-mode process configuration.
    /// </summary>
    /// <param name="claude">The absolute Claude Code executable path.</param>
    /// <returns>The process configuration.</returns>
    private static ProcessStartInfo CreateStartInfo(string claude)
    {
        ProcessStartInfo startInfo = new(claude)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (string argument in Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // A tray started from inside a Claude Code session must not look like that session's child.
        // CLAUDE_CONFIG_DIR stays: it says where the user's own sign-in lives.
        foreach (string name in startInfo.Environment.Keys.Where(IsHostSessionVariable).ToArray())
        {
            startInfo.Environment.Remove(name);
        }

        // Keeps the CLI from creating a memory folder for this working directory on every poll.
        startInfo.Environment["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "1";
        return startInfo;
    }

    /// <summary>
    /// Identifies environment variables that a hosting Claude Code session passes to its children.
    /// </summary>
    /// <param name="name">The environment variable name.</param>
    /// <returns><see langword="true"/> when the variable must not reach the usage read.</returns>
    internal static bool IsHostSessionVariable(string name) =>
        name.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("CLAUDE_CONFIG_DIR", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Finds a Claude Code executable: the standalone install, one on PATH, then the desktop app's bundled copy.
    /// </summary>
    /// <returns>The absolute executable path, or null when none is accessible.</returns>
    private static string? FindClaude()
    {
        try
        {
            string standalone = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "bin",
                "claude.exe");
            if (File.Exists(standalone))
            {
                return standalone;
            }

            string? onPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(directory => Path.Combine(directory, "claude.exe"))
                .FirstOrDefault(File.Exists);
            return onPath ?? FindDesktopClaude();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the newest CLI bundled with the Claude desktop app. Its folder changes with every app update.
    /// </summary>
    /// <returns>The absolute executable path, or null when none is accessible.</returns>
    private static string? FindDesktopClaude()
    {
        string versions = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude",
            "claude-code");
        return Directory.Exists(versions)
            ? Directory.EnumerateDirectories(versions, "*", SearchOption.TopDirectoryOnly)
                .SelectMany(version => Directory.EnumerateDirectories(version, "*", SearchOption.TopDirectoryOnly))
                .Select(build => new FileInfo(Path.Combine(build, "claude.exe")))
                .Where(file => file.Exists)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Finds the structured /usage report in the CLI's stream-json output and normalizes it.
    /// </summary>
    /// <param name="output">The complete standard output of the print-mode command.</param>
    /// <param name="refreshedAt">The local refresh timestamp.</param>
    /// <returns>The normalized snapshot.</returns>
    internal static UsageSnapshot ParseUsageOutput(string output, DateTimeOffset refreshedAt)
    {
        bool answered = false;
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line[0] != '{')
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                JsonElement message = document.RootElement;
                if (!message.TryGetProperty("type", out JsonElement type)
                    || type.ValueKind != JsonValueKind.String
                    || type.GetString() != "assistant")
                {
                    continue;
                }

                answered = true;
                if (message.TryGetProperty("usage_report", out JsonElement report)
                    && report.ValueKind == JsonValueKind.Object)
                {
                    return ParseReport(report, refreshedAt);
                }
            }
        }

        // A signed-out or API-key CLI answers /usage with session costs only, with no plan report.
        throw new ClaudeUsageException(answered
            ? "Claude Code reported no plan usage. Sign in with your Claude subscription: run 'claude auth login'."
            : "Claude Code returned no usage report. Update Claude Code and try again.");
    }

    /// <summary>
    /// Normalizes the plan-usage rows of one /usage report.
    /// </summary>
    /// <param name="report">The usage_report object.</param>
    /// <param name="refreshedAt">The local refresh timestamp.</param>
    /// <returns>The normalized snapshot.</returns>
    private static UsageSnapshot ParseReport(JsonElement report, DateTimeOffset refreshedAt)
    {
        if (!report.TryGetProperty("rate_limits", out JsonElement rateLimits)
            || rateLimits.ValueKind != JsonValueKind.Object
            || !rateLimits.TryGetProperty("limits", out JsonElement limits)
            || limits.ValueKind != JsonValueKind.Array)
        {
            // The CLI reports null here while its own usage fetch is failing or throttled.
            throw new ClaudeUsageException("Claude Code could not fetch plan limits right now.");
        }

        LimitReading fiveHour = new(LimitState.Unavailable, null, null);
        LimitReading weekly = new(LimitState.Unavailable, null, null);
        List<ScopedLimit> scoped = [];
        foreach (JsonElement row in limits.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !row.TryGetProperty("kind", out JsonElement kind)
                || kind.ValueKind != JsonValueKind.String
                || !row.TryGetProperty("percent", out JsonElement percent)
                || percent.ValueKind != JsonValueKind.Number
                || !percent.TryGetDouble(out double usedPercent)
                || !double.IsFinite(usedPercent))
            {
                continue;
            }

            int remaining = (int)Math.Clamp(Math.Round(100d - usedPercent), 0d, 100d);
            LimitReading reading = new(LimitState.Available, remaining, ReadResetTime(row));
            switch (kind.GetString())
            {
                case "session" when fiveHour.State != LimitState.Available:
                    fiveHour = reading;
                    break;
                case "weekly_all" when weekly.State != LimitState.Available:
                    weekly = reading;
                    break;
                case "weekly_scoped":
                    scoped.Add(new ScopedLimit(ReadScopeLabel(row), reading));
                    break;
                default:
                    // The server may add meters; ones this app does not know are not shown.
                    break;
            }
        }

        return new UsageSnapshot(fiveHour, weekly, refreshedAt, null) { Scoped = scoped };
    }

    /// <summary>
    /// Converts a valid ISO 8601 reset timestamp to local time.
    /// </summary>
    /// <param name="row">The usage row object.</param>
    /// <returns>The local reset timestamp, or null when unavailable or invalid.</returns>
    private static DateTimeOffset? ReadResetTime(JsonElement row) =>
        row.TryGetProperty("resets_at", out JsonElement reset)
        && reset.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(
            reset.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset resetsAt)
            ? resetsAt.ToLocalTime()
            : null;

    /// <summary>
    /// Reads what a scoped row covers, using the server's own display label.
    /// </summary>
    /// <param name="row">The usage row object.</param>
    /// <returns>The model or surface label, or a generic label when absent.</returns>
    private static string ReadScopeLabel(JsonElement row)
    {
        if (row.TryGetProperty("scope", out JsonElement scope) && scope.ValueKind == JsonValueKind.Object)
        {
            foreach (string target in new[] { "model", "surface" })
            {
                if (scope.TryGetProperty(target, out JsonElement value)
                    && value.ValueKind == JsonValueKind.Object
                    && value.TryGetProperty("display_name", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString()))
                {
                    return name.GetString()!.Trim();
                }
            }
        }

        return "Scoped";
    }
}
