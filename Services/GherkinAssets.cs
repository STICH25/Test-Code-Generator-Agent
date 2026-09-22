using System.Text.RegularExpressions;

namespace PlaywrightAgentAI.Services;

public record GherkinScenario(string Name, bool IsOutline)
{
    public override string ToString() => IsOutline ? $"{Name} (outline)" : Name;
}

/// <summary>A .feature file discovered in the linked solution.</summary>
public class FeatureFile
{
    public required string Path { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Text after "Feature:".</summary>
    public string Title { get; set; } = "";

    public List<GherkinScenario> Scenarios { get; } = [];

    /// <summary>Full text, used as the example to imitate when appending to this file.</summary>
    public string Source { get; set; } = "";

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Title) ? FileName : $"{Title}  ({FileName})";
}

/// <summary>An existing step binding the generator should reuse rather than duplicate.</summary>
public record StepBinding(string Keyword, string Pattern)
{
    public override string ToString() => $"[{Keyword}(\"{Pattern}\")]";
}

/// <summary>
/// Reads the Gherkin side of a solution: feature files, their scenarios, and the step
/// bindings that already exist.
///
/// Feeding the existing bindings to the model is the point of this - a BDD suite decays
/// fast when every generated scenario invents its own near-duplicate step, so the prompt
/// asks it to reuse a matching step and only add genuinely new ones.
/// </summary>
public static partial class GherkinAssets
{
    private const int MaxFeatureSourceLength = 4000;

    public static List<FeatureFile> FindFeatures(string root)
    {
        var features = new List<FeatureFile>();

        if (!Directory.Exists(root))
            return features;

        foreach (var path in Directory.EnumerateFiles(root, "*.feature", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path))
                continue;

            try
            {
                features.Add(Parse(path, File.ReadAllText(path)));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not read {System.IO.Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return features.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static FeatureFile Parse(string path, string text)
    {
        var feature = new FeatureFile
        {
            Path = path,
            Source = text.Length <= MaxFeatureSourceLength ? text : text[..MaxFeatureSourceLength] + "\n# ... [truncated]"
        };

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("Feature:", StringComparison.OrdinalIgnoreCase))
            {
                feature.Title = line["Feature:".Length..].Trim();
            }
            else if (line.StartsWith("Scenario Outline:", StringComparison.OrdinalIgnoreCase))
            {
                feature.Scenarios.Add(new GherkinScenario(line["Scenario Outline:".Length..].Trim(), true));
            }
            else if (line.StartsWith("Scenario:", StringComparison.OrdinalIgnoreCase))
            {
                feature.Scenarios.Add(new GherkinScenario(line["Scenario:".Length..].Trim(), false));
            }
        }

        return feature;
    }

    public static List<StepBinding> FindStepBindings(string root)
    {
        var bindings = new List<StepBinding>();

        if (!Directory.Exists(root))
            return bindings;

        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch
            {
                continue;
            }

            if (!text.Contains("[Binding]", StringComparison.Ordinal))
                continue;

            foreach (Match match in StepAttribute().Matches(text))
            {
                var binding = new StepBinding(match.Groups["kw"].Value, match.Groups["pattern"].Value);
                if (!bindings.Contains(binding))
                    bindings.Add(binding);
            }
        }

        return bindings;
    }

    /// <summary>Finds the folder holding a given kind of file, so new ones land beside them.</summary>
    public static string? FindDirectoryContaining(string root, string searchPattern)
    {
        if (!Directory.Exists(root))
            return null;

        return Directory
            .EnumerateFiles(root, searchPattern, SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(p))
            .Select(System.IO.Path.GetDirectoryName)
            .FirstOrDefault(d => d != null);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\[(?<kw>Given|When|Then|StepDefinition)\(\s*""(?<pattern>[^""]+)""")]
    private static partial Regex StepAttribute();
}
