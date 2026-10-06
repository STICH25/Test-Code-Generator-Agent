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

/// <summary>A [Binding] class discovered in the linked solution, offered as a target to extend.</summary>
public class StepDefinitionFile
{
    public required string Path { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string ClassName { get; set; } = "";

    public int BindingCount { get; set; }

    /// <summary>Full text, used as the file to reproduce when appending new steps to it.</summary>
    public string Source { get; set; } = "";

    public override string ToString() =>
        BindingCount == 0 ? ClassName : $"{ClassName}  ({BindingCount} step{(BindingCount == 1 ? "" : "s")})";
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

            if (!IsBindingClass(text))
                continue;

            foreach (Match match in StepAttribute().Matches(text))
            {
                var binding = new StepBinding(match.Groups["kw"].Value, PatternText(match.Groups["pattern"].Value));
                if (!bindings.Contains(binding))
                    bindings.Add(binding);
            }
        }

        return bindings;
    }

    /// <summary>
    /// Finds every [Binding] class, so the user can target one directly instead of always
    /// getting a brand-new file. Grouped by file rather than by binding: two classes could
    /// share a near-identical step wording, but the file is the unit the writer overwrites.
    /// </summary>
    public static List<StepDefinitionFile> FindStepDefinitionFiles(string root)
    {
        var files = new List<StepDefinitionFile>();

        if (!Directory.Exists(root))
            return files;

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

            if (!IsBindingClass(text))
                continue;

            var bindingCount = StepAttribute().Matches(text).Count;
            if (bindingCount == 0)
                continue;

            var classMatch = BindingClassName().Match(text);

            files.Add(new StepDefinitionFile
            {
                Path = path,
                ClassName = classMatch.Success ? classMatch.Groups["name"].Value : Path.GetFileNameWithoutExtension(path),
                BindingCount = bindingCount,
                Source = text.Length <= MaxFeatureSourceLength ? text : text[..MaxFeatureSourceLength] + "\n// ... [truncated]"
            });
        }

        return files.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
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

    /// <summary>
    /// True when the text declares a Reqnroll/SpecFlow binding class. Looks for a Binding
    /// attribute rather than the literal text "[Binding]": that exact spelling missed
    /// [Binding, Scope(...)], [Scope(...), Binding], [Binding(...)] and a qualified
    /// [Reqnroll.Binding] / [TechTalk.SpecFlow.Binding], so a suite written that way showed no
    /// existing step files at all.
    /// </summary>
    public static bool IsBindingClass(string text) => BindingAttribute().IsMatch(text);

    [GeneratedRegex(@"(?:\[|,)\s*(?:[\w.]+\.)?Binding\s*[\],(]")]
    private static partial Regex BindingAttribute();

    /// <summary>
    /// A step attribute and its pattern literal. The pattern is captured WITH its quotes so the
    /// two string forms can be told apart: a verbatim literal (@"^I search for ""(.*)""$", the
    /// usual way to write a regex step) or a plain one. The old pattern accepted only a plain
    /// literal straight after the parenthesis, which rejected every verbatim regex step, any
    /// [Given ("...")] with a space, and a qualified [Reqnroll.Given(...)].
    /// </summary>
    [GeneratedRegex(@"(?:\[|,)\s*(?:[\w.]+\.)?(?<kw>Given|When|Then|StepDefinition)\s*\(\s*(?<pattern>@""(?:[^""]|"""")*""|""(?:[^""\\]|\\.)*"")")]
    private static partial Regex StepAttribute();

    /// <summary>The pattern as the regex or Cucumber expression it denotes: quotes off, escapes undone.</summary>
    private static string PatternText(string literal)
    {
        if (literal.StartsWith("@\"", StringComparison.Ordinal))
            return literal[2..^1].Replace("\"\"", "\"");

        return literal[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    [GeneratedRegex(@"\bclass\s+(?<name>\w+)")]
    private static partial Regex BindingClassName();
}
