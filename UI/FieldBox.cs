using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace PlaywrightAgentAI.UI;

/// <summary>
/// A rounded, dark text field.
///
/// TextBox itself only offers None / FixedSingle / Fixed3D borders and always paints its
/// own square edge, so the rounded shape and focus ring are drawn by this host panel and
/// the inner TextBox runs borderless and inset.
/// </summary>
public class FieldBox : Panel
{
    private readonly TextBox _inner;
    private bool _focused;

    public FieldBox(bool multiline = false)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.SurfaceAlt;
        Padding = multiline ? new Padding(12, 10, 8, 10) : new Padding(12, 0, 12, 0);

        _inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Field,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Ui(10f),
            Multiline = multiline,
            Dock = multiline ? DockStyle.Fill : DockStyle.None,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            AcceptsReturn = multiline,
            AcceptsTab = multiline
        };

        _inner.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _inner.LostFocus += (_, _) => { _focused = false; Invalidate(); };

        Controls.Add(_inner);

        if (multiline)
        {
            NativeDark.UseDarkScrollBars(_inner);
            Height = 110;
        }
        else
        {
            // A single-line TextBox sizes itself to the font, so centre it vertically
            // rather than trying to stretch it.
            Height = 40;
            _inner.Width = 10;
            Resize += (_, _) => LayoutSingleLine();
        }
    }

    public TextBox Inner => _inner;

    [AllowNull]
    public override string Text
    {
        get => _inner.Text;
        set => _inner.Text = value ?? string.Empty;
    }

    [DefaultValue("")]
    public string PlaceholderText
    {
        get => _inner.PlaceholderText;
        set => _inner.PlaceholderText = value;
    }

    private void LayoutSingleLine()
    {
        _inner.Width = Math.Max(10, Width - Padding.Horizontal);
        _inner.Left = Padding.Left;
        _inner.Top = (Height - _inner.Height) / 2;
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        if (!_inner.Multiline)
            LayoutSingleLine();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(bounds, Theme.FieldRadius);

        using (var brush = new SolidBrush(Theme.Field))
            e.Graphics.FillPath(brush, path);

        using var pen = new Pen(_focused ? Theme.Accent : Theme.Hairline, _focused ? 1.6f : 1f);
        e.Graphics.DrawPath(pen, path);

        base.OnPaint(e);
    }
}
