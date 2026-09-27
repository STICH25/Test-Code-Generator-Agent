using PlaywrightAgentAI.Models;
using PlaywrightAgentAI.UI;

namespace PlaywrightAgentAI.Forms;

/// <summary>
/// Shows the screenshots captured during the last recording, in the order they were taken,
/// each captioned with the recorded action it corresponds to.
///
/// Opened modally (see MainForm.OpenScreenshotsViewer) so nothing can clear or delete the
/// underlying files - via Clear, Insert, or starting a new recording - while an image from
/// this batch is open and loaded into the PictureBox.
/// </summary>
public class ScreenshotViewerDialog : Form
{
    private readonly IReadOnlyList<ScreenshotEntry> _entries;

    private ListBox _list = null!;
    private PictureBox _picture = null!;
    private Label _captionLabel = null!;
    private Label _positionLabel = null!;

    public ScreenshotViewerDialog(IReadOnlyList<ScreenshotEntry> entries)
    {
        _entries = entries;
        BuildLayout();
    }

    private void BuildLayout()
    {
        Text = "Recorded Screenshots";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(1080, 700);
        MinimumSize = new Size(760, 480);
        BackColor = Theme.Page;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Ui(9f);
        Padding = new Padding(Theme.Gutter);

        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 8,
            BackColor = Theme.SurfaceAlt
        };

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.None,
            Font = Theme.Ui(9.5f),
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 46
        };
        foreach (var entry in _entries)
            _list.Items.Add(entry);
        _list.DrawItem += DrawListItem;
        _list.SelectedIndexChanged += (s, e) => ShowSelected();
        NativeDark.UseDarkScrollBars(_list);

        var listCard = new Card
        {
            Dock = DockStyle.Fill,
            Fill = Theme.Surface,
            Radius = 8,
            BackColor = Theme.SurfaceAlt,
            Padding = new Padding(4)
        };
        listCard.Controls.Add(_list);

        _picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            SizeMode = PictureBoxSizeMode.Zoom
        };

        var previewHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt
        };
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _captionLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.SurfaceAlt,
            Font = Theme.Ui(10f, FontStyle.Bold)
        };

        _positionLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextDisabled,
            BackColor = Theme.SurfaceAlt,
            Font = Theme.Ui(9f),
            Margin = new Padding(12, 0, 0, 0)
        };

        previewHeader.Controls.Add(_captionLabel, 0, 0);
        previewHeader.Controls.Add(_positionLabel, 1, 0);

        var previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt };
        previewPanel.Controls.Add(_picture);
        previewPanel.Controls.Add(previewHeader);

        var previewCard = new Card
        {
            Dock = DockStyle.Fill,
            Fill = Theme.Surface,
            Radius = 8,
            BackColor = Theme.SurfaceAlt,
            Padding = new Padding(10)
        };
        previewCard.Controls.Add(previewPanel);

        split.Panel1.Controls.Add(listCard);
        split.Panel2.Controls.Add(previewCard);

        var closeButton = new PillButton { Text = "Close", Style = PillStyle.Primary, Width = 120, Dock = DockStyle.Right };
        closeButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

        var buttonRow = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Page, Padding = new Padding(0, 10, 0, 0) };
        buttonRow.Controls.Add(closeButton);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Page };
        // Bottom-docked sibling added first, Fill-docked one last - the same ordering the
        // rest of this codebase relies on for docked stacks to resolve predictably.
        body.Controls.Add(buttonRow);
        body.Controls.Add(split);

        card.Controls.Add(body);
        Controls.Add(card);

        SplitLayout.Configure(split, minPanel1: 260, minPanel2: 400, desired: 300);

        CancelButton = closeButton;

        Shown += (s, e) =>
        {
            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
        };
    }

    private void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _entries.Count)
            return;

        var entry = _entries[e.Index];
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var back = new SolidBrush(selected ? Theme.Elevated : Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);

        var indexRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 4, 34, e.Bounds.Height - 8);
        TextRenderer.DrawText(e.Graphics, entry.Index.ToString(), Theme.Ui(9f, FontStyle.Bold),
            indexRect, selected ? Theme.Accent : Theme.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var textRect = new Rectangle(indexRect.Right, e.Bounds.Y + 4, e.Bounds.Width - indexRect.Width - 20, e.Bounds.Height - 8);
        TextRenderer.DrawText(e.Graphics, entry.Description, Theme.Ui(9.5f),
            textRect, selected ? Theme.TextPrimary : Theme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.WordEllipsis | TextFormatFlags.Top);
    }

    private void ShowSelected()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _entries.Count)
            return;

        var entry = _entries[_list.SelectedIndex];

        var previous = _picture.Image;
        _picture.Image = File.Exists(entry.FilePath) ? Image.FromFile(entry.FilePath) : null;
        previous?.Dispose();

        _captionLabel.Text = entry.Description;
        _positionLabel.Text = $"{_list.SelectedIndex + 1} of {_entries.Count}  -  {entry.FileName}";
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        NativeDark.UseDarkTitleBar(this);
        AppIcon.ApplyTo(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _picture.Image?.Dispose();

        base.Dispose(disposing);
    }
}
