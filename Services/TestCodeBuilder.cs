using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Services;

public class TestCodeBuilder
{
    public string Build(string url, DomSnapshot dom)
    {
        var inputLines = string.Join("\n",
            dom.Inputs.Select(i => $"        // Detected input: {i}"));

        var buttonLines = string.Join("\n",
            dom.Buttons.Select(b => $"        // Detected button: {b}"));

        return $@"
using Microsoft.Playwright;

public class GeneratedTest
{{
    public async Task Run()
    {{
        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions {{ Headless = false }});

        var page = await browser.NewPageAsync();

        // Setup: Navigate to the page (handled before this test runs)
        // await page.GotoAsync(""{url}"");

{inputLines}

{buttonLines}

        // Example interaction pattern:
        // await page.FillAsync(""#username"", ""test"");
        // await page.ClickAsync(""button:has-text('Login')"");
        // await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }}
}}
";
    }
}