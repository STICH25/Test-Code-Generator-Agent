using System.Text.RegularExpressions;

namespace PlaywrightAgentAI.Models;

public enum ArtifactKind
{
    Feature,
    StepDefinitions,
    Test
}

/// <summary>One file the generator produced, ready to be written into the solution.</summary>
public class GeneratedArtifact
{
    public required ArtifactKind Kind { get; init; }

    /// <summary>File name only, e.g. Login.feature. The scanner decides which folder.</summary>
    public required string FileName { get; set; }

    public required string Content { get; set; }

    /// <summary>Set once written.</summary>
    public string? WrittenPath { get; set; }
}

/// <summary>
/// Splits a multi-file model response into artifacts.
///
/// A Gherkin change is never one file - a scenario is meaningless without its bindings -
/// so the prompt asks for both behind explicit markers and this reads them back. Parsing a
/// marker is far more reliable than trying to infer file boundaries from content.
/// </summary>
public static partial class ArtifactParser
{
    public static List<GeneratedArtifact> Parse(string response)
    {
        var artifacts = new List<GeneratedArtifact>();

        if (string.IsNullOrWhiteSpace(response))
            return artifacts;

        var matches = FileMarker().Matches(response);

        if (matches.Count == 0)
        {
            // No markers: treat the whole response as a single file and infer its kind.
            var looksGherkin = response.Contains("Feature:", StringComparison.Ordinal) &&
                               !response.Contains("using ", StringComparison.Ordinal);

            artifacts.Add(new GeneratedArtifact
            {
                Kind = looksGherkin ? ArtifactKind.Feature : ArtifactKind.Test,
                FileName = looksGherkin ? "Generated.feature" : "GeneratedTest.cs",
                Content = response.Trim()
            });

            return artifacts;
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : response.Length;

            var name = match.Groups["name"].Value.Trim();
            var body = response[start..end].Trim();

            if (body.Length == 0)
                continue;

            // The name may arrive as a path (Features/Login.feature); keep just the leaf,
            // since the target folder comes from the scanned solution, not the model.
            var fileName = Path.GetFileName(name.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            artifacts.Add(new GeneratedArtifact
            {
                Kind = ClassifyByExtension(fileName),
                FileName = fileName,
                Content = StripFences(body)
            });
        }

        return artifacts;
    }

    private static ArtifactKind ClassifyByExtension(string fileName) =>
        fileName.EndsWith(".feature", StringComparison.OrdinalIgnoreCase)
            ? ArtifactKind.Feature
            : ArtifactKind.StepDefinitions;

    /// <summary>Markers are often followed by a fenced block despite instructions.</summary>
    private static string StripFences(string body)
    {
        var trimmed = body.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var match = FencedBlock().Match(trimmed);
        return match.Success ? match.Groups["body"].Value.Trim() : trimmed;
    }

    [GeneratedRegex(@"^[ \t]*(?:={2,}\s*)?FILE:\s*(?<name>[^\r\n=]+?)\s*(?:={2,})?[ \t]*$",
        RegexOptions.Multiline)]
    private static partial Regex FileMarker();

    [GeneratedRegex(@"\A```[a-zA-Z0-9#+-]*\s*\n(?<body>.*?)\n?```\s*\z", RegexOptions.Singleline)]
    private static partial Regex FencedBlock();
}
