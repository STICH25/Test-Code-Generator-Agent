using System.Drawing.Drawing2D;

namespace PlaywrightAgentAI.UI;

/// <summary>
/// Central palette, typography and geometry for the dark UI.
///
/// Fonts are created once and cached: WinForms does not take ownership of a Font
/// assigned to a control, so handing out fresh instances per control would leak.
/// </summary>
public static class Theme
{
    // Surfaces, darkest to lightest.
    public static readonly Color Page = Color.FromArgb(0, 0, 0);
    public static readonly Color Surface = Color.FromArgb(18, 18, 18);
    public static readonly Color SurfaceAlt = Color.FromArgb(24, 24, 24);
    public static readonly Color Elevated = Color.FromArgb(40, 40, 40);
    public static readonly Color Field = Color.FromArgb(33, 33, 33);
    public static readonly Color Hairline = Color.FromArgb(58, 58, 58);

    // Text.
    public static readonly Color TextPrimary = Color.FromArgb(255, 255, 255);
    public static readonly Color TextSecondary = Color.FromArgb(179, 179, 179);
    public static readonly Color TextDisabled = Color.FromArgb(110, 110, 110);

    // Accents.
    public static readonly Color Accent = Color.FromArgb(29, 185, 84);
    public static readonly Color AccentHover = Color.FromArgb(30, 215, 96);
    public static readonly Color AccentPressed = Color.FromArgb(26, 160, 73);
    public static readonly Color Danger = Color.FromArgb(240, 96, 96);
    public static readonly Color Warning = Color.FromArgb(240, 186, 84);

    // Geometry.
    public const int CardRadius = 12;
    public const int FieldRadius = 6;
    public const int Gutter = 8;
    public const int CardPadding = 18;

    private static readonly string UiFamily =
        ResolveFamily("Segoe UI Variable Text", "Segoe UI");

    private static readonly string DisplayFamily =
        ResolveFamily("Segoe UI Variable Display", "Segoe UI");

    private static readonly string MonoFamily =
        ResolveFamily("Cascadia Mono", "Consolas", FontFamily.GenericMonospace.Name);

    private static readonly Dictionary<(string, float, FontStyle), Font> FontCache = [];
    private static readonly Lock FontGate = new();

    public static Font Ui(float size, FontStyle style = FontStyle.Regular) => Cached(UiFamily, size, style);

    public static Font Display(float size, FontStyle style = FontStyle.Bold) => Cached(DisplayFamily, size, style);

    public static Font Mono(float size, FontStyle style = FontStyle.Regular) => Cached(MonoFamily, size, style);

    private static Font Cached(string family, float size, FontStyle style)
    {
        var key = (family, size, style);

        lock (FontGate)
        {
            if (!FontCache.TryGetValue(key, out var font))
            {
                font = new Font(family, size, style, GraphicsUnit.Point);
                FontCache[key] = font;
            }

            return font;
        }
    }

    /// <summary>
    /// WinForms silently substitutes a default face for a missing family, so probe each
    /// candidate and keep the first one Windows actually resolves to itself.
    /// </summary>
    private static string ResolveFamily(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            try
            {
                using var probe = new Font(candidate, 9f);
                if (string.Equals(probe.Name, candidate, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            catch
            {
                // Unusable family name; try the next candidate.
            }
        }

        return FontFamily.GenericSansSerif.Name;
    }

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}
