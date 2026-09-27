using Microsoft.Playwright;

namespace PlaywrightAgentAI.Tools;

/// <summary>
/// The live Playwright page behind a <see cref="DomExplorer.Capture"/> call, kept open past
/// the point the HTML is read.
///
/// HtmlAnalyzer only learns which section matched the test objective after it has parsed the
/// HTML this class carries, so the browser has to stay alive until then if a caller wants a
/// screenshot of that specific section rather than the whole page. Disposing this closes the
/// page, browser and Playwright instance - the caller owns that lifetime once CaptureAsync
/// returns one.
/// </summary>
public sealed class DomCapture : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IPage _page;

    internal DomCapture(IPlaywright playwright, IBrowser browser, IPage page, string html)
    {
        _playwright = playwright;
        _browser = browser;
        _page = page;
        Html = html;
    }

    public string Html { get; }

    /// <summary>
    /// Screenshots the section whose heading matches <paramref name="headingText"/>, or falls
    /// back to the current viewport if there's no match. Located by the heading's own
    /// accessible name rather than a CSS selector on purpose: HtmlAnalyzer's selector for an
    /// unclassed container is just its tag name (e.g. "section"), which is ambiguous the
    /// moment a page has more than one - resolving with a bare .First against that would
    /// silently screenshot whichever same-tag section happens to come first in the DOM,
    /// not the one that actually matched the objective. Re-finding the specific heading by
    /// text and walking up from THAT instance's ancestor keeps the screenshot tied to the
    /// same element HtmlAnalyzer actually matched.
    /// </summary>
    public async Task<byte[]?> ScreenshotSectionAsync(string? headingText)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(headingText))
            {
                var heading = _page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = headingText }).First;

                if (await heading.CountAsync() > 0)
                {
                    var container = heading.Locator(
                        "xpath=ancestor::*[self::section or self::article or self::div][1]").First;

                    var target = await container.CountAsync() > 0 ? container : heading;

                    if (await target.IsVisibleAsync())
                    {
                        await target.ScrollIntoViewIfNeededAsync();
                        return await target.ScreenshotAsync();
                    }
                }
            }

            return await _page.ScreenshotAsync(new PageScreenshotOptions { FullPage = false });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not capture a section screenshot: {ex.Message}");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _page.CloseAsync();
            await _browser.CloseAsync();
            _playwright.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cleanup warning: {ex.Message}");
        }
    }
}
