using System.Text.RegularExpressions;
using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Services;

/// <summary>
/// Marks generated tests that did not pass when verified, so that a test written ahead of a
/// half-finished PBI can still be inserted without looking like a finished, trustworthy one.
///
/// Only the person can say the failure is down to the PBI - a locator that does not exist yet
/// and a locator the model got wrong fail identically - so this runs on their say-so and the
/// comment says "looks incomplete", not "is".
/// </summary>
public static partial class RunNotes
{
    private const string Marker = "NOTE (not yet passing";
    private const int MaxReasonLength = 100;

    /// <summary>
    /// Adds a one-line comment above each failed scenario (Gherkin) or above the test class
    /// (plain fixture). Returns how many comments were added; zero when they were all there already.
    /// </summary>
    public static int AddIncompleteNote(
        IEnumerable<GeneratedArtifact> artifacts, TestRunResult result, int? pbiId, DateTime? now = null)
    {
        var failed = result.Tests.Where(t => !t.Passed).ToList();
        if (failed.Count == 0)
            return 0;

        var date = (now ?? DateTime.Now).ToString("yyyy-MM-dd");
        var who = pbiId is { } id ? $"PBI {id}" : "the PBI";
        var added = 0;

        foreach (var artifact in artifacts)
        {
            if (artifact.Kind == ArtifactKind.Feature)
                added += NoteFeature(artifact, failed, date, who);
            else if (artifact.Kind == ArtifactKind.Test)
                added += NoteFixture(artifact, failed, date, who);
        }

        return added;
    }

    private static int NoteFeature(GeneratedArtifact artifact, List<TestCaseResult> failed, string date, string who)
    {
        var lines = artifact.Content.ReplaceLineEndings("\n").Split('\n').ToList();

        var scenarios = lines
            .Select((line, index) => (Match: ScenarioLine().Match(line), Index: index))
            .Where(x => x.Match.Success)
            .ToList();

        if (scenarios.Count == 0)
            return 0;

        // Reqnroll names a test after its scenario, so the two compare equal once punctuation and
        // case are ignored. Outline rows add a suffix, hence "starts with" rather than equals.
        var targets = new List<(int Index, string Indent, TestCaseResult Test)>();
        foreach (var test in failed)
        {
            var testKey = Normalise(test.Name);
            foreach (var s in scenarios)
            {
                var titleKey = Normalise(s.Match.Groups["title"].Value);
                if (titleKey.Length > 0 && testKey.StartsWith(titleKey, StringComparison.Ordinal) &&
                    targets.All(t => t.Index != s.Index))
                {
                    targets.Add((s.Index, s.Match.Groups["indent"].Value, test));
                }
            }
        }

        // The names did not line up (a rename in the generator, say): a note somewhere beats none.
        if (targets.Count == 0)
            targets.Add((scenarios[0].Index, scenarios[0].Match.Groups["indent"].Value, failed[0]));

        var added = 0;
        foreach (var t in targets.OrderByDescending(x => x.Index))
        {
            if (AlreadyNoted(lines, t.Index))
                continue;

            lines.Insert(t.Index, $"{t.Indent}# {Text(date, who, t.Test)}");
            added++;
        }

        if (added > 0)
            artifact.Content = string.Join("\n", lines);

        return added;
    }

    private static int NoteFixture(GeneratedArtifact artifact, List<TestCaseResult> failed, string date, string who)
    {
        var lines = artifact.Content.ReplaceLineEndings("\n").Split('\n').ToList();

        // Above the fixture attribute when there is one (so the comment is not wedged between an
        // attribute and its class), otherwise above the class declaration.
        var index = lines.FindIndex(l => l.TrimStart().StartsWith("[TestFixture", StringComparison.Ordinal));
        if (index < 0)
            index = lines.FindIndex(l => ClassLine().IsMatch(l));
        if (index < 0 || AlreadyNoted(lines, index))
            return 0;

        var indent = new string(lines[index].TakeWhile(char.IsWhiteSpace).ToArray());
        lines.Insert(index, $"{indent}// {Text(date, who, failed[0])}");
        artifact.Content = string.Join("\n", lines);
        return 1;
    }

    private static string Text(string date, string who, TestCaseResult test) =>
        $"{Marker}, verified {date}): {who} may be incomplete - {Reason(test)}";

    /// <summary>The first meaningful line of the failure, short enough to sit on one comment line.</summary>
    internal static string Reason(TestCaseResult test)
    {
        var first = (test.Message ?? "")
            .ReplaceLineEndings("\n").Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? "the test failed";

        first = Regex.Replace(first, @"\s+", " ");
        return first.Length <= MaxReasonLength ? first : first[..(MaxReasonLength - 3)] + "...";
    }

    /// <summary>True when the line above (or the comment block above) already carries our marker.</summary>
    private static bool AlreadyNoted(List<string> lines, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Contains(Marker, StringComparison.Ordinal))
                return true;

            // Walk up through tags and other comments only; anything else ends the block.
            if (!(line.StartsWith('#') || line.StartsWith("//") || line.StartsWith('@') || line.StartsWith('[')))
                return false;
        }

        return false;
    }

    private static string Normalise(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [GeneratedRegex(@"^(?<indent>\s*)(?:Scenario Outline|Scenario Template|Scenario)\s*:\s*(?<title>.*?)\s*$")]
    private static partial Regex ScenarioLine();

    [GeneratedRegex(@"^\s*(?:public\s+|internal\s+)?(?:sealed\s+|partial\s+|abstract\s+)*class\s+\w+")]
    private static partial Regex ClassLine();
}
