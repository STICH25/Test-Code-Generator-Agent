using System.Drawing.Drawing2D;

namespace PlaywrightAgentAI.UI;

/// <summary>
/// Owner-drawn ComboBox for the dark theme.
///
/// A stock ComboBox paints its face and drop arrow through the OS theme and ignores
/// BackColor entirely in the default DrawMode, so it renders as a bright white slab on a
/// dark card. Owner drawing is the only way to control it.
/// </summary>
public class DarkComboBox : ComboBox
{
    public DarkComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Field;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Ui(10f);
        // A DropDownList sizes itself from ItemHeight, so this is what actually
        // controls the control height and lets it line up with a FieldBox.
        ItemHeight = 28;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var background = selected ? Theme.Elevated : Theme.Field;

        using (var brush = new SolidBrush(background))
            g.FillRectangle(brush, e.Bounds);

        if (e.Index >= 0 && e.Index < Items.Count)
        {
            var text = Items[e.Index]?.ToString() ?? string.Empty;
            var bounds = Rectangle.Inflate(e.Bounds, -8, 0);

            TextRenderer.DrawText(
                g, text, Font, bounds,
                Enabled ? Theme.TextPrimary : Theme.TextDisabled,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private const int WmPaint = 0x000F;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // ComboBox is a native Win32 control: overriding OnPaint does not stop the OS
        // from drawing its face and its drop-down button, which is why the stock light
        // button kept showing through. Paint over the whole client area once the base
        // has finished with it.
        if (m.Msg == WmPaint && IsHandleCreated)
        {
            using var g = Graphics.FromHwnd(Handle);
            PaintFace(g);
        }
    }

    protected override void OnPaint(PaintEventArgs e) => PaintFace(e.Graphics);

    private void PaintFace(Graphics g)
    {
        // Clear the whole client area first. The rounded fill below does not cover the
        // four corners, so without this the white face the OS already painted survives
        // there and the control shows light corner notches on a dark card.
        g.Clear(Parent?.BackColor ?? Theme.SurfaceAlt);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(bounds, Theme.FieldRadius))
        {
            using var fill = new SolidBrush(Theme.Field);
            g.FillPath(fill, path);

            using var border = new Pen(Focused ? Theme.Accent : Theme.Hairline, Focused ? 1.6f : 1f);
            g.DrawPath(border, path);
        }

        var textBounds = new Rectangle(10, 0, Width - 36, Height);
        TextRenderer.DrawText(
            g, SelectedItem?.ToString() ?? Text, Font, textBounds,
            Enabled ? Theme.TextPrimary : Theme.TextDisabled,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        DrawChevron(g);
    }

    private void DrawChevron(Graphics g)
    {
        var cx = Width - 17;
        var cy = Height / 2 - 1;

        using var pen = new Pen(Theme.TextSecondary, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        g.DrawLines(pen,
        [
            new Point(cx - 4, cy - 2),
            new Point(cx, cy + 2),
            new Point(cx + 4, cy - 2)
        ]);
    }

    // The base class repaints the OS-themed face on these, so force our own paint.
    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        Invalidate();
    }
}
