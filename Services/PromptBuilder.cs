using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Services;

public class PromptBuilder
{
    public string Build(UserPromptRequest request, DomSnapshot dom)
    {
        var inputs = string.Join(", ", dom.Inputs);
        var buttons = string.Join(", ", dom.Buttons);
        var links = string.Join(", ", dom.Links);
        var headings = string.Join(", ", dom.Headings);
        var elements = string.Join(", ", dom.Elements.Distinct());

        // Build section HTML info
        var sectionInfo = new System.Text.StringBuilder();
        foreach (var section in dom.SectionHtml)
        {
            sectionInfo.AppendLine($"\n{section.Key.ToUpper()} Section HTML:");
            sectionInfo.AppendLine(section.Value);
        }

        return $@"You are a senior QA automation engineer writing Playwright .NET C# tests.

IMPORTANT: Generate tests using REAL selectors from the actual DOM structure provided below.

URL: {request.Url}

ACTUAL PAGE STRUCTURE:
{sectionInfo}

Detected Page Elements:
- Inputs: {inputs}
- Buttons: {buttons}
- Links: {links}
- Headings: {headings}
- Sections/Cards: {elements}

Test Objective:
{request.Action}

CRITICAL REQUIREMENTS:
1. Analyze the HTML structure above and use ACTUAL class names and tag names
2. Use correct Playwright locators based on real DOM elements
3. For the skill section example: use ""div.panel"" for the container
4. Use ""h4"" or heading selectors for titles
5. Use ""ul > li"" for list items or correct actual structure
6. Extract TEXT content of elements to verify they exist
7. Use getByText(), getByRole(), or CSS selectors matching the real DOM
8. DO NOT invent class names or IDs that don't exist in the HTML
9. Include assertions to verify element visibility and text content
10. Add waits for dynamic content if needed

OUTPUT FORMAT:
Generate a complete test class with proper structure. Include:
- using statements at the top
- public class declaration
- public async Task method signature
- Playwright setup code (browser, page initialization)
- NO page.GotoAsync() call (navigation is handled separately)
- Test assertions and interactions based on the objective
- Proper cleanup with await browser.CloseAsync() at the end

Example structure (fill in with actual logic):
using Microsoft.Playwright;

public class GeneratedTest
{{
    public async Task Run()
    {{
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions {{ Headless = false }});
        var page = await browser.NewPageAsync();
        
        // TEST ASSERTIONS AND INTERACTIONS GO HERE
        // DO NOT include page.GotoAsync(""{request.Url}"")
        
        await browser.CloseAsync();
    }}
}}

EXCLUSIONS:
- DO NOT include page.GotoAsync()
- Navigation setup will be handled separately
- Keep everything else as shown in the example

Return ONLY valid C# code without markdown formatting.";
    }
}