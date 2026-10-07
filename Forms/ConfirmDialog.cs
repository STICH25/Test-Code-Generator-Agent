using PlaywrightAgentAI.UI;

namespace PlaywrightAgentAI.Forms;

/// <summary>
/// A themed Yes/No question. <see cref="MessageBox"/> cannot be darkened, and a bright
/// system dialog in the middle of this window reads as a different program.
///
/// "No" is the default and the Escape target, so a stray Enter or a click that lands on the
/// dialog as it opens keeps the user's work - these questions guard destructive actions.
/// </summary>
public class ConfirmDialog : Form
{
    private const int ContentWidth = 400;

    private ConfirmDialog(string title, string message, string yesText, string noText)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Page;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Ui(9f);
        Padding = new Padding(Theme.Gutter);

        var bodyFont = Theme.Ui(10f);
        var bodyHeight = TextRenderer.MeasureText(message, bodyFont,
            new Size(ContentWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;

        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.SurfaceAlt
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, bodyHeight + 12));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            Font = Theme.Display(14f),
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = message,
            Font = bodyFont,
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);

        var yes = new PillButton { Text = yesText, Style = PillStyle.Primary, Width = 120, Dock = DockStyle.Right };
        yes.Click += (s, e) => { DialogResult = DialogResult.Yes; Close(); };

        var no = new PillButton { Text = noText, Style = PillStyle.Outline, Width = 110, Dock = DockStyle.Right, Margin = Padding.Empty };
        no.Click += (s, e) => { DialogResult = DialogResult.No; Close(); };

        var buttonRow = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, Padding = new Padding(0, 10, 0, 0) };
        // Docked controls are laid out last-added-first, so adding Yes before No puts No
        // closest to the edge - the position Windows users expect the safe answer to be in.
        buttonRow.Controls.Add(yes);
        buttonRow.Controls.Add(no);
        layout.Controls.Add(buttonRow, 0, 2);

        card.Controls.Add(layout);
        Controls.Add(card);

        AcceptButton = no;
        CancelButton = no;

        ClientSize = new Size(
            ContentWidth + card.Padding.Horizontal + Padding.Horizontal,
            36 + bodyHeight + 12 + 52 + card.Padding.Vertical + Padding.Vertical);
        _noButton = no;
        ActiveControl = no;
    }

    private readonly PillButton _noButton;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        NativeDark.UseDarkTitleBar(this);
        AppIcon.ApplyTo(this);
    }

    /// <summary>Shows the question; true only when the user explicitly chose the yes option.</summary>
    public static bool Ask(IWin32Window owner, string title, string message, string yesText = "Yes", string noText = "No")
    {
        using var dialog = new ConfirmDialog(title, message, yesText, noText);
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}
