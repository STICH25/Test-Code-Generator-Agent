using Microsoft.Playwright;
using System;

namespace PlaywrightAgentAI.Tools;

public class DomExplorer
{
    private const int TimeoutMs = 30000; // 30 seconds

    public async Task<string> CaptureDom(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));

        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IPage? page = null;

        try
        {
            Console.WriteLine($"Launching Playwright for {url}...");
            playwright = await Playwright.CreateAsync();

            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            page = await browser.NewPageAsync();

            page.SetDefaultTimeout(TimeoutMs);
            page.SetDefaultNavigationTimeout(TimeoutMs);

            Console.WriteLine("Navigating to page...");

            // IMPORTANT: Do NOT use NetworkIdle for modern sites
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            // Give dynamic sites a moment to settle (optional)
            await page.WaitForTimeoutAsync(1500);

            Console.WriteLine("Capturing DOM content...");
            var content = await page.ContentAsync();

            return content;
        }
        catch (TimeoutException ex)
        {
            Console.Error.WriteLine($"Timeout loading {url}: {ex.Message}");
            throw new Exception($"Timeout loading {url}. The site may never reach an idle state.");
        }
        catch (PlaywrightException ex)
        {
            Console.Error.WriteLine($"Playwright error navigating to {url}: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error capturing DOM: {ex.Message}");
            throw;
        }
        finally
        {
            try
            {
                if (page != null) await page.CloseAsync();
                if (browser != null) await browser.CloseAsync();
                playwright?.Dispose();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Cleanup warning: {ex.Message}");
            }
        }
    }
}
