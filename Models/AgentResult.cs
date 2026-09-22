namespace PlaywrightAgentAI.Models;

public class AgentResult
{
    // Generated Playwright or fallback code
    public string GeneratedCode { get; set; } = "";

    // New: Generated Gherkin payload (feature step + step definition)
    public string GherkinOutput { get; set; } = "";
}
