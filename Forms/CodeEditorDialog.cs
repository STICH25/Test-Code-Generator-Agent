using PlaywrightAgentAI.UI;

namespace PlaywrightAgentAI.Forms;

/// <summary>
/// Full-window editor for one generated file.
///
/// Opened from the Edit button on the Feature / Page Object / Steps tab. Editing happens
/// entirely in memory - Save copies the text back to the caller and does not touch disk;
/// writing into the solution only happens later, when Insert is pressed.
/// </summary>
public class CodeEditorDialog : Form
{
    private readonly string _original;
    private TextBox _editor = null!;
    private PillButton _saveButton = null!;
    private Label _dirtyLabel = null!;

    public CodeEditorDialog(string title, string content)
    {
        _original = content;
        BuildLayout(title);
        _editor.Text = content.ReplaceLineEndings();
        _editor.SelectionStart = 0;
    }

    /// <summary>The edited text. Only meaningful when the dialog returns OK.</summary>
    public string Content => _editor.Text;

    private void BuildLayout(string title)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(920, 720);
        MinimumSize = new Size(560, 420);
        BackColor = Theme.Page;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Ui(9f);
        Padding = new Padding(Theme.Gutter);

        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.SurfaceAlt
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));  // 0 title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));  // 1 hint
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 2 editor
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));  // 3 buttons

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            Font = Theme.Display(14f),
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _dirtyLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = Theme.Ui(8.5f),
            ForeColor = Theme.TextDisabled,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.TopLeft,
            Text = "Changes are kept in memory until you press Insert on the main window."
        };
        layout.Controls.Add(_dirtyLabel, 0, 1);

        _editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsTab = true,
            AcceptsReturn = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Mono(10f)
        };
        NativeDark.UseDarkScrollBars(_editor);

        var editorCard = new Card
        {
            Dock = DockStyle.Fill,
            Fill = Theme.Surface,
            Radius = 8,
            BackColor = Theme.SurfaceAlt,
            Padding = new Padding(12, 10, 6, 10),
            Margin = new Padding(0, 6, 0, 6)
        };
        editorCard.Controls.Add(_editor);
        layout.Controls.Add(editorCard, 0, 2);

        _saveButton = new PillButton { Text = "Save", Style = PillStyle.Primary, Width = 120, Dock = DockStyle.Right };
        _saveButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

        var cancelButton = new PillButton { Text = "Cancel", Style = PillStyle.Outline, Width = 110, Dock = DockStyle.Right };
        cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        var revertButton = new PillButton { Text = "Revert", Style = PillStyle.Ghost, Width = 100, Dock = DockStyle.Left };
        revertButton.Click += (s, e) => _editor.Text = _original.ReplaceLineEndings();

        var buttonRow = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, Padding = new Padding(0, 10, 0, 0) };
        // Enter must insert a newline in a multi-line editor, not submit the dialog, so
        // Save is intentionally not wired as AcceptButton - only Escape gets a shortcut.
        buttonRow.Controls.Add(revertButton);
        buttonRow.Controls.Add(cancelButton);
        buttonRow.Controls.Add(_saveButton);
        layout.Controls.Add(buttonRow, 0, 3);

        card.Controls.Add(layout);
        Controls.Add(card);

        CancelButton = cancelButton;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        NativeDark.UseDarkTitleBar(this);
        AppIcon.ApplyTo(this);
        _editor.Focus();
    }
}
