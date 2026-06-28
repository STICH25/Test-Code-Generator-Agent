namespace PlaywrightAgentAI.Models;

public class DomSnapshot
{
    public List<string> Inputs { get; set; } = [];
    public List<string> Buttons { get; set; } = [];
    public List<string> Links { get; set; } = [];
    public List<string> Headings { get; set; } = [];
    public List<string> Elements { get; set; } = [];

    /// <summary>
    /// Contains actual HTML snippets for specific sections/elements
    /// Key: section name (e.g., "skills"), Value: HTML content
    /// </summary>
    public Dictionary<string, string> SectionHtml { get; set; } = [];

    /// <summary>
    /// Contains CSS selectors for each section to facilitate Playwright interactions
    /// Key: section name, Value: CSS selector
    /// </summary>
    public Dictionary<string, string> SectionSelectors { get; set; } = [];

    /// <summary>
    /// Contains list items within each section (e.g., skill items, project items)
    /// Key: section name, Value: list of items
    /// </summary>
    public Dictionary<string, List<string>> SectionItems { get; set; } = [];
}