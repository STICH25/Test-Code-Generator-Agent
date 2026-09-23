using System.Text;

namespace PlaywrightAgentAI.Forms;

/// <summary>
/// Funnels Console output into a TextBox on the UI thread.
///
/// The pipeline services (DomExplorer, HtmlAnalyzer, ExplorationAgent) report their
/// progress through Console.WriteLine / Console.Error. This is a WinExe, so no console
/// is attached and all of that output was previously discarded. Redirecting the streams
/// here surfaces it without having to thread a logger through every service.
/// </summary>
internal sealed class ControlLogWriter : TextWriter
{
    private readonly TextBox _target;
    private readonly string _prefix;
    private readonly StringBuilder _pending = new();
    private readonly Lock _gate = new();

    public ControlLogWriter(TextBox target, string prefix = "")
    {
        _target = target;
        _prefix = prefix;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        string? line = null;

        lock (_gate)
        {
            if (value == '\n')
            {
                line = _pending.ToString();
                _pending.Clear();
            }
            else if (value != '\r')
            {
                _pending.Append(value);
            }
        }

        if (line != null)
            Emit(line);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        foreach (var c in value)
            Write(c);
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        Write('\n');
    }

    private void Emit(string text)
    {
        // The handle must exist before AppendText, otherwise WinForms would create it
        // on whichever thread happened to log first.
        if (_target.IsDisposed || !_target.IsHandleCreated)
            return;

        var stamped = $"[{DateTime.Now:HH:mm:ss}] {_prefix}{text}{Environment.NewLine}";

        try
        {
            if (_target.InvokeRequired)
                _target.BeginInvoke(() => Append(stamped));
            else
                Append(stamped);
        }
        catch (ObjectDisposedException)
        {
            // Form closed mid-run; nothing left to write to.
        }
        catch (InvalidOperationException)
        {
            // Handle destroyed between the check above and the invoke.
        }
    }

    private void Append(string text)
    {
        if (_target.IsDisposed)
            return;

        _target.AppendText(text);
    }
}
