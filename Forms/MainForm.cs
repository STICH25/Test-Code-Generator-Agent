using Microsoft.Web.WebView2.WinForms;
using PlaywrightAgentAI.Agents;
using PlaywrightAgentAI.Models;
using System.Diagnostics;
using PlaywrightAgentAI.Services;
using PlaywrightAgentAI.Tools;
using PlaywrightAgentAI.UI;

namespace PlaywrightAgentAI.Forms;

public partial class MainForm : Form
{
    private AppSettings _settings = null!;
    private SolutionProfile? _solutionProfile;
    private PreviewRecorder? _recorder;
    private List<RecordedAction> _recordedActions = [];
    private ExplorationAgent _agent = null!;

    private CancellationTokenSource? _cts;
    private bool _webViewReady;
    private string _lastPreviewedUrl = "";
    private Region? _webViewRegion;

    // Controls are held as fields rather than looked up by name, so a missing control is
    // a compile error instead of a null dereference inside a finally block.
    private FieldBox _urlField = null!;
    private FieldBox _objectiveField = null!;
    private TextBox _outputTextBox = null!;
    private TextBox _logTextBox = null!;
    private PillButton _generateButton = null!;
    private PillButton _cancelButton = null!;
    private PillButton _previewButton = null!;
    private PillButton _copyButton = null!;
    private PillButton _clearLogButton = null!;
    private PillButton _settingsButton = null!;
    private PillButton _saveToSolutionButton = null!;
    private PillButton _recordButton = null!;
    private DarkComboBox _featureBox = null!;
    private Label _featureHint = null!;
    private Label _statusLabel = null!;
    private Label _previewUrlLabel = null!;
    private ActivityBar _activityBar = null!;
    private SegmentedTabs _outputTabs = null!;
    private WebView2 _webView = null!;
    private Panel _webViewHost = null!;

    public MainForm()
    {
        InitializeComponent();
        BuildLayout();

        _settings = SettingsStore.Load();
        RebuildAgent();
    }

    // ---------------------------------------------------------------- setup

    /// <summary>
    /// Rebuilds the agent from current settings. Called at startup and whenever Settings
    /// is saved, so a newly entered key or model takes effect without a restart.
    /// </summary>
    private void RebuildAgent()
    {
        var generator = BuildGenerator();

        _solutionProfile = null;
        if (!string.IsNullOrWhiteSpace(_settings.TestSolutionPath))
        {
            try
            {
                _solutionProfile = SolutionScanner.Scan(_settings.TestSolutionPath);

                if (_solutionProfile.IsUsable)
                    Console.WriteLine($"Linked solution: {_solutionProfile.Describe()}");
                else
                    Console.Error.WriteLine($"Linked solution unusable: {string.Join(" ", _solutionProfile.Notes)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not scan the linked solution: {ex.Message}");
            }
        }

        _agent = new ExplorationAgent(generator, _solutionProfile);
        ReflectConnectionState();
        PopulateFeatureFiles();
        UpdateSaveButtonState();
    }

    private ITestCodeGenerator? BuildGenerator()
    {
        try
        {
            return _settings.Provider switch
            {
                ClaudeProvider.ClaudeCodeCli => new ClaudeCliCodeGenerator(_settings),
                ClaudeProvider.ApiKey when _settings.HasApiKey => new ClaudeCodeGenerator(_settings),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Claude generator unavailable: {ex.Message}");
            return null;
        }
    }

    private void ReflectConnectionState()
    {
        if (_settings.IsConfigured)
        {
            var via = _settings.Provider == ClaudeProvider.ClaudeCodeCli ? "Claude Code CLI" : "Anthropic API";
            SetStatus($"Ready - {via}, model {_settings.ActiveModel}.", Theme.TextSecondary);
            return;
        }

        SetStatus(
            _settings.Provider == ClaudeProvider.ClaudeCodeCli
                ? "Claude Code CLI not found. Open Settings, or install it with: npm install -g @anthropic-ai/claude-code"
                : "No Claude API key. Open Settings to connect your account.",
            Theme.Warning);
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_settings);

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _settings = dialog.Result;
        Console.WriteLine($"Settings saved. Provider: {_settings.Provider}, model: {_settings.ActiveModel}.");
        RebuildAgent();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        NativeDark.UseDarkTitleBar(this);
        AppIcon.ApplyTo(this);

        // The handle exists by now, so the log writer can safely marshal to the TextBox.
        Console.SetOut(new ControlLogWriter(_logTextBox));
        Console.SetError(new ControlLogWriter(_logTextBox, "ERROR: "));

        ReflectConnectionState();

        if (!_settings.IsConfigured)
            Console.Error.WriteLine("Claude is not configured yet. Open Settings to choose how to connect.");

        await EnsureWebViewReady();
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        BackColor = Theme.Page;
        ForeColor = Theme.TextPrimary;
        Padding = new Padding(Theme.Gutter);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Page
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));

        // Two draggable splitters: one between the working column and the preview, one
        // between the input card and the output card. Every pane is resizable.
        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = Theme.Gutter,
            BackColor = Theme.Page
        };

        var leftSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = Theme.Gutter,
            BackColor = Theme.Page
        };

        leftSplit.Panel1.Controls.Add(BuildInputCard());
        leftSplit.Panel2.Controls.Add(BuildOutputCard());

        mainSplit.Panel1.Controls.Add(leftSplit);
        mainSplit.Panel2.Controls.Add(BuildPreviewCard());

        root.Controls.Add(mainSplit, 0, 0);
        root.Controls.Add(BuildStatusBar(), 0, 1);

        Controls.Add(root);

        // Panel minimums and SplitterDistance are validated against the container's
        // current size, and a SplitContainer starts at its default 150px. Setting them
        // before it is docked and parented throws InvalidOperationException.
        ConfigureSplit(mainSplit, minPanel1: 420, minPanel2: 320, desired: 660);
        // The input card's fixed rows now total 292px (the feature picker added three)
        // plus 36px of card padding, so it needs ~420 before the objective field gets a
        // usable height rather than a sliver.
        ConfigureSplit(leftSplit, minPanel1: 420, minPanel2: 170, desired: 500);
    }

    /// <summary>
    /// Sets a splitter's minimums and position, and keeps them valid as the window resizes.
    ///
    /// This has to be re-evaluated on every resize, not just once. When a SplitContainer's
    /// Panel1MinSize + Panel2MinSize + SplitterWidth exceeds its own extent, it abandons the
    /// layout pass entirely and leaves both SplitterPanels at their previous bounds - which
    /// for a nested splitter means the child panels keep a stale *width* as well, and the
    /// cards inside them stop reflowing.
    /// </summary>
    private static void ConfigureSplit(SplitContainer split, int minPanel1, int minPanel2, int desired)
    {
        void Apply()
        {
            var extent = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            var available = extent - split.SplitterWidth;
            if (available <= 40)
                return;

            var min1 = minPanel1;
            var min2 = minPanel2;

            // Scale the minimums down proportionally rather than letting the pair exceed
            // what is actually on screen.
            if (min1 + min2 > available)
            {
                var scale = available / (double)(min1 + min2);
                min1 = Math.Max(20, (int)(min1 * scale));
                min2 = Math.Max(20, (int)(min2 * scale));
            }

            // Relax both minimums before moving the splitter: each setter validates against
            // the currently stored values, so tightening first can throw.
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            split.SplitterDistance = Math.Clamp(split.SplitterDistance, min1, Math.Max(min1, available - min2));
            split.Panel1MinSize = min1;
            split.Panel2MinSize = min2;
        }

        split.SplitterDistance = Math.Max(1, desired);
        Apply();
        split.SizeChanged += (_, _) => Apply();
    }

    private Control BuildInputCard()
    {
        var card = new Card { Dock = DockStyle.Fill };

        var layout = NewCardLayout(rows: 6);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // 0 title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // 1 url caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // 2 url row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));   // 3 feature caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // 4 feature picker
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // 5 scenario hint
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // 6 objective caption
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 7 objective field
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));   // 8 actions

        _settingsButton = new PillButton { Text = "Settings", Style = PillStyle.Outline, Width = 96, Height = 30, Dock = DockStyle.Right };
        _settingsButton.Click += (s, e) => OpenSettings();

        var titleRow = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt };
        titleRow.Controls.Add(Title("Generate Test"));
        titleRow.Controls.Add(_settingsButton);
        layout.Controls.Add(titleRow, 0, 0);
        layout.Controls.Add(Caption("URL"), 0, 1);

        _urlField = new FieldBox { Dock = DockStyle.Fill, PlaceholderText = "https://example.com" };
        _urlField.Inner.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await LoadPreview();
            }
        };

        _previewButton = new PillButton { Text = "Preview", Style = PillStyle.Outline, Width = 104, Dock = DockStyle.Fill };
        _previewButton.Click += async (s, e) => await LoadPreview();

        layout.Controls.Add(Row(_urlField, _previewButton, 104), 0, 2);
        _featureBox = new DarkComboBox { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4) };
        _featureBox.SelectedIndexChanged += (s, e) => ShowFeatureScenarios();

        _featureHint = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.Ui(8.5f),
            ForeColor = Theme.TextDisabled,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.TopLeft,
            Text = "Link a solution in Settings to target a feature file."
        };

        layout.Controls.Add(Caption("Feature file"), 0, 3);
        layout.Controls.Add(_featureBox, 0, 4);
        layout.Controls.Add(_featureHint, 0, 5);
        layout.Controls.Add(Caption("Test Objective"), 0, 6);

        _objectiveField = new FieldBox(multiline: true)
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "e.g. Verify the Skills section lists every skill item and each one is visible."
        };
        layout.Controls.Add(_objectiveField, 0, 7);

        _generateButton = new PillButton { Text = "Generate Test", Style = PillStyle.Primary, Hero = true, Dock = DockStyle.Fill };
        _generateButton.Click += async (s, e) => await GenerateTest();

        _cancelButton = new PillButton { Text = "Cancel", Style = PillStyle.Outline, Width = 104, Dock = DockStyle.Fill, Enabled = false };
        _cancelButton.Click += (s, e) =>
        {
            _cancelButton.Enabled = false;
            SetStatus("Cancelling...", Theme.TextSecondary);
            _cts?.Cancel();
        };

        _recordButton = new PillButton { Text = "Record", Style = PillStyle.Outline, Width = 104, Dock = DockStyle.Fill };
        _recordButton.Click += async (s, e) => await ToggleRecording();

        var actionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt,
            Margin = new Padding(0, 10, 0, 0)
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
        _generateButton.Margin = new Padding(0, 0, 10, 0);
        _cancelButton.Margin = new Padding(0, 0, 10, 0);
        _recordButton.Margin = Padding.Empty;
        actionRow.Controls.Add(_generateButton, 0, 0);
        actionRow.Controls.Add(_cancelButton, 1, 0);
        actionRow.Controls.Add(_recordButton, 2, 0);
        layout.Controls.Add(actionRow, 0, 8);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildOutputCard()
    {
        var card = new Card { Dock = DockStyle.Fill };

        var layout = NewCardLayout(rows: 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _outputTabs = new SegmentedTabs();
        _outputTabs.SetItems("Code", "Log");
        _outputTabs.SelectedIndexChanged += (s, e) => ShowOutputTab(_outputTabs.SelectedIndex);

        _copyButton = new PillButton { Text = "Copy", Style = PillStyle.Outline, Height = 32, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        _copyButton.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_outputTextBox.Text))
            {
                SetStatus("Nothing to copy - generate a test first.", Theme.Warning);
                return;
            }

            Clipboard.SetText(_outputTextBox.Text);
            SetStatus("Code copied to clipboard.", Theme.Accent);
        };

        _clearLogButton = new PillButton { Text = "Clear", Style = PillStyle.Ghost, Height = 32, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        _clearLogButton.Click += (s, e) => _logTextBox.Clear();

        _saveToSolutionButton = new PillButton { Text = "Open Folder", Style = PillStyle.Outline, Height = 32, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), Enabled = false };
        _saveToSolutionButton.Click += (s, e) => OpenSolutionFolder();

        // A TableLayoutPanel rather than docked buttons: with Dock=Right the buttons were
        // clipped out of existence as the card narrowed (Clear vanished entirely at the
        // minimum window size). Here the tab strip absorbs the shrinking instead, and every
        // button stays reachable.
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt,
            Margin = new Padding(0, 0, 0, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        _outputTabs.Dock = DockStyle.Fill;
        _outputTabs.Margin = Padding.Empty;

        header.Controls.Add(_outputTabs, 0, 0);
        header.Controls.Add(_clearLogButton, 1, 0);
        header.Controls.Add(_copyButton, 2, 0);
        header.Controls.Add(_saveToSolutionButton, 3, 0);
        layout.Controls.Add(header, 0, 0);

        // Code does not wrap - wrapping changes how it reads. Log lines do, because they
        // are prose and would otherwise need horizontal scrolling.
        _outputTextBox = NewConsoleBox(wrap: false);
        _logTextBox = NewConsoleBox(wrap: true);
        _logTextBox.Visible = false;

        var content = new Card
        {
            Dock = DockStyle.Fill,
            Fill = Theme.Surface,
            Radius = 8,
            BackColor = Theme.SurfaceAlt,
            Padding = new Padding(12, 10, 6, 10)
        };
        content.Controls.Add(_outputTextBox);
        content.Controls.Add(_logTextBox);
        layout.Controls.Add(content, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildPreviewCard()
    {
        var card = new Card { Dock = DockStyle.Fill };

        var layout = NewCardLayout(rows: 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _previewUrlLabel = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Ui(9f),
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true,
            Text = "no page loaded"
        };

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, Margin = new Padding(0, 0, 0, 8) };
        header.Controls.Add(_previewUrlLabel);
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Left,
            Width = 130,
            Font = Theme.Display(14f),
            ForeColor = Theme.TextPrimary,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Preview"
        });
        layout.Controls.Add(header, 0, 0);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Theme.Surface
        };

        _webViewHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        _webViewHost.Controls.Add(_webView);
        // WebView2 hosts a native child window that ignores managed painting, so the
        // rounded corner has to be applied as an HWND region.
        _webViewHost.Resize += (s, e) => ClipWebViewCorners();
        layout.Controls.Add(_webViewHost, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildStatusBar()
    {
        var card = new Card
        {
            Dock = DockStyle.Fill,
            Fill = Theme.Surface,
            Margin = new Padding(0, Theme.Gutter, 0, 0),
            Padding = new Padding(20, 14, 20, 14)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.Ui(9.5f),
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Surface,
            Text = "Ready."
        };
        layout.Controls.Add(_statusLabel, 0, 0);

        _activityBar = new ActivityBar { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = new Padding(0, 3, 0, 0) };
        layout.Controls.Add(_activityBar, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    // ------------------------------------------------------------ layout helpers

    private static TableLayoutPanel NewCardLayout(int rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows,
            BackColor = Theme.SurfaceAlt
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    /// <summary>A stretching primary control with a fixed-width companion on its right.</summary>
    private static TableLayoutPanel Row(Control fill, Control trailing, int trailingWidth, Padding? margin = null)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt,
            Margin = margin ?? Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, trailingWidth + 10));

        fill.Margin = new Padding(0, 0, 10, 0);
        trailing.Margin = Padding.Empty;

        row.Controls.Add(fill, 0, 0);
        row.Controls.Add(trailing, 1, 0);
        return row;
    }

    private static Label Title(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        Font = Theme.Display(14f),
        ForeColor = Theme.TextPrimary,
        BackColor = Theme.SurfaceAlt,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label Caption(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text.ToUpperInvariant(),
        Font = Theme.Ui(8f, FontStyle.Bold),
        ForeColor = Theme.TextSecondary,
        BackColor = Theme.SurfaceAlt,
        TextAlign = ContentAlignment.BottomLeft,
        Padding = new Padding(2, 0, 0, 4)
    };

    private static TextBox NewConsoleBox(bool wrap)
    {
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = wrap,
            ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Mono(9.5f)
        };

        NativeDark.UseDarkScrollBars(box);
        return box;
    }

    private void ShowOutputTab(int index)
    {
        _outputTextBox.Visible = index == 0;
        _logTextBox.Visible = index == 1;
    }

    private void ClipWebViewCorners()
    {
        if (_webViewHost.Width <= 0 || _webViewHost.Height <= 0)
            return;

        var previous = _webViewRegion;

        using var path = Theme.RoundedRect(new Rectangle(0, 0, _webViewHost.Width, _webViewHost.Height), 10);
        _webViewRegion = new Region(path);
        _webViewHost.Region = _webViewRegion;

        previous?.Dispose();
    }

    // ---------------------------------------------------------------- preview

    private async Task<bool> EnsureWebViewReady()
    {
        if (_webViewReady)
            return true;

        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webViewReady = _webView.CoreWebView2 != null;

            // DefaultBackgroundColor only takes effect once a document exists; with no
            // navigation at all the control paints solid black.
            if (_webViewReady)
                _webView.CoreWebView2!.Navigate("about:blank");

            return _webViewReady;
        }
        catch (Exception ex)
        {
            // A missing WebView2 Evergreen runtime should degrade the preview, not the app.
            Console.Error.WriteLine($"WebView2 could not start: {ex.Message}");
            SetStatus("Web preview unavailable (WebView2 runtime not found).", Theme.Danger);
            return false;
        }
    }

    private async Task LoadPreview()
    {
        var url = NormalizeUrl(_urlField.Text);
        if (url == null)
        {
            SetStatus("Enter a valid absolute URL, e.g. https://example.com", Theme.Warning);
            return;
        }

        if (!await EnsureWebViewReady())
            return;

        try
        {
            _webView.CoreWebView2!.Navigate(url);
            _lastPreviewedUrl = url;
            _previewUrlLabel.Text = url;
            SetStatus($"Preview loading {url}", Theme.TextSecondary);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Preview navigation failed: {ex.Message}");
            SetStatus($"Preview failed: {ex.Message}", Theme.Danger);
        }
    }

    private static string? NormalizeUrl(string raw)
    {
        var text = raw.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Accept "example.com" the way a browser address bar would.
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
    }

    // ---------------------------------------------------------------- run

    private async Task GenerateTest()
    {
        var url = NormalizeUrl(_urlField.Text);
        if (url == null)
        {
            SetStatus("Enter a valid absolute URL, e.g. https://example.com", Theme.Warning);
            _urlField.Inner.Focus();
            return;
        }

        var objective = _objectiveField.Text.Trim();
        if (string.IsNullOrWhiteSpace(objective) && _recordedActions.Count == 0)
        {
            SetStatus("Enter a test objective, or record the steps you want turned into a test.", Theme.Warning);
            _objectiveField.Inner.Focus();
            return;
        }

        _urlField.Text = url;

        // Show the page immediately rather than only after a successful run, so a
        // failed generation still leaves something to look at.
        if (!string.Equals(url, _lastPreviewedUrl, StringComparison.OrdinalIgnoreCase))
            await LoadPreview();

        SetRunning(true);
        _outputTabs.SelectedIndex = 1;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            SetStatus("Generating test...", Theme.Accent);

            var request = new ExplorationRequest
            {
                Url = url,
                TestObjective = objective,
                RecordedActions = _recordedActions,
                TargetFeaturePath = SelectedFeature?.Path
            };
            var result = await _agent.Run(request, _cts.Token);

            // A multi-line TextBox only breaks on CRLF. The template literals in
            // TestCodeBuilder come from LF-only source files and model output is LF too,
            // so without this the whole test collapses onto a single line.
            _outputTextBox.Text = result.GeneratedCode.ReplaceLineEndings();

            foreach (var warning in result.Warnings)
                Console.Error.WriteLine(warning);

            UpdateSaveButtonState();

            // The old code reported success even when the AI had failed and the static
            // stub was returned. Report what actually happened.
            if (result.UsedAi)
            {
                _outputTabs.SelectedIndex = 0;
                SetStatus("Test generated.", Theme.Accent);
                AutoSave(result);
            }
            else
            {
                SetStatus($"Static template only - {result.Warnings.FirstOrDefault() ?? "AI was not used."}", Theme.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.", Theme.TextSecondary);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            SetStatus($"Failed: {ex.Message}", Theme.Danger);
            _outputTabs.SelectedIndex = 1;
        }
        finally
        {
            SetRunning(false);
        }
    }

    private void SetRunning(bool running)
    {
        _generateButton.Enabled = !running;
        _generateButton.Text = running ? "Generating..." : "Generate Test";
        _cancelButton.Enabled = running;
        _previewButton.Enabled = !running;
        _activityBar.Running = running;
        UseWaitCursor = running;
    }

    /// <summary>
    /// Record opens a real Playwright browser; Done closes it and immediately generates a
    /// test from what was captured, which is the flow the user asked for.
    /// </summary>
    private async Task ToggleRecording()
    {
        if (_recorder is { IsRecording: true })
        {
            await FinishRecording();
            return;
        }

        var url = NormalizeUrl(_urlField.Text);
        if (url == null)
        {
            SetStatus("Enter a valid absolute URL to record against.", Theme.Warning);
            _urlField.Inner.Focus();
            return;
        }

        try
        {
            if (!await EnsureWebViewReady())
            {
                SetStatus("The preview must be available to record.", Theme.Danger);
                return;
            }

            _recordedActions = [];
            _recorder = new PreviewRecorder(_webView);
            _recorder.ActionRecorded += OnActionRecorded;

            _outputTabs.SelectedIndex = 1;
            SetRecordingUi(true);

            await _recorder.StartAsync(url);
            _lastPreviewedUrl = url;
            _previewUrlLabel.Text = url;
            SetStatus("Recording in the preview - perform your steps, then press Done.", Theme.Accent);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not start recording: {ex.Message}");
            SetStatus($"Recording failed to start: {ex.Message}", Theme.Danger);
            await DisposeRecorder();
            SetRecordingUi(false);
        }
    }

    private async Task FinishRecording()
    {
        if (_recorder == null)
            return;

        try
        {
            _recordedActions = [.. await _recorder.StopAsync()];
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Problem stopping the recorder: {ex.Message}");
        }
        finally
        {
            await DisposeRecorder();
            SetRecordingUi(false);
        }

        if (_recordedActions.Count == 0)
        {
            SetStatus("No steps were recorded.", Theme.Warning);
            return;
        }

        SetStatus($"Captured {_recordedActions.Count} step(s). Generating the test...", Theme.Accent);
        await GenerateTest();
    }

    private void OnActionRecorded(RecordedAction action)
    {
        if (IsDisposed)
            return;

        // Raised from a Playwright callback thread.
        BeginInvoke(() =>
        {
            var count = _recorder?.Actions.Count ?? 0;
            SetStatus($"Recording - {count} step(s) captured. Press Done when finished.", Theme.Accent);
        });
    }

    private async Task DisposeRecorder()
    {
        if (_recorder == null)
            return;

        _recorder.ActionRecorded -= OnActionRecorded;

        await _recorder.DisposeAsync();
        _recorder = null;
    }

    private void SetRecordingUi(bool recording)
    {
        _recordButton.Text = recording ? "Done" : "Record";
        _recordButton.Style = recording ? PillStyle.Primary : PillStyle.Outline;
        _generateButton.Enabled = !recording;
        _previewButton.Enabled = !recording;
    }

    private sealed record FeatureChoice(FeatureFile? File, string Label)
    {
        public override string ToString() => Label;
    }

    private void PopulateFeatureFiles()
    {
        _featureBox.Items.Clear();

        if (_solutionProfile is not { CanWriteGherkin: true })
        {
            _featureBox.Items.Add(new FeatureChoice(null, "No Gherkin solution linked"));
            _featureBox.SelectedIndex = 0;
            _featureBox.Enabled = false;
            _featureHint.Text = "Link a Reqnroll solution in Settings to target a feature file.";
            return;
        }

        _featureBox.Enabled = true;
        _featureBox.Items.Add(new FeatureChoice(null, "New feature file"));

        foreach (var feature in _solutionProfile.Features)
            _featureBox.Items.Add(new FeatureChoice(feature, feature.ToString()));

        _featureBox.SelectedIndex = 0;
        ShowFeatureScenarios();
    }

    /// <summary>
    /// Shows what is already in the selected feature, so the user can see which scenarios
    /// exist before adding another - and so it is obvious the new one will be appended
    /// rather than replacing them.
    /// </summary>
    private void ShowFeatureScenarios()
    {
        if (_featureBox.SelectedItem is not FeatureChoice choice)
            return;

        if (choice.File == null)
        {
            _featureHint.Text = _solutionProfile is { CanWriteGherkin: true }
                ? "A new .feature file will be created and named after the behaviour."
                : "Link a Reqnroll solution in Settings to target a feature file.";
            return;
        }

        var scenarios = choice.File.Scenarios;
        _featureHint.Text = scenarios.Count == 0
            ? $"{choice.File.FileName} has no scenarios yet."
            : $"Appending to {scenarios.Count} existing scenario(s): {string.Join("; ", scenarios.Take(3))}"
              + (scenarios.Count > 3 ? " ..." : string.Empty);
    }

    private FeatureFile? SelectedFeature =>
        (_featureBox.SelectedItem as FeatureChoice)?.File;

    /// <summary>
    /// Writes everything the run produced straight into the solution.
    ///
    /// Auto-saving is what the user asked for, so the guard rails moved into
    /// SolutionWriter: it backs up any file it is about to overwrite, which matters
    /// because appending a scenario rewrites the whole feature file.
    /// </summary>
    private void AutoSave(AgentResult result)
    {
        if (_solutionProfile is not { IsUsable: true } || result.Artifacts.Count == 0)
            return;

        var written = SolutionWriter.Write(_solutionProfile, result.Artifacts);

        if (written.Count == 0)
        {
            SetStatus("Generated, but nothing could be written to the solution. See Log.", Theme.Danger);
            return;
        }

        var names = string.Join(", ", written.Select(a => Path.GetFileName(a.WrittenPath!)));
        SetStatus($"Saved to solution: {names}", Theme.Accent);

        // Picking up a newly created feature file requires a rescan.
        RescanSolution();
    }

    private void RescanSolution()
    {
        if (string.IsNullOrWhiteSpace(_settings.TestSolutionPath))
            return;

        try
        {
            var selected = SelectedFeature?.Path;
            _solutionProfile = SolutionScanner.Scan(_settings.TestSolutionPath);
            _agent = new ExplorationAgent(BuildGenerator(), _solutionProfile);

            PopulateFeatureFiles();
            RestoreFeatureSelection(selected);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not rescan the solution: {ex.Message}");
        }
    }

    private void RestoreFeatureSelection(string? path)
    {
        if (path == null)
            return;

        for (var i = 0; i < _featureBox.Items.Count; i++)
        {
            if (_featureBox.Items[i] is FeatureChoice { File: not null } choice &&
                string.Equals(choice.File.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                _featureBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void UpdateSaveButtonState()
    {
        _saveToSolutionButton.Enabled = _solutionProfile is { IsUsable: true };
    }

    /// <summary>
    /// Files are written automatically now, so this just reveals where they landed.
    /// </summary>
    private void OpenSolutionFolder()
    {
        var folder = _solutionProfile switch
        {
            { CanWriteGherkin: true } p => p.FeaturesDirectory,
            { IsUsable: true } p => p.TestDirectory,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            SetStatus("No solution folder to open. Link one in Settings.", Theme.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not open {folder}: {ex.Message}");
            SetStatus($"Could not open the folder: {ex.Message}", Theme.Danger);
        }
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }
}
