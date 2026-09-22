using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PlaywrightAgentAI.UI;

public enum PillStyle
{
    /// <summary>Solid accent fill. One per screen — this is the primary action.</summary>
    Primary,

    /// <summary>Hairline outline on the card surface. Secondary actions.</summary>
    Outline,

    /// <summary>No border until hovered. Tertiary actions.</summary>
    Ghost
}

/// <summary>
/// Pill-shaped button drawn entirely by hand. The stock Button cannot round its corners
/// or keep a custom BackColor when disabled, both of which this design needs.
/// </summary>
public class PillButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private PillStyle _style = PillStyle.Primary;

    public PillButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.SurfaceAlt;
        Font = Theme.Ui(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Height = 38;
        UseVisualStyleBackColor = false;
    }

    [DefaultValue(PillStyle.Primary)]
    public PillStyle Style
    {
        get => _style;
        set { _style = value; Invalidate(); }
    }

    /// <summary>Grows the text slightly, for the single hero action.</summary>
    [DefaultValue(false)]
    public bool Hero { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Spotify's primary button lifts on hover; reproduce with a small inset.
        var lift = _style == PillStyle.Primary && _hovered && !_pressed && Enabled ? 1 : 0;
        var bounds = new Rectangle(lift, lift, Width - 1 - lift * 2, Height - 1 - lift * 2);
        var radius = bounds.Height / 2;

        using var path = Theme.RoundedRect(bounds, radius);

        ResolveColors(out var fill, out var border, out var text);

        if (fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }

        if (border.A > 0)
        {
            using var pen = new Pen(border, 1f);
            g.DrawPath(pen, path);
        }

        var font = Hero ? Theme.Ui(10.5f, FontStyle.Bold) : Font;
        TextRenderer.DrawText(
            g, Text, font, bounds, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused && Enabled)
        {
            using var focusPen = new Pen(Theme.Mix(text, Theme.Page, 0.55), 1f) { DashStyle = DashStyle.Dot };
            using var focusPath = Theme.RoundedRect(Rectangle.Inflate(bounds, -4, -4), Math.Max(0, radius - 4));
            g.DrawPath(focusPen, focusPath);
        }
    }

    private void ResolveColors(out Color fill, out Color border, out Color text)
    {
        if (!Enabled)
        {
            fill = _style == PillStyle.Primary ? Theme.Elevated : Color.Transparent;
            border = _style == PillStyle.Outline ? Theme.Mix(Theme.Hairline, Theme.Page, 0.5) : Color.Transparent;
            text = Theme.TextDisabled;
            return;
        }

        switch (_style)
        {
            case PillStyle.Primary:
                fill = _pressed ? Theme.AccentPressed : _hovered ? Theme.AccentHover : Theme.Accent;
                border = Color.Transparent;
                text = Color.Black;
                break;

            case PillStyle.Outline:
                fill = _pressed ? Theme.Elevated : Color.Transparent;
                border = _hovered ? Theme.TextPrimary : Theme.Hairline;
                text = _hovered ? Theme.TextPrimary : Theme.TextSecondary;
                break;

            default:
                fill = _hovered ? Theme.Elevated : Color.Transparent;
                border = Color.Transparent;
                text = _hovered ? Theme.TextPrimary : Theme.TextSecondary;
                break;
        }
    }
}
