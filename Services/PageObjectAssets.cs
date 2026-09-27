using System.Text.RegularExpressions;

namespace PlaywrightAgentAI.Services;

/// <summary>A page object class discovered in the linked solution.</summary>
public class PageObjectFile
{
    public required string Path { get; init; }

    public required string ClassName { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Public action/query methods, so generation can reuse one instead of adding a rival.</summary>
    public List<string> Methods { get; } = [];

    /// <summary>Full text, supplied as the shape to imitate or the file to extend.</summary>
    public string Source { get; set; } = "";

    public override string ToString() =>
        Methods.Count == 0 ? ClassName : $"{ClassName}  ({Methods.Count} methods)";
}

/// <summary>
/// Finds the page objects in a solution.
///
/// This exists so generated code can extend an existing page object rather than create a
/// second one for the same screen. A suite with LoginPage and LoginPageObject, each holding
/// half the selectors, is the page-object equivalent of duplicate step bindings - it does
/// not fail, it just quietly rots.
/// </summary>
public static partial class PageObjectAssets
{
    private const int MaxSourceLength = 5000;

    /// <summary>
    /// A page object is recognised by construction, not by folder: a class that takes an
    /// IPage. Matching on a "Pages" folder alone would miss projects that organise by
    /// feature, and matching on the name alone would sweep up unrelated classes.
    /// </summary>
    public static List<PageObjectFile> Find(string root)
    {
        var found = new List<PageObjectFile>();

        if (!Directory.Exists(root))
            return found;

        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path) || path.EndsWith(".feature.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = ReadSafe(path);
            if (text.Length == 0)
                continue;

            // A [Binding] class is a step-definition file even if it happens to hold an
            // IPage, so exclude those before anything else.
            if (text.Contains("[Binding]", StringComparison.Ordinal))
                continue;

            var match = PageObjectClass().Match(text);
            if (!match.Success)
                continue;

            var page = new PageObjectFile
            {
                Path = path,
                ClassName = match.Groups["name"].Value,
                Source = text.Length <= MaxSourceLength ? text : text[..MaxSourceLength] + "\n// ... [truncated]"
            };

            foreach (Match method in PublicMethod().Matches(text))
            {
                var name = method.Groups["name"].Value;
                if (name != page.ClassName && !page.Methods.Contains(name))
                    page.Methods.Add(name);
            }

            found.Add(page);
        }

        return found.OrderBy(p => p.ClassName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Where new page objects should go: beside existing ones, else a Pages folder.</summary>
    public static string DirectoryFor(string root, List<PageObjectFile> existing) =>
        existing.Count > 0
            ? System.IO.Path.GetDirectoryName(existing[0].Path)!
            : System.IO.Path.Combine(root, "Pages");

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

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

    // A class declaring a constructor that accepts IPage - the defining trait of a page object.
    [GeneratedRegex(@"class\s+(?<name>\w+)[^{]*\{(?:[^}]|\}(?!\s*$))*?\b\k<name>\s*\([^)]*\bIPage\b",
        RegexOptions.Singleline)]
    private static partial Regex PageObjectClass();

    [GeneratedRegex(@"public\s+(?:async\s+)?(?:[\w<>\[\],\?\.]+\s+)+(?<name>\w+)\s*\(")]
    private static partial Regex PublicMethod();
}
