using PlaywrightAgentAI.Forms;
using PlaywrightAgentAI.UI;

namespace PlaywrightAgentAI;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Must run before any window exists so title bars and scrollbars come up dark.
        NativeDark.EnableForProcess();

        Application.Run(new MainForm());
    }
}