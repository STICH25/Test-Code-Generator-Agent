using System.Text;
using System.Text.Json;
using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Services;

/// <summary>What a PBI lookup produced: the context if it worked, and whatever went wrong either way.</summary>
/// <param name="SkillsConsulted">
/// The skills Claude reports having loaded (its own account, so a diagnostic, not a guarantee).
/// Empty means it worked from the built-in fallback recipe instead - worth knowing, because
/// the user's skills are meant to be the authority on how Azure DevOps is reached.
/// </param>
public sealed record PbiLookupResult(
    PbiContext? Context,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string>? SkillsConsulted = null);

/// <summary>
/// Reads a PBI and its linked Test Cases from Azure DevOps by asking Claude to do it.
///
/// Reaching ADO is knowledge the user's own skills already carry (gherkin-to-ado-testcases for
/// the az CLI route, specforge-reqnroll step 1 for "PBI -> TestedBy-Forward -> each Test Case's
/// steps"), so the lookup defers to them instead of re-implementing it. Claude runs under the
/// user's own `az login`; this app never sees an ADO credential.
///
/// Two boundaries are enforced by the CLI itself, not by asking nicely. The tool allow-list
/// is read-only `az` commands, and anything else is denied in --print mode - so even though the
/// skills describe creating missing Test Cases in ADO, that cannot happen from here. And the
/// whole thing is optional: any failure comes back as problems, and generation carries on
/// without PBI context, exactly as before.
/// </summary>
public static class AdoPbiLookup
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(4);

    private const int MaxTextLength = 3000;
    private const int MaxTestCases = 25;
    private const int MaxStepsPerCase = 40;

    // Read-only, by allow-list. `az rest` is denied outright because it is the route the
    // skills use to POST suites and cases.
    private static readonly string[] AllowedTools =
    [
        "Skill",
        "Bash(az boards work-item show:*)",
        "Bash(az account show:*)",
        "Bash(az extension show:*)",
        "Bash(az version:*)",
        "Bash(az devops project show:*)"
    ];

    private static readonly string[] DisallowedTools =
    [
        "Bash(az rest:*)",
        "Bash(az boards work-item create:*)",
        "Bash(az boards work-item update:*)",
        "Bash(az boards work-item delete:*)",
        "Bash(az boards work-item relation:*)"
    ];

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<PbiLookupResult> Fetch(
        ClaudeCliCodeGenerator cli,
        PbiReference reference,
        string? organization,
        string? project,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        // Run from the user's own solution when there is one, the way they run these skills
        // themselves: the skill looks in the repo's CLAUDE.md for the Azure DevOps organization
        // and project, and project-level skills and settings are found there. Nothing is
        // written to that folder - the tool allow-list has no write access, and the prompt log
        // stays in the scratch directory.
        var options = new CliRunOptions(
            MaxTurns: 25,
            AllowedTools: AllowedTools,
            DisallowedTools: DisallowedTools,
            PromptLogName: "last-pbi-prompt.txt",
            WorkingDirectory: workingDirectory);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        string reply;
        try
        {
            reply = await cli.RunRestricted(
                "Follow the piped message. Reply with only the JSON object it asks for.",
                BuildPayload(reference, organization, project),
                options,
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PbiLookupResult(null, [$"The Azure DevOps lookup did not finish within {Timeout.TotalMinutes:0} minutes."]);
        }

        return Parse(reply, reference.Id);
    }

    /// <summary>The message piped to Claude. Public so the exact wording can be inspected and tested.</summary>
    public static string BuildPayload(PbiReference reference, string? organization, string? project)
    {
        var text = new StringBuilder();

        text.AppendLine("You are fetching read-only context from Azure DevOps for a test generator.");
        text.AppendLine();
        text.AppendLine("TARGET");
        text.AppendLine($"  PBI (work item) id: {reference.Id}");
        text.AppendLine($"  Organization:       {organization ?? "not given"}");
        text.AppendLine($"  Project:            {project ?? "not given"}");
        text.AppendLine();
        text.AppendLine("HARD LIMITS");
        text.AppendLine("- READ-ONLY. You may run only `az boards work-item show`, `az account show`,");
        text.AppendLine("  `az extension show`, `az version` and `az devops project show`. Never create, update,");
        text.AppendLine("  link or delete anything, and never use `az rest`. Anything else is denied; if a command");
        text.AppendLine("  you need is denied, stop and report it under \"problems\".");
        text.AppendLine("- Do NOT create missing Test Cases, even though the skills below describe doing so.");
        text.AppendLine("  That workflow is out of scope here: if the PBI has no linked Test Cases, say so and stop.");
        text.AppendLine();
        text.AppendLine("HOW - THE SKILLS COME FIRST");
        text.AppendLine("Your first action must be to load these two skills with the Skill tool and follow them. They are");
        text.AppendLine("the authority on how this user reaches Azure DevOps; do not substitute an approach of your own.");
        text.AppendLine("  1. `gherkin-to-ado-testcases` - how Azure DevOps is reached (its preflight and access route).");
        text.AppendLine("     Use ONLY the read-only parts; ignore every write step.");
        text.AppendLine("  2. `specforge-reqnroll`, step 1 - fetch the PBI with its relations, find the");
        text.AppendLine("     `Microsoft.VSTS.Common.TestedBy-Forward` relations (each points at a Test Case), and fetch");
        text.AppendLine("     each Test Case's real steps from `Microsoft.VSTS.TCM.Steps`.");
        text.AppendLine("Only the `az` commands listed under HARD LIMITS are permitted here. If a skill points you at an MCP");
        text.AppendLine("tool or any other command, it will be denied - use the az route and say so under \"problems\".");
        text.AppendLine("If the repository you are running in has a CLAUDE.md with an Azure DevOps block (organization,");
        text.AppendLine("project), use it when organization or project above are \"not given\".");
        text.AppendLine();
        text.AppendLine("FALLBACK - only if a skill cannot be loaded at all:");
        text.AppendLine("  az boards work-item show --id <id> --org <organization url> --expand relations -o json");
        text.AppendLine("  then for each TestedBy-Forward relation, take the trailing id of its url and run");
        text.AppendLine("  az boards work-item show --id <testCaseId> --org <organization url> -o json");
        text.AppendLine("If organization or project are still unknown, try the commands without them first (az may have");
        text.AppendLine("defaults configured); if that fails, report it under \"problems\".");
        text.AppendLine();
        text.AppendLine("STEP XML");
        text.AppendLine("`Microsoft.VSTS.TCM.Steps` is an XML <steps> block. Each <step> has two <parameterizedString>");
        text.AppendLine("children: the action, then the expected result. Their text is HTML that has been escaped");
        text.AppendLine("into XML - unescape it, strip the tags, and collapse the whitespace.");
        text.AppendLine();
        text.AppendLine("REPLY");
        text.AppendLine("Reply with ONLY this JSON object. No code fences, no prose before or after:");
        text.AppendLine("{");
        text.AppendLine("  \"pbi\": { \"id\": 0, \"title\": \"\", \"description\": \"\", \"acceptanceCriteria\": \"\" },");
        text.AppendLine("  \"testCases\": [ { \"id\": 0, \"title\": \"\", \"steps\": [ { \"action\": \"\", \"expected\": \"\" } ] } ],");
        text.AppendLine("  \"skillsUsed\": [ \"\" ],");
        text.AppendLine("  \"problems\": [ \"\" ]");
        text.AppendLine("}");
        text.AppendLine("Plain text only inside every string. \"skillsUsed\" lists exactly the skills you actually loaded with");
        text.AppendLine("the Skill tool - an empty list if you loaded none. If you could not read the PBI at all, set \"pbi\"");
        text.AppendLine("to null and explain why in \"problems\". \"problems\" may be empty.");

        return text.ToString();
    }

    /// <summary>
    /// Reads Claude's reply, tolerating the things models do despite instructions: a code fence
    /// around the JSON, or a sentence before it.
    /// </summary>
    public static PbiLookupResult Parse(string reply, int expectedId)
    {
        var json = ExtractJsonObject(reply);
        if (json == null)
            return new PbiLookupResult(null, ["The Azure DevOps lookup did not return readable JSON."]);

        ReplyDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ReplyDto>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            return new PbiLookupResult(null, [$"The Azure DevOps lookup returned malformed JSON ({ex.Message})."]);
        }

        var problems = (dto?.Problems ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var skills = (dto?.SkillsUsed ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();

        if (dto?.Pbi == null)
        {
            if (problems.Count == 0)
                problems.Add($"Azure DevOps returned nothing for PBI {expectedId}.");

            return new PbiLookupResult(null, problems, skills);
        }

        var testCases = (dto.TestCases ?? [])
            .Take(MaxTestCases)
            .Select(tc => new AdoTestCase
            {
                Id = tc.Id,
                Title = (tc.Title ?? "").Trim(),
                Steps = (tc.Steps ?? [])
                    .Take(MaxStepsPerCase)
                    .Select(s => new AdoTestStep((s.Action ?? "").Trim(), (s.Expected ?? "").Trim()))
                    .Where(s => s.Action.Length > 0 || s.Expected.Length > 0)
                    .ToList()
            })
            .ToList();

        if ((dto.TestCases?.Count ?? 0) > MaxTestCases)
            problems.Add($"Only the first {MaxTestCases} of {dto.TestCases!.Count} linked test cases were used.");

        var context = new PbiContext
        {
            Id = dto.Pbi.Id != 0 ? dto.Pbi.Id : expectedId,
            Title = (dto.Pbi.Title ?? "").Trim(),
            Description = Truncate(dto.Pbi.Description),
            AcceptanceCriteria = Truncate(dto.Pbi.AcceptanceCriteria),
            TestCases = testCases
        };

        return new PbiLookupResult(context, problems, skills);
    }

    private static string Truncate(string? value)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength] + " ... [truncated]";
    }

    private static string? ExtractJsonObject(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return null;

        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');

        return start >= 0 && end > start ? reply[start..(end + 1)] : null;
    }

    // ---------------------------------------------------------------- reply shape

    private sealed class ReplyDto
    {
        public PbiDto? Pbi { get; set; }
        public List<TestCaseDto>? TestCases { get; set; }
        public List<string>? Problems { get; set; }
        public List<string>? SkillsUsed { get; set; }
    }

    private sealed class PbiDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? AcceptanceCriteria { get; set; }
    }

    private sealed class TestCaseDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public List<StepDto>? Steps { get; set; }
    }

    private sealed class StepDto
    {
        public string? Action { get; set; }
        public string? Expected { get; set; }
    }
}
