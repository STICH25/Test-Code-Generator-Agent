using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PlaywrightAgentAI.UI;

/// <summary>
/// A rounded surface panel. The corners are cut out of the page background, so the
/// control's own BackColor must match whatever sits behind it.
/// </summary>
public class Card : Panel
{
    private int _radius = Theme.CardRadius;
    private Color _fill = Theme.SurfaceAlt;

    public Card()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Page;
        ForeColor = Theme.TextPrimary;
        Padding = new Padding(Theme.CardPadding);
    }

    [DefaultValue(Theme.CardRadius)]
    public int Radius
    {
        get => _radius;
        set { _radius = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill
    {
        get => _fill;
        set { _fill = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), _radius);
        using var brush = new SolidBrush(_fill);
        e.Graphics.FillPath(brush, path);

        base.OnPaint(e);
    }
}
