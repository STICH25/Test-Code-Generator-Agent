using Microsoft.Playwright;
using System;
using System.Threading;

namespace PlaywrightAgentAI.Tools;

public class DomExplorer
{
    private const int TimeoutMs = 30000; // 30 seconds

    public async Task<string> CaptureDom(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));

        cancellationToken.ThrowIfCancellationRequested();

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

            cancellationToken.ThrowIfCancellationRequested();

            page = await browser.NewPageAsync();

            page.SetDefaultTimeout(TimeoutMs);
            page.SetDefaultNavigationTimeout(TimeoutMs);

            Console.WriteLine("Navigating to page...");

            // IMPORTANT: Do NOT use NetworkIdle for modern sites
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            // Give dynamic sites a moment to settle (optional). Task.Delay rather than
            // WaitForTimeoutAsync so a cancel during the settle window takes effect at once.
            await Task.Delay(1500, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine("Capturing DOM content...");
            var content = await page.ContentAsync();

            return content;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("DOM capture cancelled.");
            throw;
        }
        catch (PlaywrightException ex)
        {
            // Playwright signals navigation timeouts through PlaywrightException, not
            // System.TimeoutException, so both cases land here.
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

    /// <summary>
    /// Same navigation and capture as <see cref="CaptureDom"/>, but keeps the browser open
    /// and hands it back wrapped in a <see cref="DomCapture"/> instead of closing it - so a
    /// caller that wants a screenshot of a specific section (known only after the HTML has
    /// been analyzed) still has a live page to take it from.
    /// </summary>
    public async Task<DomCapture> Capture(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));

        cancellationToken.ThrowIfCancellationRequested();

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

            cancellationToken.ThrowIfCancellationRequested();

            page = await browser.NewPageAsync();

            page.SetDefaultTimeout(TimeoutMs);
            page.SetDefaultNavigationTimeout(TimeoutMs);

            Console.WriteLine("Navigating to page...");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            await Task.Delay(1500, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine("Capturing DOM content...");
            var content = await page.ContentAsync();

            return new DomCapture(playwright, browser, page, content);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("DOM capture cancelled.");
            await CleanUp(page, browser, playwright);
            throw;
        }
        catch (PlaywrightException ex)
        {
            Console.Error.WriteLine($"Playwright error navigating to {url}: {ex.Message}");
            await CleanUp(page, browser, playwright);
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error capturing DOM: {ex.Message}");
            await CleanUp(page, browser, playwright);
            throw;
        }
    }

    /// <summary>Only used on the failure path of <see cref="Capture"/> - on success the returned DomCapture owns disposal.</summary>
    private static async Task CleanUp(IPage? page, IBrowser? browser, IPlaywright? playwright)
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
