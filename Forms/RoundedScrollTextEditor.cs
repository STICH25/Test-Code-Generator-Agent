using System.Windows.Forms;
using System.ComponentModel;
using System;
using System.Runtime.InteropServices;
using System.Drawing;

namespace PlaywrightAgentAI.Forms
{
    public class RoundedScrollTextEditor : UserControl
    {
        private RichTextBox editor;
        private RoundedScrollbar scrollbar;

        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        [DefaultValue("")]
        public string PlaceholderText { get; set; } = "";

        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        [DefaultValue(typeof(Color), "Gray")]
        public Color PlaceholderColor { get; set; } = Color.Gray;

        private bool _readOnly = false;
        [Browsable(true)]
        [DefaultValue(false)]
        public bool ReadOnly
        {
            get => _readOnly;
            set
            {
                _readOnly = value;
                if (editor != null)
                    editor.ReadOnly = value;

                if (editor != null)
                    editor.BackColor = value
                        ? Color.FromArgb(50, 50, 50)
                        : Color.FromArgb(40, 40, 40);
            }
        }

        [Browsable(true)]
        [DefaultValue(true)]
        public bool WordWrap
        {
            get => editor?.WordWrap ?? true;
            set
            {
                if (editor != null)
                    editor.WordWrap = value;
            }
        }

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public override string Text
        {
            get => editor?.Text ?? base.Text;
            set
            {
                if (editor != null)
                {
                    editor.Text = value;
                    UpdateScrollbar();
                    OnTextChanged(EventArgs.Empty);
                }
                else
                {
                    base.Text = value;
                }
            }
        }

        // Win32 scroll helpers
        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public uint nPage;
            public int nPos;
            public int nTrackPos;
        }

        private const int SIF_RANGE = 0x1;
        private const int SIF_PAGE = 0x2;
        private const int SIF_POS = 0x4;
        private const int SIF_TRACKPOS = 0x10;
        private const int SIF_ALL = SIF_RANGE | SIF_PAGE | SIF_POS | SIF_TRACKPOS;

        private const int SB_VERT = 1;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const int SB_THUMBPOSITION = 4;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetScrollInfo(IntPtr hWnd, int fnBar, ref SCROLLINFO lpsi);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SetScrollPos(IntPtr hWnd, int nBar, int nPos, bool bRedraw);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int EM_LINESCROLL = 0x00B6;
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;

        public RoundedScrollTextEditor()
        {
            DoubleBuffered = true;

            editor = new RichTextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.None,
                Multiline = true,
                Dock = DockStyle.Fill
            };

            scrollbar = new RoundedScrollbar
            {
                Dock = DockStyle.Right,
                Width = 12,
                ThumbSize = 40,
                Radius = 8,
                TrackColor = Color.FromArgb(30, 30, 30),
                ThumbColor = Color.FromArgb(90, 90, 90)
            };

            scrollbar.Scroll += Scrollbar_Scroll;

            Controls.Add(editor);
            Controls.Add(scrollbar);

            // Resize logic
            Resize += (s, e) => LayoutEditor();

            // editor scroll and text change handlers
            editor.VScroll += Editor_VScroll;
            editor.TextChanged += Editor_TextChanged;

            // ensure the editor gets focus on hover so mouse-wheel works reliably
            editor.MouseEnter += (_, _) => editor.Focus();
            editor.GotFocus += (_, _) => UpdateScrollbar();

            // forward wheel events that land on this container to editor (backup)
            MouseWheel += (s, e) => ForwardWheelToEditor(e.Delta);
            editor.MouseWheel += (s, e) => { UpdateScrollbar(); };

            // ensure initial layout
            LayoutEditor();
            UpdateScrollbar();
        }

        private void Editor_TextChanged(object? sender, EventArgs e)
        {
            base.Text = editor.Text;
            UpdateScrollbar();
            OnTextChanged(EventArgs.Empty);
        }

        private void LayoutEditor()
        {
            // Make editor fill all available space except scrollbar width
            editor.Location = new Point(0, 0);
            editor.Size = new Size(Width - scrollbar.Width, Height);
            scrollbar.Height = Height;
        }

        private void Editor_VScroll(object sender, EventArgs e)
        {
            UpdateScrollbar();
        }

        private void Scrollbar_Scroll(object sender, EventArgs e)
        {
            if (!editor.IsHandleCreated) return;

            // Our scrollbar.Value is a pixel offset target (0..Maximum)
            int targetPixel = Math.Clamp(scrollbar.Value, 0, scrollbar.Maximum);

            // Set the native scroll position and signal the edit control to update view
            SetScrollPos(editor.Handle, SB_VERT, targetPixel, true);

            // WM_VSCROLL: wParam highword = pos, lowword = SB_THUMBPOSITION
            IntPtr wParam = new IntPtr((targetPixel << 16) | (SB_THUMBPOSITION & 0xFFFF));
            SendMessage(editor.Handle, WM_VSCROLL, wParam, IntPtr.Zero);

            // Update our thumb to match (in case native behavior adjusts)
            UpdateScrollbar();
        }

        private void ForwardWheelToEditor(int delta)
        {
            if (!editor.IsHandleCreated) return;

            // Build wParam: high-word = delta, low-word = 0
            IntPtr wParam = (IntPtr)((delta << 16) & 0xFFFF0000);
            SendMessage(editor.Handle, WM_MOUSEWHEEL, wParam, IntPtr.Zero);

            // Update our custom scrollbar to reflect any change
            UpdateScrollbar();
        }

        private void UpdateScrollbar()
        {
            if (!editor.IsHandleCreated)
            {
                // Fallback: compute by lines (less accurate)
                int lineCount = Math.Max(1, editor.GetLineFromCharIndex(editor.TextLength) + 1);
                int lineHeight = Math.Max(1, editor.Font.Height);
                int contentHeight = lineCount * lineHeight;
                int visibleHeight = Math.Max(1, editor.ClientSize.Height);
                int maxScroll = Math.Max(0, contentHeight - visibleHeight);

                if (maxScroll <= 0)
                {
                    scrollbar.Maximum = 0;
                    scrollbar.ThumbSize = scrollbar.Height;
                    scrollbar.Value = 0;
                }
                else
                {
                    scrollbar.Maximum = maxScroll;
                    int thumb = Math.Max(18, (int)((float)visibleHeight / contentHeight * scrollbar.Height));
                    scrollbar.ThumbSize = Math.Min(scrollbar.Height, thumb);
                    // leave current scrollbar.Value as-is
                }

                scrollbar.Invalidate();
                return;
            }

            // Use native scroll info for accurate range and position
            var si = new SCROLLINFO();
            si.cbSize = (uint)Marshal.SizeOf(si);
            si.fMask = SIF_ALL;

            if (GetScrollInfo(editor.Handle, SB_VERT, ref si))
            {
                // nPos is the current scroll position, nPage is visible size in scroll units
                int nMin = si.nMin;
                int nMax = si.nMax;
                int nPos = si.nPos;
                int nPage = (int)si.nPage;

                // Compute pixel maximum (approx). We'll treat pos and max as pixel units for our custom scrollbar.
                // nPage is number of units visible; map to pixels using font height as approximation.
                int lineHeight = Math.Max(1, editor.Font.Height);
                int visiblePixels = editor.ClientSize.Height;
                int contentLinesEstimate = Math.Max(1, editor.GetLineFromCharIndex(editor.TextLength) + 1);
                int contentHeightEstimate = contentLinesEstimate * lineHeight;

                int maxPixel = Math.Max(0, contentHeightEstimate - visiblePixels);

                scrollbar.Maximum = maxPixel;

                // Thumb size proportional to visible fraction
                if (maxPixel <= 0)
                {
                    scrollbar.ThumbSize = scrollbar.Height;
                    scrollbar.Value = 0;
                }
                else
                {
                    int thumb = Math.Max(18, (int)((float)visiblePixels / contentHeightEstimate * scrollbar.Height));
                    scrollbar.ThumbSize = Math.Min(scrollbar.Height, thumb);

                    // Map native scroll position nPos to pixel offset. nPos is in "scroll units"; best estimate:
                    // Use first visible line for accurate mapping.
                    int firstVisibleLine = SendMessage(editor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                    int currentOffsetPixels = Math.Clamp(firstVisibleLine * lineHeight, 0, maxPixel);
                    scrollbar.Value = currentOffsetPixels;
                }

                scrollbar.Invalidate();
            }
            else
            {
                // fallback to line-based method if GetScrollInfo fails
                int lineCount = Math.Max(1, editor.GetLineFromCharIndex(editor.TextLength) + 1);
                int lineHeight = Math.Max(1, editor.Font.Height);
                int contentHeight = lineCount * lineHeight;
                int visibleHeight = Math.Max(1, editor.ClientSize.Height);
                int maxScroll = Math.Max(0, contentHeight - visibleHeight);

                scrollbar.Maximum = maxScroll;
                if (maxScroll <= 0)
                {
                    scrollbar.ThumbSize = scrollbar.Height;
                    scrollbar.Value = 0;
                }
                else
                {
                    int thumb = Math.Max(18, (int)((float)visibleHeight / contentHeight * scrollbar.Height));
                    scrollbar.ThumbSize = Math.Min(scrollbar.Height, thumb);
                    int firstVisibleLine = SendMessage(editor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                    scrollbar.Value = Math.Clamp(firstVisibleLine * lineHeight, 0, maxScroll);
                }
                scrollbar.Invalidate();
            }
        }

        // Ensure that mouse wheel messages that reach this UserControl are forwarded to the inner editor.
        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL) && editor != null && editor.IsHandleCreated)
            {
                // Forward the Windows message to the RichTextBox so it handles scrolling even if focus routing differs.
                SendMessage(editor.Handle, m.Msg, m.WParam, m.LParam);
                // Update our custom scrollbar to reflect any scroll change
                UpdateScrollbar();
                return;
            }

            base.WndProc(ref m);
        }
    }
}