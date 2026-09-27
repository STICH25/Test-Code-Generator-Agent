using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PlaywrightAgentAI.Models;
using PlaywrightAgentAI.Services;

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
    private readonly List<ScreenshotEntry> _screenshots = [];
    private readonly Lock _gate = new();

    private CoreWebView2? _core;
    private string? _scriptId;
    private string _lastUrl = "";
    private string? _screenshotsFolder;
    private string _initialDescription = "";
    private int _screenshotIndex;
    private bool _capturedInitialLoad;

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

    /// <summary>Debug screenshots captured so far, in capture order. Empty when no folder is configured.</summary>
    public IReadOnlyList<ScreenshotEntry> Screenshots
    {
        get
        {
            lock (_gate)
                return _screenshots.ToList();
        }
    }

    /// <summary>
    /// Starts recording. <paramref name="screenshotsFolder"/> is optional - when it is null
    /// or blank, actions are still recorded but nothing is captured to disk.
    /// </summary>
    public async Task StartAsync(string url, string? screenshotsFolder = null)
    {
        if (IsRecording)
            throw new InvalidOperationException("Already recording.");

        _core = _webView.CoreWebView2
                ?? throw new InvalidOperationException("The preview is not ready yet.");

        lock (_gate)
        {
            _actions.Clear();
            _screenshots.Clear();
        }

        _screenshotsFolder = screenshotsFolder;
        _screenshotIndex = 0;
        _capturedInitialLoad = false;

        // A stale screenshot from a previous, never-cleared session must not leak into this
        // one - the viewer has no way to tell "left over" from "just captured" apart.
        ScreenshotCapture.ClearFolder(_screenshotsFolder);

        _core.WebMessageReceived += OnWebMessage;
        _core.NavigationCompleted += OnNavigationCompleted;

        // Registered for documents created from now on, so it survives every navigation
        // the user makes while recording.
        _scriptId = await _core.AddScriptToExecuteOnDocumentCreatedAsync(
            RecorderScript.For(RecorderScript.WebViewTransport));

        IsRecording = true;

        var initial = new RecordedAction { Kind = "navigate", Url = url };
        _initialDescription = initial.Describe();
        // capture: false - the page has not navigated yet at this point, so there is
        // nothing worth a screenshot until OnNavigationCompleted fires below.
        Add(initial, capture: false);
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
        if (string.IsNullOrWhiteSpace(url) || url == "about:blank")
            return;

        if (url != _lastUrl)
        {
            _lastUrl = url;
            Add(new RecordedAction { Kind = "navigate", Url = url });
            return;
        }

        // The very first navigation (StartAsync's Navigate call) lands here too, since
        // _lastUrl was already set before it started - that is the one time a screenshot is
        // still owed: the initial "navigate" action was recorded with capture:false because
        // the page had not loaded yet, and now it has.
        if (!_capturedInitialLoad)
        {
            _capturedInitialLoad = true;
            _ = CaptureAsync(_initialDescription);
        }
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

    private void Add(RecordedAction action, bool capture = true)
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

        if (capture)
            _ = CaptureAsync(action.Describe());
    }

    /// <summary>
    /// Captures the preview's current visual via WebView2's own CapturePreviewAsync - the
    /// built-in, purpose-made API for this, rather than a GDI screen-scrape of the app
    /// window (which would need to fight z-order and DPI, and would capture the app chrome
    /// along with the page). Fire-and-forget from the caller's point of view: a screenshot
    /// failing must never break recording itself.
    /// </summary>
    private async Task CaptureAsync(string description)
    {
        if (string.IsNullOrWhiteSpace(_screenshotsFolder) || _core == null)
            return;

        int index;
        lock (_gate)
            index = ++_screenshotIndex;

        var path = Path.Combine(_screenshotsFolder, $"{index:000}.png");

        try
        {
            Directory.CreateDirectory(_screenshotsFolder);

            await using (var stream = File.Create(path))
                await _core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);

            var entry = new ScreenshotEntry { Index = index, FilePath = path, Description = description };
            lock (_gate)
                _screenshots.Add(entry);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not capture a screenshot: {ex.Message}");
        }
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
