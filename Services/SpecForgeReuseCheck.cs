using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Services;

/// <summary>
/// Checks, after generation, which steps in the new scenarios reuse bindings that already
/// existed - by handing them to SpecForge's matcher.
///
/// The in-app SolutionScanner still shapes the prompt (it is always available, and the model
/// needs the binding list up front). This is the independent second opinion: SpecForge scans
/// the solution with Roslyn and matches with real Cucumber-expression / regex semantics, so a
/// step the model believed it reused but worded slightly off shows up as new here.
///
/// Everything runs locally against the linked solution; the only files written are scratch
/// copies under %TEMP%, kept after the run (like last-prompt.txt) so a surprising result can
/// be inspected instead of theorised about.
/// </summary>
public static class SpecForgeReuseCheck
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static string WorkDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "PlaywrightAgentAI.specforge");

    /// <summary>
    /// Returns null when there is nothing to check (no feature produced, or no new steps).
    /// Throws if SpecForge itself fails - the caller decides how loudly to say so.
    /// </summary>
    public static async Task<ReuseReport?> Run(
        SpecForgeCommand specForge,
        SolutionProfile profile,
        IEnumerable<GeneratedArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        var feature = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.Feature);
        if (feature == null)
            return null;

        // When appending to an existing feature file the model reproduces the whole file, so
        // scenarios that were already there would inflate the tally with old steps.
        var existingNames = ExistingScenarioNames(profile, feature);
        var scenarios = ParseScenarios(feature.Content)
            .Where(s => !existingNames.Contains(s.Name))
            .ToList();

        if (scenarios.Sum(s => s.Steps.Count) == 0)
            return null;

        Directory.CreateDirectory(WorkDirectory);
        var scenariosPath = Path.Combine(WorkDirectory, "scenarios.json");
        var inventoryPath = Path.Combine(WorkDirectory, "inventory.json");
        var matchPath = Path.Combine(WorkDirectory, "match.json");

        // A stale file from the previous run must never be mistaken for this run's output.
        foreach (var stale in new[] { inventoryPath, matchPath })
            File.Delete(stale);

        File.WriteAllText(scenariosPath, JsonSerializer.Serialize(scenarios, WriteOptions), new UTF8Encoding(false));

        await Execute(specForge, ["scan", "--project", profile.RootPath, "--out", inventoryPath], cancellationToken);
        await Execute(specForge, ["match", "--scenarios", scenariosPath, "--inventory", inventoryPath, "--out", matchPath], cancellationToken);

        var match = JsonSerializer.Deserialize<MatchDto>(File.ReadAllText(matchPath), ReadOptions)
                    ?? throw new InvalidOperationException("SpecForge wrote an unreadable match report.");

        var newSteps = match.Results
            .Where(r => string.Equals(r.MatchKind, "None", StringComparison.OrdinalIgnoreCase))
            .Select(r => $"{(string.IsNullOrWhiteSpace(r.EffectiveKeyword) ? r.Keyword : r.EffectiveKeyword)} {r.Text}")
            .ToList();

        return new ReuseReport { Total = match.Total, Reused = match.Reused, NewSteps = newSteps };
    }

    private static HashSet<string> ExistingScenarioNames(SolutionProfile profile, GeneratedArtifact feature)
    {
        if (string.IsNullOrWhiteSpace(feature.TargetPath))
            return [];

        var existing = profile.Features.FirstOrDefault(
            f => string.Equals(f.Path, feature.TargetPath, StringComparison.OrdinalIgnoreCase));

        return existing == null
            ? []
            : new HashSet<string>(existing.Scenarios.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- feature text -> SpecForge scenarios

    private sealed record StepDto(string Keyword, string Text);

    // Property names are PascalCase on purpose: they are SpecForge's own scenarios.json shape.
    private sealed class ScenarioDto
    {
        public string Feature { get; set; } = "";
        public string Group { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string Flow { get; set; } = "Main";
        public string Name { get; set; } = "";
        public List<string> Tags { get; set; } = [];
        public List<StepDto> Steps { get; set; } = [];
    }

    private static readonly Regex StepLine = new(@"^(Given|When|Then|And|But)\s+(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// Reads just enough Gherkin to list steps per scenario: Feature, Background, Scenario /
    /// Scenario Outline, tags and step lines. Examples tables, comments and doc strings are
    /// skipped. Not a general parser - the text is the model's own output in a known shape.
    /// </summary>
    private static List<ScenarioDto> ParseScenarios(string featureText)
    {
        var scenarios = new List<ScenarioDto>();
        var featureName = "";
        ScenarioDto? current = null;
        var pendingTags = new List<string>();
        var inDocString = false;

        foreach (var raw in featureText.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("\"\"\"", StringComparison.Ordinal))
            {
                inDocString = !inDocString;
                continue;
            }

            if (inDocString || line.Length == 0 || line[0] == '#' || line[0] == '|')
                continue;

            if (line[0] == '@')
            {
                pendingTags.AddRange(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                continue;
            }

            if (line.StartsWith("Feature:", StringComparison.Ordinal))
            {
                featureName = line["Feature:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("Background:", StringComparison.Ordinal))
            {
                current = Start(scenarios, featureName, "Background", pendingTags);
                continue;
            }

            var heading = ScenarioHeading(line);
            if (heading != null)
            {
                current = Start(scenarios, featureName, heading, pendingTags);
                continue;
            }

            var step = StepLine.Match(line);
            if (step.Success && current != null)
                current.Steps.Add(new StepDto(step.Groups[1].Value, step.Groups[2].Value.Trim()));
        }

        return scenarios;
    }

    private static ScenarioDto Start(List<ScenarioDto> into, string feature, string name, List<string> tags)
    {
        var scenario = new ScenarioDto { Feature = feature, Name = name, Tags = [.. tags] };
        tags.Clear();
        into.Add(scenario);
        return scenario;
    }

    private static string? ScenarioHeading(string line)
    {
        foreach (var prefix in new[] { "Scenario Outline:", "Scenario Template:", "Scenario:", "Example:" })
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..].Trim();
        }

        return null;
    }

    // ---------------------------------------------------------------- SpecForge match report

    private sealed class MatchDto
    {
        public int Total { get; set; }
        public int Reused { get; set; }
        public List<MatchResultDto> Results { get; set; } = [];
    }

    private sealed class MatchResultDto
    {
        public string Keyword { get; set; } = "";
        public string EffectiveKeyword { get; set; } = "";
        public string Text { get; set; } = "";
        public string MatchKind { get; set; } = "None";
    }

    // ---------------------------------------------------------------- process

    private static async Task Execute(SpecForgeCommand command, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in command.LeadingArgs)
            startInfo.ArgumentList.Add(argument);

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Could not start {command.Display}.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);

        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }

            if (cancellationToken.IsCancellationRequested)
                throw;

            throw new TimeoutException($"SpecForge '{arguments[0]}' did not finish within {CommandTimeout.TotalSeconds:0}s.");
        }

        if (process.ExitCode != 0)
        {
            var detail = (await stderr).Trim();
            if (detail.Length == 0)
                detail = (await stdout).Trim();

            throw new InvalidOperationException($"SpecForge '{arguments[0]}' failed (exit {process.ExitCode}): {detail}");
        }
    }
}
