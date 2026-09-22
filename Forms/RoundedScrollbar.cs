using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PlaywrightAgentAI.Forms
{
    public class RoundedScrollbar : Control
    {
        private int _maximum = 0;
        private int _value = 0;
        private int _thumbSize = 40;
        private int _radius = 8;

        private bool _dragging;
        private int _dragOffset;

        public event EventHandler? Scroll;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(0, value);

                if (_value > _maximum)
                    _value = _maximum;

                Invalidate();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set
            {
                int newValue = Math.Max(0, Math.Min(_maximum, value));

                if (_value == newValue)
                    return;

                _value = newValue;

                Invalidate();

                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ThumbSize
        {
            get => _thumbSize;
            set
            {
                _thumbSize = Math.Max(20, value);
                Invalidate();
            }
        }

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius
        {
            get => _radius;
            set
            {
                _radius = Math.Max(2, value);
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color TrackColor { get; set; } = Color.FromArgb(35, 35, 35);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color ThumbColor { get; set; } = Color.FromArgb(90, 90, 90);

        public RoundedScrollbar()
        {
            Width = 12;
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var trackBrush = new SolidBrush(TrackColor);
            e.Graphics.FillRectangle(trackBrush, ClientRectangle);

            int travel = Math.Max(1, Height - ThumbSize);

            int thumbTop = 0;

            if (Maximum > 0)
                thumbTop = (int)((float)Value / Maximum * travel);

            var thumbRect = new Rectangle(
                2,
                thumbTop,
                Width - 4,
                ThumbSize);

            using var thumbBrush = new SolidBrush(ThumbColor);

            e.Graphics.FillRoundedRectangle(
                thumbBrush,
                thumbRect,
                Radius);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            _dragging = true;

            int travel = Math.Max(1, Height - ThumbSize);

            int thumbTop = Maximum == 0
                ? 0
                : (int)((float)Value / Maximum * travel);

            _dragOffset = e.Y - thumbTop;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_dragging)
                return;

            int travel = Math.Max(1, Height - ThumbSize);

            int thumbTop = e.Y - _dragOffset;

            thumbTop = Math.Max(0, Math.Min(travel, thumbTop));

            Value = (int)((float)thumbTop / travel * Maximum);
        }
    }

    public static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(
            this Graphics g,
            Brush brush,
            Rectangle rect,
            int radius)
        {
            radius = Math.Min(radius,
                Math.Min(rect.Width / 2, rect.Height / 2));

            using var path = new GraphicsPath();

            int d = radius * 2;

            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);

            path.CloseFigure();

            g.FillPath(brush, path);
        }
    }
}