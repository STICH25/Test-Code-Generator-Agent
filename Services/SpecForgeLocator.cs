namespace PlaywrightAgentAI.Services;

/// <summary>How to launch SpecForge: the executable, any arguments that must precede its own, and a label for the UI.</summary>
public sealed record SpecForgeCommand(string FileName, string[] LeadingArgs, string Display);

/// <summary>
/// Finds the optional SpecForge tool on this machine.
///
/// SpecForge is never required - when it is absent the app falls back to its own scanner and
/// simply skips the post-generation reuse check. It is usually a global dotnet tool
/// (specforge.exe), but can also be run straight from a source build's SpecForge.Cli.dll, which
/// is how it is typically used before it has been packed and installed.
/// </summary>
public static class SpecForgeLocator
{
    /// <summary>Returns the first SpecForge found, or null. An explicit path that exists always wins.</summary>
    public static SpecForgeCommand? Locate(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configured = configuredPath.Trim().Trim('"');
            if (File.Exists(configured))
                return For(configured);
        }

        foreach (var candidate in CandidatePaths())
        {
            if (File.Exists(candidate))
                return For(candidate);
        }

        return null;
    }

    private static SpecForgeCommand For(string path) =>
        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new SpecForgeCommand("dotnet", [path], $"dotnet {Path.GetFileName(path)}")
            : new SpecForgeCommand(path, [], path);

    private static IEnumerable<string> CandidatePaths()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Where `dotnet tool install --global` puts it.
        yield return Path.Combine(profile, ".dotnet", "tools", "specforge.exe");

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0)
                continue;

            string combined;
            try
            {
                combined = Path.Combine(trimmed, "specforge.exe");
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry should not take the search down with it.
                continue;
            }

            yield return combined;
        }
    }
}
