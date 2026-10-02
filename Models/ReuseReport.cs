namespace PlaywrightAgentAI.Models;

/// <summary>
/// How many steps in a freshly generated scenario reuse a binding that already existed in
/// the linked solution, as judged by SpecForge's matcher (Roslyn-scanned bindings, Cucumber
/// expressions and regex both understood) rather than by the model's own say-so.
///
/// Advisory only: the app never depends on this being present, and a run without SpecForge
/// simply has no report.
/// </summary>
public class ReuseReport
{
    public int Total { get; init; }

    public int Reused { get; init; }

    /// <summary>Steps with no matching existing binding, as "Keyword text" lines.</summary>
    public List<string> NewSteps { get; init; } = [];

    public string Summary => $"{Reused} of {Total} step(s) reuse existing bindings, {Total - Reused} new.";
}
