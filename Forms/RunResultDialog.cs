using PlaywrightAgentAI.Services;
using PlaywrightAgentAI.UI;

namespace PlaywrightAgentAI.Forms;

/// <summary>
/// Shows what happened when the generated test was run, and - only when tests ran and failed -
/// offers to mark them as probably waiting on an unfinished PBI.
///
/// The offer is the user's call, not the app's: a locator that does not exist yet and one the
/// model got wrong fail the same way, so the app cannot tell them apart. A compile error is
/// never offered a note, because that is a defect in the output and not in the PBI.
/// </summary>
public class RunResultDialog : Form
{
    private readonly bool _offerNote;
    private readonly TextBox _details;

    private RunResultDialog(TestRunResult result, string? pbiLabel)
    {
        _offerNote = result.Outcome == TestRunOutcome.Failed;

        Text = "Test run";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(520, 360);
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // 1 explanation
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 2 details
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));  // 3 buttons

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = Headline(result),
            Font = Theme.Display(14f),
            ForeColor = result.Outcome == TestRunOutcome.Passed ? Theme.Accent : Theme.Warning,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = Theme.Ui(9.5f),
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.TopLeft,
            Text = Explanation(result, pbiLabel)
        }, 0, 1);

        var details = _details = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Mono(9.5f),
            Text = Details(result).ReplaceLineEndings()
        };
        NativeDark.UseDarkScrollBars(details);

        var detailsCard = new Card
        {
            Dock = DockStyle.Fill,
            Fill = Theme.Surface,
            Radius = 8,
            BackColor = Theme.SurfaceAlt,
            Padding = new Padding(12, 10, 6, 10),
            Margin = new Padding(0, 6, 0, 6)
        };
        detailsCard.Controls.Add(details);
        layout.Controls.Add(detailsCard, 0, 2);

        var close = new PillButton
        {
            Text = "Close",
            Style = _offerNote ? PillStyle.Outline : PillStyle.Primary,
            Width = 110,
            Dock = DockStyle.Right
        };
        close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        var buttonRow = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, Padding = new Padding(0, 10, 0, 0) };
        if (_offerNote)
        {
            var add = new PillButton { Text = "Add note: PBI incomplete", Style = PillStyle.Primary, Width = 210, Dock = DockStyle.Right };
            add.Click += (s, e) => { DialogResult = DialogResult.Yes; Close(); };
            buttonRow.Controls.Add(add);
        }
        buttonRow.Controls.Add(close);
        layout.Controls.Add(buttonRow, 0, 3);

        card.Controls.Add(layout);
        Controls.Add(card);

        CancelButton = close;
    }

    private static string Headline(TestRunResult result) => result.Outcome switch
    {
        TestRunOutcome.Passed => "Test passed",
        TestRunOutcome.Failed => "Test failed",
        TestRunOutcome.BuildFailed => "Generated code does not build",
        TestRunOutcome.NoTestsFound => "Nothing was run",
        TestRunOutcome.TimedOut => "Test run timed out",
        TestRunOutcome.Cancelled => "Cancelled",
        _ => "Could not run the test"
    };

    private static string Explanation(TestRunResult result, string? pbiLabel) => result.Outcome switch
    {
        TestRunOutcome.Failed =>
            $"{result.Summary} If it fails because {pbiLabel ?? "the PBI"} is not finished yet - the page does not have " +
            "what the test looks for - you can still insert it with a one-line comment saying so.",
        TestRunOutcome.BuildFailed =>
            "The new code has compile errors, so it cannot be verified. This is a problem with the generated code, " +
            "not with the PBI. Edit it or generate again.",
        _ => result.Summary
    };

    private static string Details(TestRunResult result)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var test in result.Tests)
        {
            sb.AppendLine($"{(test.Passed ? "PASS" : "FAIL")}  {test.Name}");
            if (!test.Passed && !string.IsNullOrWhiteSpace(test.Message))
            {
                foreach (var line in test.Message.ReplaceLineEndings("\n").Split('\n').Take(12))
                    sb.AppendLine($"        {line.TrimEnd()}");
            }
            sb.AppendLine();
        }

        foreach (var error in result.BuildErrors)
            sb.AppendLine(error);

        return sb.Length == 0 ? "(no further detail - see the Log tab)" : sb.ToString().TrimEnd();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        NativeDark.UseDarkTitleBar(this);
        AppIcon.ApplyTo(this);

        // A read-only TextBox that receives focus selects all of its text, which painted the whole
        // failure message as a selection.
        _details.Select(0, 0);
    }

    /// <summary>
    /// Shows the outcome. True only when the user chose to add the "PBI incomplete" note,
    /// which is only offered for failed tests.
    /// </summary>
    public static bool Show(IWin32Window owner, TestRunResult result, string? pbiLabel)
    {
        using var dialog = new RunResultDialog(result, pbiLabel);
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}
