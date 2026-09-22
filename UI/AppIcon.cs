using System.Reflection;

namespace PlaywrightAgentAI.UI;

/// <summary>
/// The application icon, loaded once from the embedded .ico.
///
/// Loading the multi-size .ico rather than calling Icon.ExtractAssociatedIcon matters:
/// extraction hands back a single 32x32 frame, which Windows then squashes to 16x16 for
/// the title bar and it comes out blurred. Reading the icon file lets each surface pick
/// the frame drawn for its size.
/// </summary>
public static class AppIcon
{
    private static readonly Lock Gate = new();
    private static Icon? _icon;

    public static Icon? Value
    {
        get
        {
            lock (Gate)
            {
                if (_icon != null)
                    return _icon;

                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    using var stream = assembly.GetManifestResourceStream("PlaywrightAgentAI.Assets.app.ico");

                    if (stream != null)
                        _icon = new Icon(stream);
                }
                catch (Exception ex)
                {
                    // A missing icon should never stop the app opening.
                    Console.Error.WriteLine($"Could not load the application icon: {ex.Message}");
                }

                return _icon;
            }
        }
    }

    /// <summary>Applies the icon to a form, doing nothing if it could not be loaded.</summary>
    public static void ApplyTo(Form form)
    {
        var icon = Value;
        if (icon != null)
            form.Icon = icon;
    }
}
