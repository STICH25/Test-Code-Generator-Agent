using PlaywrightAgentAI.Forms;

namespace PlaywrightAgentAI;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}