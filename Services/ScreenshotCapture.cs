namespace PlaywrightAgentAI.Services;

/// <summary>
/// Filesystem side of debug screenshots: preparing and clearing the folder configured in
/// Settings. Capturing the images themselves happens in <see cref="Tools.PreviewRecorder"/>,
/// which is the thing that actually holds the WebView2 instance to capture from.
///
/// Screenshots are debug-only and disposable by design - never referenced by generated code,
/// never written into the linked test solution, and wiped on every boundary where they would
/// otherwise go stale: a new recording starting, Clear, or a successful Insert.
/// </summary>
public static class ScreenshotCapture
{
    /// <summary>Empties the folder (creating it if needed), or does nothing if not configured.</summary>
    public static void ClearFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                return;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
                File.Delete(file);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not clear the screenshots folder: {ex.Message}");
        }
    }
}
