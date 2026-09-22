namespace PlaywrightAgentAI.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _cts?.Dispose();
            _webViewRegion?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        SuspendLayout();

        // AutoScaleDimensions must accompany AutoScaleMode.Font, otherwise the form is
        // rescaled against an unknown baseline on high-DPI displays.
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Font = UI.Theme.Ui(9F);

        ClientSize = new Size(1400, 940);
        MinimumSize = new Size(940, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UI.Theme.Page;
        ForeColor = UI.Theme.TextPrimary;
        Text = "Playwright Test Generator";
        Name = "MainForm";

        ResumeLayout(false);
    }
}
