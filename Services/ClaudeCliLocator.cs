namespace PlaywrightAgentAI.Services;

/// <summary>
/// Finds the Claude Code CLI on this machine.
///
/// The app shells out to the CLI rather than calling the API, so that it runs on the
/// user's existing Claude Code login instead of needing an API key.
/// </summary>
public static class ClaudeCliLocator
{
    private static readonly string[] ExecutableNames = ["claude.cmd", "claude.exe", "claude.bat", "claude"];

    /// <summary>Returns the first CLI found, or null. An explicit path always wins.</summary>
    public static string? Locate(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        foreach (var candidate in CandidatePaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // npm's global bin on Windows, where `npm i -g @anthropic-ai/claude-code` lands.
        foreach (var name in ExecutableNames)
            yield return Path.Combine(appData, "npm", name);

        foreach (var name in ExecutableNames)
            yield return Path.Combine(profile, ".claude", "local", name);

        foreach (var name in ExecutableNames)
            yield return Path.Combine(localAppData, "Programs", "claude", name);

        // Anything already on PATH.
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0)
                continue;

            foreach (var name in ExecutableNames)
            {
                string combined;
                try
                {
                    combined = Path.Combine(trimmed, name);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry should not take the search down with it.
                    break;
                }

                yield return combined;
            }
        }
    }

    public static string InstallHint =>
        "Claude Code CLI not found. Install it with:  npm install -g @anthropic-ai/claude-code";
}
