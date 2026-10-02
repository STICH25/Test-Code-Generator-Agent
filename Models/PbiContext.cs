namespace PlaywrightAgentAI.Models;

/// <summary>One manual step of an Azure DevOps Test Case: what to do, and what should be seen.</summary>
public record AdoTestStep(string Action, string Expected);

/// <summary>An Azure DevOps Test Case linked to the PBI, with its real steps.</summary>
public class AdoTestCase
{
    public int Id { get; init; }

    public string Title { get; init; } = "";

    public List<AdoTestStep> Steps { get; init; } = [];
}

/// <summary>
/// What was read from Azure DevOps for the PBI an objective refers to.
///
/// The linked Test Cases, not the PBI's own acceptance criteria, are the intended source of
/// scenarios: acceptance criteria are often thin ("Given I am logged in and I have
/// permissions...") while the Test Cases carry the real, detailed steps.
/// </summary>
public class PbiContext
{
    public int Id { get; init; }

    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public string AcceptanceCriteria { get; init; } = "";

    public List<AdoTestCase> TestCases { get; init; } = [];

    public string Summary =>
        TestCases.Count == 0
            ? $"PBI {Id} \"{Title}\" - no linked test cases"
            : $"PBI {Id} \"{Title}\" - {TestCases.Count} linked test case(s)";
}
