namespace PlaywrightAgentAI.Models;

/// <summary>
/// One screenshot captured during recording, in the order it was taken.
///
/// The description is the recorded action's own human-readable line (e.g. "Click role=button
/// named \"Sign in\"") rather than a feature-file step - the screenshot is taken the moment the
/// action happens, well before a .feature file exists to quote from, so the action's own
/// description is the only caption available at capture time and needs no extra bookkeeping to
/// keep in sync.
/// </summary>
public class ScreenshotEntry
{
    public required int Index { get; init; }

    public required string FilePath { get; init; }

    public required string Description { get; init; }

    public string FileName => System.IO.Path.GetFileName(FilePath);
}
