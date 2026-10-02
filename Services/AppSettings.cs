using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlaywrightAgentAI.Services;

public enum ClaudeProvider
{
    /// <summary>Drive the locally installed Claude Code CLI under the user's own login.</summary>
    ClaudeCodeCli,

    /// <summary>Call the Anthropic API directly with a key.</summary>
    ApiKey
}

/// <summary>
/// User settings, persisted per-user outside the repository.
///
/// The API key lives here only in memory; what gets written to disk is the DPAPI
/// ciphertext in <see cref="ProtectedApiKey"/>. The previous design put the key in
/// appsettings.json, which is tracked in git.
/// </summary>
public class AppSettings
{
    public const string DefaultModel = "claude-opus-5";

    /// <summary>
    /// Defaults to the CLI: organisations often supply a Claude Code seat without
    /// handing out API keys, so this is the path more users can actually run.
    /// </summary>
    public ClaudeProvider Provider { get; set; } = ClaudeProvider.ClaudeCodeCli;

    /// <summary>Explicit CLI location. Empty means "search the usual places".</summary>
    public string? ClaudeCliPath { get; set; }

    /// <summary>Model alias passed to the CLI's --model flag.</summary>
    public string CliModel { get; set; } = "opus";

    /// <summary>Root folder of the linked test-automation solution, if any.</summary>
    public string? TestSolutionPath { get; set; }

    /// <summary>
    /// Folder to save per-click debug screenshots into while recording. Null or blank
    /// disables the feature entirely - nothing is captured and the Screenshots button stays
    /// disabled. Always separate from <see cref="TestSolutionPath"/>: these images are a
    /// debugging aid and must never be written into the test solution itself.
    /// </summary>
    public string? ScreenshotsPath { get; set; }

    /// <summary>
    /// Optional explicit location of SpecForge (specforge.exe, or SpecForge.Cli.dll from a
    /// source build). Blank means "look on PATH". SpecForge is never required - without it the
    /// post-generation step-reuse check is simply skipped.
    /// </summary>
    public string? SpecForgePath { get; set; }

    /// <summary>
    /// Azure DevOps organization ("acme" or the full https://dev.azure.com/acme URL) used when a
    /// test objective names a PBI by number only. A full work-item link in the objective carries
    /// its own organization and project and wins over these. Not secrets: no credential is ever
    /// stored here - the lookup runs under the user's own `az login`.
    /// </summary>
    public string? AdoOrganization { get; set; }

    /// <summary>Azure DevOps project to look the PBI up in. See <see cref="AdoOrganization"/>.</summary>
    public string? AdoProject { get; set; }

    /// <summary>DPAPI-encrypted API key. This is the only form ever written to disk.</summary>
    public string? ProtectedApiKey { get; set; }

    public string Model { get; set; } = DefaultModel;

    /// <summary>One of low / medium / high / xhigh / max. Controls thinking depth and spend.</summary>
    public string Effort { get; set; } = "high";

    public int MaxTokens { get; set; } = 16000;

    [JsonIgnore]
    public string? ApiKey
    {
        get => SecretStore.Unprotect(ProtectedApiKey);
        set => ProtectedApiKey = SecretStore.Protect(value);
    }

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Whether the selected provider has everything it needs to run.</summary>
    [JsonIgnore]
    public bool IsConfigured => Provider == ClaudeProvider.ClaudeCodeCli
        ? ClaudeCliLocator.Locate(ClaudeCliPath) != null
        : HasApiKey;

    /// <summary>Model shown in the UI for whichever provider is selected.</summary>
    [JsonIgnore]
    public string ActiveModel => Provider == ClaudeProvider.ClaudeCodeCli ? CliModel : Model;

    public AppSettings Clone() => new()
    {
        Provider = Provider,
        ClaudeCliPath = ClaudeCliPath,
        CliModel = CliModel,
        TestSolutionPath = TestSolutionPath,
        ScreenshotsPath = ScreenshotsPath,
        SpecForgePath = SpecForgePath,
        AdoOrganization = AdoOrganization,
        AdoProject = AdoProject,
        ProtectedApiKey = ProtectedApiKey,
        Model = Model,
        Effort = Effort,
        MaxTokens = MaxTokens
    };
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PlaywrightAgentAI",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return MigrateLegacy();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // A corrupt settings file must not stop the app from starting.
            Console.Error.WriteLine($"Could not read settings ({ex.Message}); using defaults.");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Json));
    }

    /// <summary>
    /// First run after the OpenAI-to-Claude switch: pick up an ANTHROPIC_API_KEY from the
    /// environment so the app is usable without a trip to Settings.
    /// </summary>
    private static AppSettings MigrateLegacy()
    {
        var settings = new AppSettings();

        var fromEnvironment = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            settings.ApiKey = fromEnvironment;

        return settings;
    }
}
