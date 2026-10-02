using System.Text.RegularExpressions;

namespace PlaywrightAgentAI.Services;

/// <summary>A work item named in a test objective, plus the organization and project when it came as a full link.</summary>
public sealed record PbiReference(int Id, string? Organization, string? Project);

/// <summary>
/// Spots "this objective is about an Azure DevOps PBI" in free text.
///
/// Deliberately conservative: a bare number is never a PBI (objectives mention counts and
/// years constantly), so it takes either a work-item link or a number that follows a word like
/// PBI / work item / user story. A false negative just means no lookup, which is the old
/// behaviour; a false positive would send Claude off to read an unrelated work item.
/// </summary>
public static partial class PbiReferenceFinder
{
    /// <summary>The first reference in the text, or null when there is none.</summary>
    public static PbiReference? Find(string? objective)
    {
        if (string.IsNullOrWhiteSpace(objective))
            return null;

        var link = WorkItemLink().Match(objective);
        if (link.Success && int.TryParse(link.Groups["id"].Value, out var linkedId))
        {
            var organization = link.Groups["org"].Success ? link.Groups["org"].Value : link.Groups["vsorg"].Value;
            var project = link.Groups["project"].Success ? Uri.UnescapeDataString(link.Groups["project"].Value) : null;
            return new PbiReference(linkedId, string.IsNullOrEmpty(organization) ? null : organization, project);
        }

        var word = KeywordThenNumber().Match(objective);
        if (word.Success && int.TryParse(word.Groups["id"].Value, out var id))
            return new PbiReference(id, null, null);

        return null;
    }

    /// <summary>
    /// Accepts "acme", "dev.azure.com/acme" or "https://dev.azure.com/acme" and returns the full
    /// organization URL az expects. Null for blank input.
    /// </summary>
    public static string? NormalizeOrganization(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim().TrimEnd('/');

        if (trimmed.Contains("://", StringComparison.Ordinal))
            return trimmed;

        if (trimmed.Contains('.', StringComparison.Ordinal))
            return $"https://{trimmed}";

        return $"https://dev.azure.com/{trimmed}";
    }

    // https://dev.azure.com/<org>/<project>/_workitems/edit/<id>  and  https://<org>.visualstudio.com/<project>/_workitems/edit/<id>
    [GeneratedRegex(
        @"https?://(?:dev\.azure\.com/(?<org>[^/\s]+)|(?<vsorg>[^/\s.]+)\.visualstudio\.com)/(?<project>[^/\s]+)/_workitems/edit/(?<id>\d+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex WorkItemLink();

    [GeneratedRegex(
        @"\b(?:PBI|work\s*item|user\s*story|ADO|bug|task)\b[\s:#\-]*(?:no\.?|number|id)?[\s:#\-]*(?<id>\d{3,})\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeywordThenNumber();
}
