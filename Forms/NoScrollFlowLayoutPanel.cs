using System.Windows.Forms;
namespace PlaywrightAgentAI.Forms;

public class NoScrollFlowLayoutPanel : FlowLayoutPanel
{
    protected override void WndProc(ref Message m)
    {
        // Block scrollbar painting
        const int WM_NCPAINT = 0x85;
        const int WM_ERASEBKGND = 0x14;

        if (m.Msg == WM_NCPAINT || m.Msg == WM_ERASEBKGND)
            return;

        base.WndProc(ref m);
    }

    public NoScrollFlowLayoutPanel()
    {
        AutoScroll = true;

        VerticalScroll.Visible = false;
        HorizontalScroll.Visible = false;

        VerticalScroll.Maximum = 0;
        HorizontalScroll.Maximum = 0;
    }
}
