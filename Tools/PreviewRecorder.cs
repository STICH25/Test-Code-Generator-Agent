using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Tools;

/// <summary>
/// Records interactions in the embedded WebView2 preview.
///
/// This is not a compromise version of the Playwright recorder: it injects the same
/// RecorderScript and produces the same locators. The earlier argument for a separate
/// Playwright window - "selectors come from the engine that replays them" - did not
/// really hold, because the locators are produced by the injected script in both cases,
/// not by Playwright's own selector engine.
///
/// Recording here also means the user records against the page they are already looking
/// at, including anything they logged into in the preview.
/// </summary>
public class PreviewRecorder : IAsyncDisposable
{
    private readonly WebView2 _webView;
    private readonly List<RecordedAction> _actions = [];
    private readonly Lock _gate = new();

    private CoreWebView2? _core;
    private string? _scriptId;
    private string _lastUrl = "";

    public PreviewRecorder(WebView2 webView)
    {
        _webView = webView;
    }

    public bool IsRecording { get; private set; }

    public event Action<RecordedAction>? ActionRecorded;

    public IReadOnlyList<RecordedAction> Actions
    {
        get
        {
            lock (_gate)
                return _actions.ToList();
        }
    }

    public async Task StartAsync(string url)
    {
        if (IsRecording)
            throw new InvalidOperationException("Already recording.");

        _core = _webView.CoreWebView2
                ?? throw new InvalidOperationException("The preview is not ready yet.");

        lock (_gate)
            _actions.Clear();

        _core.WebMessageReceived += OnWebMessage;
        _core.NavigationCompleted += OnNavigationCompleted;

        // Registered for documents created from now on, so it survives every navigation
        // the user makes while recording.
        _scriptId = await _core.AddScriptToExecuteOnDocumentCreatedAsync(
            RecorderScript.For(RecorderScript.WebViewTransport));

        IsRecording = true;

        Add(new RecordedAction { Kind = "navigate", Url = url });
        _lastUrl = url;

        // Reload so the init script applies to the page already on screen.
        _core.Navigate(url);

        Console.WriteLine("Recording in the preview. Perform your steps, then press Done.");
    }

    public async Task<IReadOnlyList<RecordedAction>> StopAsync()
    {
        IsRecording = false;
        await DetachAsync();

        var captured = Actions;
        Console.WriteLine($"Recording stopped. {captured.Count} step(s) captured.");
        return captured;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!IsRecording || _core == null)
            return;

        var url = _core.Source;
        if (string.IsNullOrWhiteSpace(url) || url == "about:blank" || url == _lastUrl)
            return;

        _lastUrl = url;
        Add(new RecordedAction { Kind = "navigate", Url = url });
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsRecording)
            return;

        try
        {
            // postMessage with a string arrives as a JSON-encoded string, so it has to be
            // unwrapped once before it is the action JSON.
            var payload = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(payload))
                return;

            var action = JsonSerializer.Deserialize<RecordedAction>(payload);
            if (action != null)
                Add(action);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read a recorded action: {ex.Message}");
        }
    }

    private void Add(RecordedAction action)
    {
        lock (_gate)
        {
            if (action.Kind == "fill" && _actions.Count > 0)
            {
                var previous = _actions[^1];
                if (previous.Kind == "fill" && previous.Selector == action.Selector)
                {
                    previous.Value = action.Value;
                    return;
                }
            }

            if (action.Kind == "navigate" && _actions.Count > 0 && _actions[^1].Kind == "navigate")
            {
                _actions[^1] = action;
                return;
            }

            _actions.Add(action);
        }

        Console.WriteLine($"  recorded: {action.Describe()}");
        ActionRecorded?.Invoke(action);
    }

    private async Task DetachAsync()
    {
        if (_core == null)
            return;

        _core.WebMessageReceived -= OnWebMessage;
        _core.NavigationCompleted -= OnNavigationCompleted;

        if (_scriptId != null)
        {
            try
            {
                // Removal is synchronous in this SDK; only the Add counterpart is async.
                // It stops future documents getting the script - the page currently loaded
                // keeps its listeners, which is harmless now the handler is unsubscribed.
                _core.RemoveScriptToExecuteOnDocumentCreated(_scriptId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not remove the recorder script: {ex.Message}");
            }

            _scriptId = null;
        }

        _core = null;
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (IsRecording)
            await StopAsync();
        else
            await DetachAsync();

        GC.SuppressFinalize(this);
    }
}
