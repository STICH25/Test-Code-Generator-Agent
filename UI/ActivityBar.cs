using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PlaywrightAgentAI.UI;

/// <summary>
/// Indeterminate progress line, styled like a track scrubber.
///
/// A stock ProgressBar in Marquee mode is drawn by the OS theme and ignores BackColor,
/// so it cannot be made to sit on a dark surface. The timer only runs while active.
/// </summary>
public class ActivityBar : Control
{
    private readonly System.Windows.Forms.Timer _timer;
    private double _phase;
    private bool _running;

    public ActivityBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = Theme.Surface;
        Height = 4;

        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += (_, _) =>
        {
            _phase += 0.012;
            if (_phase > 1)
                _phase -= 1;

            Invalidate();
        };
    }

    [DefaultValue(false)]
    public bool Running
    {
        get => _running;
        set
        {
            if (_running == value)
                return;

            _running = value;
            _phase = 0;

            if (value)
                _timer.Start();
            else
                _timer.Stop();

            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var radius = Height / 2;

        using (var track = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), radius))
        using (var brush = new SolidBrush(Theme.Elevated))
            g.FillPath(brush, track);

        if (!_running || Width <= 0)
            return;

        // Ease the sweep so it slows at the edges instead of scrolling linearly.
        var segment = Math.Max(60, Width / 4);
        var travel = Width + segment;
        var eased = (1 - Math.Cos(_phase * 2 * Math.PI)) / 2;
        var left = (int)(eased * travel) - segment;

        var clipped = Rectangle.Intersect(
            new Rectangle(left, 0, segment, Height),
            new Rectangle(0, 0, Width, Height));

        if (clipped.Width <= 0)
            return;

        using var path = Theme.RoundedRect(clipped, radius);
        using var accent = new SolidBrush(Theme.Accent);
        g.FillPath(accent, path);
    }
}
