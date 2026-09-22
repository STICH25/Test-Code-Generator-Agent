namespace PlaywrightAgentAI.Models;

public class ExplorationRequest
{
    public string Url { get; set; } = "";
    public string? TestObjective { get; set; }

    // New: whether to generate gherkin output
    public bool IncludeGherkin { get; set; } = false;

    // New: keyword for Gherkin steps (Given/When/Then/And/But)
    public string GherkinKeyword { get; set; } = "Given";
}