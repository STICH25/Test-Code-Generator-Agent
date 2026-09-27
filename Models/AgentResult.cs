
namespace PlaywrightAgentAI.Models;

public class AgentResult
{
    public string GeneratedCode { get; set; } = "";

    /// <summary>
    /// True when the code came from the AI generator, false when it fell back to the
    /// static template. The UI must not report a plain success for a fallback result.
    /// </summary>
    public bool UsedAi { get; set; }

    /// <summary>
    /// Non-fatal problems worth showing the user (AI unavailable, AI call failed, no
    /// sections found, ...). An empty list means the run was clean.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// The files produced. A Gherkin run yields a .feature plus its step bindings; a
    /// plain NUnit run yields one .cs file.
    /// </summary>
    public List<GeneratedArtifact> Artifacts { get; set; } = [];

    /// <summary>
    /// Debug screenshot(s) taken while producing this result - a typed-objective run
    /// captures the matched section so the user can confirm the right part of the page was
    /// tested. Empty when no screenshots folder is configured, or when the run instead came
    /// from a recording (which captures its own, one per action, separately).
    /// </summary>
    public List<ScreenshotEntry> Screenshots { get; set; } = [];
}
