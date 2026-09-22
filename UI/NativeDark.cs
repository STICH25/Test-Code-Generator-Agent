using System.Runtime.InteropServices;

namespace PlaywrightAgentAI.UI;

/// <summary>
/// Opt the process and individual windows into the OS dark mode.
///
/// This is what turns the title bar and the scrollbars inside TextBox controls dark.
/// Neither can be reached through managed painting: the caption is drawn by DWM and
/// scrollbars by the common controls theme. Every call is best-effort — these are
/// undocumented or version-gated APIs, and a failure only costs us a light scrollbar.
/// </summary>
internal static class NativeDark
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    // Windows 10 1809 exposed this ordinal as AllowDarkModeForApp; 1903+ renamed it to
    // SetPreferredAppMode and widened the argument. 2 == ForceDark on both.
    private const int PreferredAppModeForceDark = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", EntryPoint = "#135", CharSet = CharSet.Unicode)]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string? subIdList);

    /// <summary>Call once at startup, before any window is shown.</summary>
    public static void EnableForProcess()
    {
        try
        {
            SetPreferredAppMode(PreferredAppModeForceDark);
            FlushMenuThemes();
        }
        catch
        {
            // Older Windows build without the ordinal; light scrollbars are acceptable.
        }
    }

    /// <summary>Darkens a window's title bar. Safe to call before the handle exists.</summary>
    public static void UseDarkTitleBar(Form form)
    {
        if (!form.IsHandleCreated)
        {
            form.HandleCreated += (_, _) => UseDarkTitleBar(form);
            return;
        }

        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch
        {
            // Pre-20H1 build; the caption stays light.
        }
    }

    /// <summary>Switches a control's non-client scrollbars to the dark theme.</summary>
    public static void UseDarkScrollBars(Control control)
    {
        if (!control.IsHandleCreated)
        {
            control.HandleCreated += (_, _) => UseDarkScrollBars(control);
            return;
        }

        try
        {
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }
        catch
        {
            // Theme unavailable; scrollbars stay light.
        }
    }
}
