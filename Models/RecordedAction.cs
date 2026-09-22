using System.Text.Json.Serialization;

namespace PlaywrightAgentAI.Models;

/// <summary>
/// One user interaction captured while recording.
///
/// The recorder deliberately reports several ways of addressing the same element rather
/// than committing to one: the model picks the most durable locator when it writes the
/// test, and a role plus accessible name usually beats a CSS path.
/// </summary>
public class RecordedAction
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "click";

    [JsonPropertyName("selector")]
    public string? Selector { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("name")]
    public string? AccessibleName { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("testId")]
    public string? TestId { get; set; }

    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Renders the action as a line the model can turn into a Playwright call.</summary>
    public string Describe()
    {
        var target = DescribeTarget();

        return Kind switch
        {
            "navigate" => $"Navigate to {Url}",
            "click" => $"Click {target}",
            "fill" => $"Fill {target} with \"{Value}\"",
            "select" => $"Select \"{Value}\" in {target}",
            "check" => $"Check {target}",
            "uncheck" => $"Uncheck {target}",
            "press" => $"Press {Value} in {target}",
            _ => $"{Kind} {target}"
        };
    }

    private string DescribeTarget()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(TestId))
            parts.Add($"data-testid=\"{TestId}\"");

        if (!string.IsNullOrWhiteSpace(Role))
        {
            parts.Add(string.IsNullOrWhiteSpace(AccessibleName)
                ? $"role={Role}"
                : $"role={Role} named \"{AccessibleName}\"");
        }
        else if (!string.IsNullOrWhiteSpace(Text))
        {
            parts.Add($"text \"{Truncate(Text)}\"");
        }

        if (!string.IsNullOrWhiteSpace(Selector))
            parts.Add($"css={Selector}");

        if (parts.Count == 0)
            parts.Add(Tag ?? "element");

        return string.Join(" | ", parts);
    }

    private static string Truncate(string value) =>
        value.Length <= 60 ? value : value[..60] + "...";
}
