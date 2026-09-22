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
            The response MUST contain exactly two sections, in this order, delimited by the exact markers below:

            <<<PLAYWRIGHT>>>
            (Only valid C# Playwright test code goes here. RETURN ONLY the C# code for the Playwright test; do NOT add commentary.)
            <<<GHERKIN>>>
            (Only the Gherkin payload goes here. Provide the Feature step line followed by the C# step-definition method. Use the SpecFlow attribute style, e.g. [Given(""the client started shopping"")] and the corresponding method with a PendingStepException.)

            STRICT RULES:
            - Do NOT include any other text outside the two markers.
            - Do NOT include page.GotoAsync() (navigation is handled separately).
            - The Playwright section must be valid C# test code.
            - The Gherkin section must include the plain feature step line and a valid C# step-definition skeleton.
            - Use real selectors present in the DOM above; do not invent classes or ids.
            - If you cannot generate one of the sections, still include the markers and leave that section empty.

            EXAMPLE RESPONSE (exact format expected):

            <<<PLAYWRIGHT>>>
            using Microsoft.Playwright;

            public class GeneratedTest
            {{
                public async Task Run()
                {{
                    using var playwright = await Playwright.CreateAsync();
                    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions {{ Headless = false }});
                    var page = await browser.NewPageAsync();

                    // Valid Playwright interactions using real selectors...
                    await browser.CloseAsync();
                }}
            }}
            <<<GHERKIN>>>
            Given the client started shopping

            [Given(""the client started shopping"")]
            public void GivenTheClientStartedShopping()
            {{
                throw new PendingStepException();
            }}

            END OF INSTRUCTIONS.";
    }
}