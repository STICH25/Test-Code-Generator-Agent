using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PlaywrightAgentAI.UI;

/// <summary>
/// A row of chip-style tabs. Replaces TabControl, which draws OS-themed tab headers that
/// cannot be recoloured for a dark surface.
/// </summary>
public class SegmentedTabs : Control
{
    private readonly List<string> _items = [];
    private readonly List<Rectangle> _hitBoxes = [];
    private int _selectedIndex;
    private int _hoverIndex = -1;

    public SegmentedTabs()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.SurfaceAlt;
        Font = Theme.Ui(9.5f, FontStyle.Bold);
        Height = 32;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? SelectedIndexChanged;

    public void SetItems(params string[] items)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _items.Count - 1));
        Invalidate();
    }

    [DefaultValue(0)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, _items.Count - 1));
            if (clamped == _selectedIndex)
                return;

            _selectedIndex = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(e.Location);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var index = IndexAt(e.Location);
        if (index >= 0)
            SelectedIndex = index;

        base.OnMouseDown(e);
    }

    private int IndexAt(Point point)
    {
        for (var i = 0; i < _hitBoxes.Count; i++)
        {
            if (_hitBoxes[i].Contains(point))
                return i;
        }

        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        _hitBoxes.Clear();
        var x = 0;

        for (var i = 0; i < _items.Count; i++)
        {
            var size = TextRenderer.MeasureText(g, _items[i], Font);
            var chip = new Rectangle(x, 0, size.Width + 28, Height);
            _hitBoxes.Add(chip);

            var selected = i == _selectedIndex;
            var hovered = i == _hoverIndex;

            if (selected || hovered)
            {
                var fill = selected ? Theme.Elevated : Theme.Mix(Theme.SurfaceAlt, Theme.Elevated, 0.5);
                using var path = Theme.RoundedRect(new Rectangle(chip.X, chip.Y, chip.Width, chip.Height), chip.Height / 2);
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }

            var color = selected ? Theme.TextPrimary : hovered ? Theme.TextPrimary : Theme.TextSecondary;
            TextRenderer.DrawText(
                g, _items[i], Font, chip, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            x = chip.Right + 6;
        }
    }
}
