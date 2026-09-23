
namespace PlaywrightAgentAI.Models;

public class ExplorationRequest
{
    public string Url { get; set; } = "";

    public string? TestObjective { get; set; }

    /// <summary>
    /// Steps captured by the recorder, in order. When present the generated test should
    /// reproduce this exact sequence rather than inventing its own interactions.
    /// </summary>
    public List<RecordedAction> RecordedActions { get; set; } = [];

    /// <summary>
    /// Existing feature file to append the new scenario to. Null means create a new one.
    /// </summary>
    public string? TargetFeaturePath { get; set; }
}
