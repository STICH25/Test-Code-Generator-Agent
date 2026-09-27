
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

    /// <summary>Existing page object to extend with a new method. Null means create a new one.</summary>
    public string? TargetPageObjectPath { get; set; }

    /// <summary>Existing step-definitions class to append new bindings to. Null means create a new one.</summary>
    public string? TargetStepDefinitionsPath { get; set; }

    /// <summary>
    /// Folder to save debug screenshots into, from Settings. Null/blank disables capture.
    /// Only used for a typed-objective run (RecordedActions is empty) - a recording captures
    /// its own screenshots separately, one per action, as it happens.
    /// </summary>
    public string? ScreenshotsPath { get; set; }
}
