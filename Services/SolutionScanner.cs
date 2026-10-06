using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlaywrightAgentAI.Services;

/// <summary>
/// Reads a test-automation solution and works out its conventions.
///
/// Regex over source rather than a Roslyn workspace: the app only needs class names,
/// base types and a sample file, and pulling in the compiler platform to get them would
/// be a heavy dependency for a shallow read. Everything here is read-only.
/// </summary>
public static partial class SolutionScanner
{
    private const int MaxExampleLength = 4000;
    private const int MaxBaseClassLength = 4000;

    public static SolutionProfile Scan(string rootPath)
    {
        var profile = new SolutionProfile { RootPath = rootPath };

        if (!Directory.Exists(rootPath))
        {
            profile.Notes.Add($"Folder not found: {rootPath}");
            return profile;
        }

        var sources = EnumerateSources(rootPath).ToList();
        if (sources.Count == 0)
        {
            profile.Notes.Add("No C# source files found in that folder.");
            return profile;
        }

        profile.ProjectFile = Directory
            .EnumerateFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(p => !IsBuildOutput(p));

        var files = sources
            .Select(path => (Path: path, Text: ReadSafe(path)))
            .Where(f => f.Text.Length > 0)
            .ToList();

        DetectBaseClass(files, profile);
        DetectHelpers(files, profile);
        DetectExampleTest(files, profile);
        DetectBaseUrl(rootPath, profile);
        DetectGherkin(rootPath, files, profile);
        DetectPageObjects(rootPath, profile);

        if (profile.BaseClassName == null)
            profile.Notes.Add("No abstract test base class found; generated tests will set up their own browser.");

        if (profile.ExampleTestSource == null)
            profile.Notes.Add("No existing test found to imitate; generation will rely on the base class alone.");

        return profile;
    }

    private static IEnumerable<string> EnumerateSources(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(p => !IsBuildOutput(p));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string ReadSafe(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// The base class is an abstract class deriving from one of Playwright's NUnit bases.
    /// That is the thing generated tests should inherit, so it is worth finding precisely.
    /// </summary>
    private static void DetectBaseClass(List<(string Path, string Text)> files, SolutionProfile profile)
    {
        foreach (var (path, text) in files)
        {
            var match = AbstractTestBase().Match(text);
            if (!match.Success)
                continue;

            profile.BaseClassName = match.Groups["name"].Value;
            profile.BaseClassSource = Truncate(text, MaxBaseClassLength);

            var ns = NamespaceDeclaration().Match(text);
            if (ns.Success)
                profile.TestNamespace = ns.Groups["ns"].Value;

            profile.Notes.Add($"Base class {profile.BaseClassName} found in {Path.GetFileName(path)}.");
            return;
        }
    }

    private static void DetectHelpers(List<(string Path, string Text)> files, SolutionProfile profile)
    {
        foreach (var (_, text) in files)
        {
            foreach (Match match in StaticHelperClass().Matches(text))
            {
                var name = match.Groups["name"].Value;
                if (!profile.Helpers.Contains(name))
                    profile.Helpers.Add(name);
            }
        }
    }

    /// <summary>
    /// Picks the richest existing test as a few-shot example, preferring one that already
    /// derives from the detected base class.
    /// </summary>
    private static void DetectExampleTest(List<(string Path, string Text)> files, SolutionProfile profile)
    {
        var candidates = files
            .Where(f => f.Text.Contains("[Test]", StringComparison.Ordinal))
            .Select(f => new
            {
                f.Path,
                f.Text,
                TestCount = Regex.Matches(f.Text, @"\[Test\]").Count,
                UsesBase = profile.BaseClassName != null &&
                           Regex.IsMatch(f.Text, $@":\s*{Regex.Escape(profile.BaseClassName)}\b")
            })
            .OrderByDescending(f => f.UsesBase)
            .ThenByDescending(f => f.TestCount)
            .ToList();

        var chosen = candidates.FirstOrDefault();
        if (chosen == null)
            return;

        profile.ExampleTestName = Path.GetFileNameWithoutExtension(chosen.Path);
        profile.ExampleTestSource = Truncate(chosen.Text, MaxExampleLength);
        profile.TestDirectory = Path.GetDirectoryName(chosen.Path) ?? profile.RootPath;

        var ns = NamespaceDeclaration().Match(chosen.Text);
        if (ns.Success)
            profile.TestNamespace = ns.Groups["ns"].Value;
    }

    /// <summary>
    /// Works out whether this solution is a BDD suite and where its pieces live.
    ///
    /// Reqnroll generates a .feature.cs beside every .feature file; those are build output
    /// in spirit, so the [Binding] class search deliberately looks for the attribute rather
    /// than for file names.
    /// </summary>
    private static void DetectGherkin(string rootPath, List<(string Path, string Text)> files, SolutionProfile profile)
    {
        var projectText = profile.ProjectFile != null ? ReadSafe(profile.ProjectFile) : string.Empty;

        profile.SupportsGherkin =
            projectText.Contains("Reqnroll", StringComparison.OrdinalIgnoreCase) ||
            projectText.Contains("SpecFlow", StringComparison.OrdinalIgnoreCase) ||
            files.Any(f => f.Text.Contains("using Reqnroll", StringComparison.Ordinal) ||
                           f.Text.Contains("using TechTalk.SpecFlow", StringComparison.Ordinal));

        profile.Features.AddRange(GherkinAssets.FindFeatures(rootPath));
        profile.StepBindings.AddRange(GherkinAssets.FindStepBindings(rootPath));
        profile.StepDefinitionFiles.AddRange(GherkinAssets.FindStepDefinitionFiles(rootPath));

        profile.FeaturesDirectory =
            GherkinAssets.FindDirectoryContaining(rootPath, "*.feature")
            ?? (profile.SupportsGherkin ? Path.Combine(rootPath, "Features") : null);

        // Prefer the folder that actually holds the step files - the one with the most of them,
        // so a lone hooks or helper file elsewhere cannot win. Built from the same discovery
        // that fills the Step definitions picker, so the two can never disagree about what a
        // step file is.
        var stepFolder = profile.StepDefinitionFiles
            .GroupBy(f => Path.GetDirectoryName(f.Path)!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(f => f.BindingCount))
            .FirstOrDefault();

        if (stepFolder != null)
        {
            var representative = stepFolder.OrderByDescending(f => f.BindingCount).First();
            profile.StepDefinitionsDirectory = stepFolder.Key;
            profile.StepClassSource = Truncate(representative.Source, MaxExampleLength);
            profile.StepClassName = representative.ClassName;
        }
        else if (profile.SupportsGherkin)
        {
            // No step file recognised. Still prefer a folder the solution already has for them -
            // whatever it is called (Steps, StepsDefinitions, StepDefs...) - over inventing a
            // second, differently-named one beside it.
            profile.StepDefinitionsDirectory = FindStepsFolder(rootPath) ?? Path.Combine(rootPath, "StepDefinitions");
        }

        if (profile.SupportsGherkin)
        {
            profile.Notes.Add(
                $"Gherkin suite: {profile.Features.Count} feature file(s), {profile.StepBindings.Count} existing step binding(s).");
        }
    }

    /// <summary>
    /// Locates the page objects and where new ones belong. Generated step definitions are
    /// meant to hold assertions only, so the page object is where the locators live - which
    /// means generation needs to know what already exists before inventing a new one.
    /// </summary>
    private static void DetectPageObjects(string rootPath, SolutionProfile profile)
    {
        profile.PageObjects.AddRange(PageObjectAssets.Find(rootPath));
        profile.PageObjectsDirectory = PageObjectAssets.DirectoryFor(rootPath, profile.PageObjects);

        // Prefer the richest existing page object as the example: one with a handful of
        // methods shows the action/query split far better than a near-empty stub.
        profile.ExamplePageObject = profile.PageObjects
            .OrderByDescending(p => p.Methods.Count)
            .FirstOrDefault();

        profile.Notes.Add(profile.PageObjects.Count == 0
            ? $"No page objects found; new ones will be created in {profile.PageObjectsDirectory}."
            : $"{profile.PageObjects.Count} page object(s) found in {profile.PageObjectsDirectory}.");
    }

    /// <summary>An existing folder that looks like it holds step definitions, by name; null if none does.</summary>
    private static string? FindStepsFolder(string rootPath) =>
        Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
            .Where(d => !IsBuildOutput(d + Path.DirectorySeparatorChar))
            .FirstOrDefault(d =>
            {
                var name = Path.GetFileName(d);
                return name.Contains("step", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Bindings", StringComparison.OrdinalIgnoreCase);
            });

    private static void DetectBaseUrl(string rootPath, SolutionProfile profile)
    {
        var settingsPath = Path.Combine(rootPath, "appsettings.json");
        if (!File.Exists(settingsPath))
            return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (document.RootElement.TryGetProperty("Playwright", out var playwright) &&
                playwright.TryGetProperty("BaseUrl", out var baseUrl))
            {
                profile.BaseUrl = baseUrl.GetString();
            }
        }
        catch (Exception ex)
        {
            profile.Notes.Add($"Could not read appsettings.json: {ex.Message}");
        }
    }

    private static string Truncate(string text, int limit) =>
        text.Length <= limit ? text : text[..limit] + "\n// ... [truncated]";

    [GeneratedRegex(@"public\s+abstract\s+(?:partial\s+)?class\s+(?<name>\w+)\s*:\s*\w*(?:PlaywrightTest|PageTest|BrowserTest|ContextTest)\b")]
    private static partial Regex AbstractTestBase();

    [GeneratedRegex(@"namespace\s+(?<ns>[\w.]+)\s*[;{]")]
    private static partial Regex NamespaceDeclaration();

    [GeneratedRegex(@"public\s+static\s+class\s+(?<name>\w+)")]
    private static partial Regex StaticHelperClass();
}
