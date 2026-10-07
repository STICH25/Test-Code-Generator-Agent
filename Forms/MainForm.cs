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

    /// <summary>
    /// Debug screenshots from the last recording, in capture order. Cleared - files and all,
    /// via <see cref="ClearScreenshots"/> - whenever a new recording starts, Clear is
    /// pressed, or Insert succeeds, so this list never outlives the run it belongs to.
    /// </summary>
    private List<ScreenshotEntry> _recordedScreenshots = [];
    private ExplorationAgent _agent = null!;

    /// <summary>
    /// The files the last run produced (or the static-template fallback, wrapped the same
    /// way). This is the single source of truth for what the three code tabs show, what
    /// Edit modifies, and what Insert writes - editing a tab's TextBox directly would leave
    /// the artifact's own Content stale and Insert would write the old version.
    /// </summary>
    private List<GeneratedArtifact> _lastArtifacts = [];

    private CancellationTokenSource? _cts;

    // The Claude connection gate (see SetConnectionLocked). _verifyVersion lets a newer check, or
    // a pass reported by the Settings dialog, supersede one still in flight.
    private bool _connectionVerified;
    private bool _verifying;
    private int _verifyVersion;
    private CancellationTokenSource? _verifyCts;

    // Tracked here rather than read back from the buttons, because a locked button reports
    // itself disabled and would look "busy" when it is only waiting for the connection check.
    private bool _running;
    private bool _recordingUi;
    private bool IsBusy => _running || _recordingUi;

    private bool _webViewReady;
    private string _lastPreviewedUrl = "";
    private Region? _webViewRegion;

    // Controls are held as fields rather than looked up by name, so a missing control is
    // a compile error instead of a null dereference inside a finally block.
    private FieldBox _urlField = null!;
    private FieldBox _objectiveField = null!;
    private TextBox _featureTextBox = null!;
    private TextBox _pageObjectTextBox = null!;
    private TextBox _stepsTextBox = null!;
    private TextBox _logTextBox = null!;
    private PillButton _generateButton = null!;
    private PillButton _cancelButton = null!;
    private PillButton _previewButton = null!;
    private PillButton _refreshButton = null!;
    private PillButton _editButton = null!;
    private PillButton _copyButton = null!;
    private PillButton _clearButton = null!;
    private PillButton _settingsButton = null!;
    private PillButton _openFolderButton = null!;
    private PillButton _insertButton = null!;
    private PillButton _recordButton = null!;
    private PillButton _screenshotsButton = null!;
    private DarkComboBox _featureBox = null!;
    private Label _featureHint = null!;
    private DarkComboBox _pageObjectBox = null!;
    private Label _pageObjectHint = null!;
    private DarkComboBox _stepFileBox = null!;
    private Label _stepFileHint = null!;
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

        // Locked from the first frame, so nothing is clickable in the moment before the
        // connection check starts.
        SetConnectionLocked(true);

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

        _agent = NewAgent(generator);
        ReflectConnectionState();
        PopulateTargetPickers();
        UpdateInsertButtonState();
    }

    /// <summary>
    /// SpecForge is looked up afresh each time the agent is rebuilt, so installing it (or
    /// pointing Settings at it) takes effect without a restart. It is optional throughout.
    /// </summary>
    private ExplorationAgent NewAgent(ITestCodeGenerator? generator) =>
        new(generator, _solutionProfile, SpecForgeLocator.Locate(_settings.SpecForgePath));

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
            SetStatus(
                _connectionVerified
                    ? $"Ready - {via} connected, model {_settings.ActiveModel}."
                    : $"{via}, model {_settings.ActiveModel} - connection not verified yet.",
                Theme.TextSecondary);
            return;
        }

        SetStatus(
            _settings.Provider == ClaudeProvider.ClaudeCodeCli
                ? "Claude Code CLI not found. Open Settings, or install it with: npm install -g @anthropic-ai/claude-code"
                : "No Claude API key. Open Settings to connect your account.",
            Theme.Warning);
    }

    // ---------------------------------------------------------------- connection gate

    /// <summary>
    /// Every button except Settings stays disabled until the Claude connection has been verified
    /// with a real call. An expired session otherwise only shows itself after the user has done
    /// the work - a recording, say - and the generation that follows fails. The lock is on the
    /// button itself (<see cref="PillButton.Locked"/>), so none of the many places that enable
    /// and disable buttons can undo it.
    /// </summary>
    private void SetConnectionLocked(bool locked)
    {
        PillButton[] gated =
        [
            _previewButton, _refreshButton, _generateButton, _cancelButton, _recordButton, _screenshotsButton,
            _editButton, _clearButton, _copyButton, _openFolderButton, _insertButton
        ];

        foreach (var button in gated)
            button.Locked = locked;
    }

    /// <summary>Tests the connection for real and unlocks the window if it passes.</summary>
    private async Task VerifyConnectionAsync()
    {
        _verifyCts?.Cancel();
        _verifyCts?.Dispose();
        var cts = _verifyCts = new CancellationTokenSource();
        var version = ++_verifyVersion;

        _connectionVerified = false;
        SetConnectionLocked(true);

        if (!_settings.IsConfigured)
        {
            // Nothing to test - the CLI is missing or there is no key. Say what is missing.
            ReflectConnectionState();
            return;
        }

        SetStatus("Checking the Claude connection...", Theme.Accent);
        _verifying = true;
        _activityBar.Running = true;

        try
        {
            var result = _settings.Provider == ClaudeProvider.ClaudeCodeCli
                ? await ClaudeCliCodeGenerator.TestConnection(_settings, cts.Token)
                : await ClaudeAccount.TestConnection(_settings.ApiKey ?? string.Empty, cts.Token);

            // A newer check (or a pass from the Settings dialog) has taken over.
            if (version != _verifyVersion)
                return;

            if (result.Success)
            {
                Console.WriteLine($"Claude connection verified. {result.Message}");
                MarkConnectionVerified();
                return;
            }

            Console.Error.WriteLine($"Claude connection check failed: {result.Message}");
            HandleConnectionFailure(result.Message);
        }
        catch (OperationCanceledException)
        {
            // Superseded; whoever superseded it reports the outcome.
        }
        finally
        {
            if (version == _verifyVersion)
            {
                _verifying = false;
                _activityBar.Running = false;
            }
        }
    }

    private void MarkConnectionVerified()
    {
        _verifyCts?.Cancel();
        _verifyVersion++;
        _connectionVerified = true;

        if (_verifying)
        {
            _verifying = false;
            _activityBar.Running = false;
        }

        SetConnectionLocked(false);
        ReflectConnectionState();
    }

    private void HandleConnectionFailure(string message)
    {
        if (_settings.Provider == ClaudeProvider.ClaudeCodeCli && ClaudeCliCodeGenerator.IsAuthenticationFailure(message))
        {
            // Same help as a failed generation: put a real terminal in front of the user so
            // they can sign in themselves. The app never handles Claude credentials.
            try
            {
                new ClaudeCliCodeGenerator(_settings).OpenSignInTerminal();
                SetStatus("Claude sign-in needed. A terminal opened - sign in there, then open Settings and press Test Connection.", Theme.Warning);
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not open a sign-in terminal: {ex.Message}");
            }
        }

        SetStatus($"Claude connection failed - {message}  Open Settings to fix it.", Theme.Danger);
    }

    /// <summary>True when a change in Settings could change whether Claude is reachable.</summary>
    private static bool ConnectionSettingsChanged(AppSettings before, AppSettings after) =>
        before.Provider != after.Provider ||
        before.ActiveModel != after.ActiveModel ||
        before.ClaudeCliPath != after.ClaudeCliPath ||
        before.ApiKey != after.ApiKey;

    private void OpenSettings()
    {
        var before = _settings;
        using var dialog = new SettingsForm(_settings);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            // Cancelled. If the window is still locked, give it another go: the user may have
            // signed in or fixed something outside the app while the dialog was open.
            if (!_connectionVerified && !IsBusy)
                _ = VerifyConnectionAsync();

            return;
        }

        _settings = dialog.Result;
        Console.WriteLine($"Settings saved. Provider: {_settings.Provider}, model: {_settings.ActiveModel}.");
        RebuildAgent();

        // Saving unrelated settings (a folder, the SpecForge path) must not lock the window and
        // re-test; only a change that could affect the connection, or one that was never verified.
        if (_connectionVerified && !ConnectionSettingsChanged(before, _settings))
            return;

        if (dialog.ConnectionVerified)
            MarkConnectionVerified();
        else if (!IsBusy)
            _ = VerifyConnectionAsync();
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

        // Verify the connection while the preview engine starts; both take a few seconds.
        var verification = VerifyConnectionAsync();
        await EnsureWebViewReady();
        await verification;
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
        // The input card's fixed rows now total three target pickers (Feature / Page
        // Object / Steps, 88px each) plus the url row and card padding - about 640px
        // before the objective field gets a usable height rather than a sliver. If you
        // add rows to the input card, raise this to match.
        ConfigureSplit(leftSplit, minPanel1: 640, minPanel2: 170, desired: 760);
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

        var layout = NewCardLayout(rows: 8);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // 0 title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // 1 url caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // 2 url row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));   // 3 target pickers (feature / page object / steps)
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // 4 target hint
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // 5 objective caption
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 6 objective field
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));   // 7 actions

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

        // Three side-by-side pickers rather than three stacked blocks: each lets the user
        // target an existing file (extend it) or leave "New ..." to create one, exactly
        // like the feature-file picker did on its own before the page object and step
        // targets joined it. A combined hint line underneath reports all three choices at
        // once, since a full caption+picker+hint block per target would triple the height
        // this card needs.
        var targetsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Theme.SurfaceAlt
        };
        targetsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        targetsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        targetsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        targetsRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        targetsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _featureBox = new DarkComboBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
        _featureBox.SelectedIndexChanged += (s, e) => ShowTargetHints();

        _pageObjectBox = new DarkComboBox { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 0) };
        _pageObjectBox.SelectedIndexChanged += (s, e) => ShowTargetHints();

        _stepFileBox = new DarkComboBox { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        _stepFileBox.SelectedIndexChanged += (s, e) => ShowTargetHints();

        targetsRow.Controls.Add(MiniCaption("Feature file"), 0, 0);
        targetsRow.Controls.Add(MiniCaption("Page object"), 1, 0);
        targetsRow.Controls.Add(MiniCaption("Step definitions"), 2, 0);
        targetsRow.Controls.Add(_featureBox, 0, 1);
        targetsRow.Controls.Add(_pageObjectBox, 1, 1);
        targetsRow.Controls.Add(_stepFileBox, 2, 1);

        layout.Controls.Add(targetsRow, 0, 3);

        var targetHintRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt
        };
        targetHintRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        targetHintRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        targetHintRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));

        _featureHint = TargetHintLabel();
        _pageObjectHint = TargetHintLabel();
        _stepFileHint = TargetHintLabel();
        _featureHint.Margin = new Padding(0, 0, 6, 0);
        _pageObjectHint.Margin = new Padding(3, 0, 3, 0);
        _stepFileHint.Margin = new Padding(6, 0, 0, 0);

        targetHintRow.Controls.Add(_featureHint, 0, 0);
        targetHintRow.Controls.Add(_pageObjectHint, 1, 0);
        targetHintRow.Controls.Add(_stepFileHint, 2, 0);
        layout.Controls.Add(targetHintRow, 0, 4);

        layout.Controls.Add(Caption("Test Objective"), 0, 5);

        _objectiveField = new FieldBox(multiline: true)
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "e.g. Verify the Skills section lists every skill item and each one is visible."
        };
        layout.Controls.Add(_objectiveField, 0, 6);

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

        _screenshotsButton = new PillButton { Text = "Screenshots", Style = PillStyle.Outline, Width = 128, Dock = DockStyle.Fill, Enabled = false };
        _screenshotsButton.Click += (s, e) => OpenScreenshotsViewer();

        var actionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt,
            Margin = new Padding(0, 10, 0, 0)
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        _generateButton.Margin = new Padding(0, 0, 10, 0);
        _cancelButton.Margin = new Padding(0, 0, 10, 0);
        _recordButton.Margin = new Padding(0, 0, 10, 0);
        _screenshotsButton.Margin = Padding.Empty;
        actionRow.Controls.Add(_generateButton, 0, 0);
        actionRow.Controls.Add(_cancelButton, 1, 0);
        actionRow.Controls.Add(_recordButton, 2, 0);
        actionRow.Controls.Add(_screenshotsButton, 3, 0);
        layout.Controls.Add(actionRow, 0, 7);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildOutputCard()
    {
        var card = new Card { Dock = DockStyle.Fill };

        var layout = NewCardLayout(rows: 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Four tabs: the three files a Gherkin run produces, plus the Log. A plain NUnit
        // fixture (no Reqnroll solution linked) has only one file - it goes in Steps, the
        // closest of the three to "the runnable code" - and Feature/Page Object stay empty.
        _outputTabs = new SegmentedTabs();
        _outputTabs.SetItems("Feature", "Page Object", "Steps", "Log");
        _outputTabs.SelectedIndexChanged += (s, e) => ShowOutputTab(_outputTabs.SelectedIndex);

        // These four buttons all act on whichever tab is showing, the way Copy/Clear
        // already did - Edit opens a window for the active tab's content; Copy/Clear read
        // and clear it. Insert is the only one that is not tab-scoped: it always writes
        // every artifact from the last run, using whichever targets were selected.
        _editButton = new PillButton { Text = "Edit", Style = PillStyle.Outline, Height = 32, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        _editButton.Click += (s, e) => EditActiveArtifact();

        _copyButton = new PillButton { Text = "Copy", Style = PillStyle.Outline, Height = 32, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        _copyButton.Click += (s, e) =>
        {
            var box = ActiveOutputBox;
            var what = ActiveOutputName;

            if (string.IsNullOrWhiteSpace(box.Text))
            {
                SetStatus($"Nothing to copy - the {what} is empty.", Theme.Warning);
                return;
            }

            Clipboard.SetText(box.Text);
            SetStatus($"{char.ToUpperInvariant(what[0])}{what[1..]} copied to clipboard.", Theme.Accent);
        };

        _clearButton = new PillButton { Text = "Clear", Style = PillStyle.Ghost, Height = 32, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        _clearButton.Click += (s, e) =>
        {
            var what = ActiveOutputName;

            if (string.IsNullOrWhiteSpace(ActiveOutputBox.Text))
            {
                SetStatus($"The {what} is already empty.", Theme.TextSecondary);
                return;
            }

            ActiveOutputBox.Clear();
            ClearScreenshots();
            SetStatus($"Cleared the {what}.", Theme.TextSecondary);
        };

        _openFolderButton = new PillButton { Text = "Folder", Style = PillStyle.Outline, Height = 32, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), Enabled = false };
        _openFolderButton.Click += (s, e) => OpenSolutionFolder();

        _insertButton = new PillButton { Text = "Insert", Style = PillStyle.Primary, Height = 32, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), Enabled = false };
        _insertButton.Click += (s, e) => InsertToSolution();

        // A TableLayoutPanel rather than docked buttons: with Dock=Right the buttons were
        // clipped out of existence as the card narrowed (Clear vanished entirely at the
        // minimum window size). Here the tab strip absorbs the shrinking instead, and every
        // button stays reachable.
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt,
            Margin = new Padding(0, 0, 0, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        _outputTabs.Dock = DockStyle.Fill;
        _outputTabs.Margin = Padding.Empty;

        header.Controls.Add(_outputTabs, 0, 0);
        header.Controls.Add(_editButton, 1, 0);
        header.Controls.Add(_clearButton, 2, 0);
        header.Controls.Add(_copyButton, 3, 0);
        header.Controls.Add(_openFolderButton, 4, 0);
        header.Controls.Add(_insertButton, 5, 0);
        layout.Controls.Add(header, 0, 0);

        // Code does not wrap - wrapping changes how it reads. Log lines do, because they
        // are prose and would otherwise need horizontal scrolling.
        _featureTextBox = NewConsoleBox(wrap: false);
        _pageObjectTextBox = NewConsoleBox(wrap: false);
        _stepsTextBox = NewConsoleBox(wrap: false);
        _logTextBox = NewConsoleBox(wrap: true);

        var content = new Card
        {
            Dock = DockStyle.Fill,
            Fill = Theme.Surface,
            Radius = 8,
            BackColor = Theme.SurfaceAlt,
            Padding = new Padding(12, 10, 6, 10)
        };
        content.Controls.Add(_featureTextBox);
        content.Controls.Add(_pageObjectTextBox);
        content.Controls.Add(_stepsTextBox);
        content.Controls.Add(_logTextBox);
        layout.Controls.Add(content, 0, 1);

        card.Controls.Add(layout);

        // Sets initial visibility (only the Feature box) and the Edit button's enabled
        // state, matching whatever ShowOutputTab does for every tab switch afterwards.
        ShowOutputTab(0);

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

        // Reloads the page that is on screen, wherever the user has navigated to. The Preview
        // button beside the URL always goes back to the typed address, which is the wrong tool
        // for a session that expired mid-flow: that would throw away the user's place.
        // Docked after the title and before the URL label takes the leftover space, as above.
        _refreshButton = new PillButton { Text = "Refresh", Style = PillStyle.Outline, Dock = DockStyle.Fill };
        _refreshButton.Click += async (s, e) => await RefreshPreview();
        header.Controls.Add(new Panel
        {
            Dock = DockStyle.Right,
            Width = 98,
            BackColor = Theme.SurfaceAlt,
            Padding = new Padding(8, 2, 0, 2),
            Controls = { _refreshButton }
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

    /// <summary>A smaller caption for the three side-by-side target pickers.</summary>
    private static Label MiniCaption(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text.ToUpperInvariant(),
        Font = Theme.Ui(7f, FontStyle.Bold),
        ForeColor = Theme.TextDisabled,
        BackColor = Theme.SurfaceAlt,
        TextAlign = ContentAlignment.BottomLeft,
        AutoEllipsis = true
    };

    private static Label TargetHintLabel() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Font = Theme.Ui(7.5f),
        ForeColor = Theme.TextDisabled,
        BackColor = Theme.SurfaceAlt,
        TextAlign = ContentAlignment.TopLeft,
        Padding = new Padding(0, 2, 0, 0)
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
        _featureTextBox.Visible = index == 0;
        _pageObjectTextBox.Visible = index == 1;
        _stepsTextBox.Visible = index == 2;
        _logTextBox.Visible = index == 3;

        // The log is generated console output, not a file - there is nothing to edit or
        // insert about it.
        _editButton.Enabled = index != 3;
    }

    /// <summary>The box the user is actually looking at.</summary>
    private TextBox ActiveOutputBox => _outputTabs.SelectedIndex switch
    {
        0 => _featureTextBox,
        1 => _pageObjectTextBox,
        2 => _stepsTextBox,
        _ => _logTextBox
    };

    private string ActiveOutputName => _outputTabs.SelectedIndex switch
    {
        0 => "feature file",
        1 => "page object",
        2 => "step definitions",
        _ => "log"
    };

    /// <summary>Which artifact kind the active tab shows, or null for the Log tab.</summary>
    private ArtifactKind? ActiveArtifactKind => _outputTabs.SelectedIndex switch
    {
        0 => ArtifactKind.Feature,
        1 => ArtifactKind.PageObject,
        2 => ArtifactKind.StepDefinitions,
        _ => null
    };

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
            {
                // The label otherwise keeps showing the address that was typed, even after the
                // user has clicked through to another page - which is the page Refresh reloads.
                _webView.CoreWebView2!.SourceChanged += (s, e) =>
                {
                    var source = _webView.CoreWebView2?.Source;
                    if (!string.IsNullOrWhiteSpace(source) && source != "about:blank")
                        _previewUrlLabel.Text = source;
                };

                _webView.CoreWebView2!.Navigate("about:blank");
            }

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

    /// <summary>
    /// Reloads whatever page the preview is showing, so a session that expired while the
    /// user was idle can be renewed without leaving the page they were on. Falls back to the
    /// typed URL when nothing has been loaded yet. Allowed mid-recording: the recorder's
    /// script is registered for every new document, a reload of the same address is not
    /// recorded as a step, and if the expired session bounces to a login page that
    /// navigation is recorded, which is what actually happened.
    /// </summary>
    private async Task RefreshPreview()
    {
        if (_refreshButton.Locked)
            return;

        var current = _webViewReady ? _webView.CoreWebView2?.Source : null;
        if (string.IsNullOrWhiteSpace(current) || current == "about:blank")
        {
            await LoadPreview();
            return;
        }

        try
        {
            _webView.CoreWebView2!.Reload();
            SetStatus($"Preview refreshing {current}", Theme.TextSecondary);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Preview refresh failed: {ex.Message}");
            SetStatus($"Preview refresh failed: {ex.Message}", Theme.Danger);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // F5 is the browser convention. WebView2 reloads on its own when it has focus; this
        // covers the rest of the window, where the key would otherwise do nothing.
        if (keyData == Keys.F5)
        {
            _ = RefreshPreview();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
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
        _outputTabs.SelectedIndex = 3; // Log

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
                TargetFeaturePath = SelectedFeature?.Path,
                TargetPageObjectPath = SelectedPageObject?.Path,
                TargetStepDefinitionsPath = SelectedStepDefinitionFile?.Path,
                ScreenshotsPath = _settings.ScreenshotsPath,
                AdoOrganization = _settings.AdoOrganization,
                AdoProject = _settings.AdoProject
            };
            var result = await _agent.Run(request, _cts.Token);

            // The static-template fallback (no AI, or the AI call failed) never populates
            // Artifacts, only GeneratedCode. Wrap it the same way a real multi-file result
            // is wrapped, so every downstream consumer - the tabs, Edit, Insert - has one
            // shape to deal with regardless of which path produced it.
            _lastArtifacts = result.Artifacts.Count > 0
                ? result.Artifacts
                : [new GeneratedArtifact { Kind = ArtifactKind.Test, FileName = "GeneratedTest.cs", Content = result.GeneratedCode }];

            // A recording already populated this from the recorder itself; a typed-objective
            // run has no recorded actions, so whatever the agent captured (or didn't) replaces
            // stale state from an earlier recording rather than leaving it hanging around.
            if (_recordedActions.Count == 0)
            {
                _recordedScreenshots = [.. result.Screenshots];
                UpdateScreenshotsButtonState();
            }

            PopulateArtifactTabs();

            foreach (var warning in result.Warnings)
                Console.Error.WriteLine(warning);

            UpdateInsertButtonState();

            // The old code reported success even when the AI had failed and the static
            // stub was returned. Report what actually happened. Nothing is written to disk
            // here any more - Insert is now the explicit, user-triggered step, so there is
            // a chance to review or edit first.
            if (result.ReuseReport is { } reuse)
            {
                Console.WriteLine($"Step reuse (SpecForge): {reuse.Summary}");
                foreach (var step in reuse.NewSteps)
                    Console.WriteLine($"  new step: {step}");
            }

            if (result.UsedAi)
            {
                _outputTabs.SelectedIndex = 0;

                var reuseNote = result.ReuseReport is { Total: > 0 } r
                    ? $" {r.Reused} of {r.Total} steps reuse existing bindings."
                    : "";

                // Say so when the objective named a PBI: either it was used (with how many test
                // cases) or it could not be read and the run fell back to the plain objective.
                var pbiNote = result.Pbi is { } pbi
                    ? $" Built from PBI {pbi.Id} ({pbi.TestCases.Count} linked test case(s))."
                    : PbiReferenceFinder.Find(objective) is { } missed && _recordedActions.Count == 0
                        ? $" PBI {missed.Id} could not be read - see Log."
                        : "";
                SetStatus($"Test generated.{pbiNote}{reuseNote} Review it, then press Insert to write it into the solution.", Theme.Accent);
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
            _outputTabs.SelectedIndex = 3; // Log
        }
        finally
        {
            SetRunning(false);
        }
    }

    private void SetRunning(bool running)
    {
        _running = running;
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
            _recordedScreenshots = [];
            UpdateScreenshotsButtonState();
            _recorder = new PreviewRecorder(_webView);
            _recorder.ActionRecorded += OnActionRecorded;

            _outputTabs.SelectedIndex = 3; // Log
            SetRecordingUi(true);

            await _recorder.StartAsync(url, _settings.ScreenshotsPath);
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
            _recordedScreenshots = [.. _recorder.Screenshots];
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

        UpdateScreenshotsButtonState();

        if (_recordedActions.Count == 0)
        {
            SetStatus("No steps were recorded.", Theme.Warning);
            return;
        }

        var screenshotNote = _recordedScreenshots.Count > 0
            ? $" ({_recordedScreenshots.Count} screenshot(s) captured)"
            : "";
        SetStatus($"Captured {_recordedActions.Count} step(s){screenshotNote}. Generating the test...", Theme.Accent);
        await GenerateTest();
    }

    private void UpdateScreenshotsButtonState() =>
        _screenshotsButton.Enabled = _recordedScreenshots.Count > 0;

    /// <summary>
    /// Deletes the screenshots on disk and forgets them in memory. Screenshots are a
    /// debugging aid tied to one recording pass - once its code has been cleared or written
    /// into the solution, keeping stale images around would only let the viewer show a batch
    /// that no longer matches anything on screen.
    /// </summary>
    private void ClearScreenshots()
    {
        if (_recordedScreenshots.Count == 0)
            return;

        ScreenshotCapture.ClearFolder(_settings.ScreenshotsPath);
        _recordedScreenshots = [];
        UpdateScreenshotsButtonState();
    }

    private void OpenScreenshotsViewer()
    {
        if (_recordedScreenshots.Count == 0)
        {
            SetStatus("No screenshots captured yet. Set a folder in Settings, then record.", Theme.Warning);
            return;
        }

        try
        {
            using var dialog = new ScreenshotViewerDialog(_recordedScreenshots);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            // A bug in the viewer must never take down the whole app over what is only a
            // debugging aid - report it and move on.
            Console.Error.WriteLine($"Could not open the screenshots viewer: {ex}");
            SetStatus($"Could not open the screenshots viewer: {ex.Message}", Theme.Danger);
        }
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
        _recordingUi = recording;
        _recordButton.Text = recording ? "Done" : "Record";
        _recordButton.Style = recording ? PillStyle.Primary : PillStyle.Outline;
        _generateButton.Enabled = !recording;
        _previewButton.Enabled = !recording;
    }

    // ---------------------------------------------------------------- target pickers
    //
    // Feature, Page Object and Step Definitions each get the same three things: a choice
    // record wrapping "an existing file" or "create new", a Populate method that fills the
    // dropdown from the scanned solution, and a hint that reports what selecting it means.
    // Kept as three near-identical blocks rather than one generic one because each choice
    // carries a different file type and a different hint message - collapsing them would
    // trade this straightforward repetition for a generics puzzle that is not worth solving
    // for three dropdowns.

    private sealed record FeatureChoice(FeatureFile? File, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record PageObjectChoice(PageObjectFile? File, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record StepFileChoice(StepDefinitionFile? File, string Label)
    {
        public override string ToString() => Label;
    }

    private void PopulateTargetPickers()
    {
        PopulateFeatureFiles();
        PopulatePageObjectFiles();
        PopulateStepDefinitionFiles();
    }

    private void PopulateFeatureFiles()
    {
        _featureBox.Items.Clear();

        if (_solutionProfile is not { CanWriteGherkin: true })
        {
            _featureBox.Items.Add(new FeatureChoice(null, "No Gherkin solution linked"));
            _featureBox.SelectedIndex = 0;
            _featureBox.Enabled = false;
        }
        else
        {
            _featureBox.Enabled = true;
            _featureBox.Items.Add(new FeatureChoice(null, "New feature file"));

            foreach (var feature in _solutionProfile.Features)
                _featureBox.Items.Add(new FeatureChoice(feature, feature.ToString()));

            _featureBox.SelectedIndex = 0;
        }

        ShowTargetHints();
    }

    private void PopulatePageObjectFiles()
    {
        _pageObjectBox.Items.Clear();

        if (_solutionProfile is not { CanWriteGherkin: true })
        {
            _pageObjectBox.Items.Add(new PageObjectChoice(null, "No Gherkin solution linked"));
            _pageObjectBox.SelectedIndex = 0;
            _pageObjectBox.Enabled = false;
        }
        else
        {
            _pageObjectBox.Enabled = true;
            _pageObjectBox.Items.Add(new PageObjectChoice(null, "New page object"));

            foreach (var page in _solutionProfile.PageObjects)
                _pageObjectBox.Items.Add(new PageObjectChoice(page, page.ToString()));

            _pageObjectBox.SelectedIndex = 0;
        }

        ShowTargetHints();
    }

    private void PopulateStepDefinitionFiles()
    {
        _stepFileBox.Items.Clear();

        if (_solutionProfile is not { CanWriteGherkin: true })
        {
            _stepFileBox.Items.Add(new StepFileChoice(null, "No Gherkin solution linked"));
            _stepFileBox.SelectedIndex = 0;
            _stepFileBox.Enabled = false;
        }
        else
        {
            _stepFileBox.Enabled = true;
            _stepFileBox.Items.Add(new StepFileChoice(null, "New step file"));

            foreach (var file in _solutionProfile.StepDefinitionFiles)
                _stepFileBox.Items.Add(new StepFileChoice(file, file.ToString()));

            _stepFileBox.SelectedIndex = 0;
        }

        ShowTargetHints();
    }

    /// <summary>
    /// Shows what each selection means, so the user can see - before generating - which
    /// scenarios or methods already exist and that a new one will be appended rather than
    /// replacing them.
    /// </summary>
    private void ShowTargetHints()
    {
        var linked = _solutionProfile is { CanWriteGherkin: true };

        if (_featureBox.SelectedItem is FeatureChoice featureChoice)
        {
            _featureHint.Text = !linked
                ? "Link a Reqnroll solution in Settings."
                : featureChoice.File == null
                    ? "New .feature file, named after the behaviour."
                    : featureChoice.File.Scenarios.Count == 0
                        ? $"{featureChoice.File.FileName}: no scenarios yet."
                        : $"Appending to {featureChoice.File.Scenarios.Count} scenario(s).";
        }

        if (_pageObjectBox.SelectedItem is PageObjectChoice pageChoice)
        {
            _pageObjectHint.Text = !linked
                ? "Link a Reqnroll solution in Settings."
                : pageChoice.File == null
                    ? "New page object, named after the page."
                    : $"Extending {pageChoice.File.ClassName} ({pageChoice.File.Methods.Count} method(s)).";
        }

        if (_stepFileBox.SelectedItem is StepFileChoice stepChoice)
        {
            _stepFileHint.Text = !linked
                ? "Link a Reqnroll solution in Settings."
                : stepChoice.File == null
                    ? "New file, holding only the new step(s)."
                    : $"Adding to {stepChoice.File.ClassName} ({stepChoice.File.BindingCount} existing step(s)).";
        }
    }

    private FeatureFile? SelectedFeature =>
        (_featureBox.SelectedItem as FeatureChoice)?.File;

    private PageObjectFile? SelectedPageObject =>
        (_pageObjectBox.SelectedItem as PageObjectChoice)?.File;

    private StepDefinitionFile? SelectedStepDefinitionFile =>
        (_stepFileBox.SelectedItem as StepFileChoice)?.File;

    /// <summary>
    /// Puts what the last run produced into the three code tabs. The single-artifact
    /// fallback (static template, or a plain NUnit fixture with no Gherkin solution linked)
    /// has no Feature or Page Object file, so those tabs are simply left blank rather than
    /// treated as an error - there is nothing wrong, there is just nothing to show there.
    /// </summary>
    private void PopulateArtifactTabs()
    {
        _featureTextBox.Text = ContentFor(ArtifactKind.Feature);
        _pageObjectTextBox.Text = ContentFor(ArtifactKind.PageObject);
        // A plain fixture (ArtifactKind.Test) is the closest thing to runnable code this
        // app produces without a Gherkin solution, so it lands on the Steps tab.
        _stepsTextBox.Text = ContentFor(ArtifactKind.StepDefinitions, fallback: ArtifactKind.Test);
    }

    private string ContentFor(ArtifactKind kind, ArtifactKind? fallback = null)
    {
        var artifact = _lastArtifacts.FirstOrDefault(a => a.Kind == kind)
                       ?? (fallback.HasValue ? _lastArtifacts.FirstOrDefault(a => a.Kind == fallback) : null);

        return artifact?.Content.ReplaceLineEndings() ?? "";
    }

    /// <summary>
    /// Opens the active tab's content in a full-window editor. Saving updates the in-memory
    /// artifact (and the read-only preview box) but writes nothing to disk - that only
    /// happens when Insert is pressed, so a half-finished edit can never end up on disk.
    /// </summary>
    private void EditActiveArtifact()
    {
        var kind = ActiveArtifactKind;
        if (kind == null)
        {
            SetStatus("Nothing to edit on the Log tab.", Theme.Warning);
            return;
        }

        var artifact = _lastArtifacts.FirstOrDefault(a => a.Kind == kind)
                       ?? (kind == ArtifactKind.StepDefinitions ? _lastArtifacts.FirstOrDefault(a => a.Kind == ArtifactKind.Test) : null);

        if (artifact == null)
        {
            SetStatus("Generate a test first.", Theme.Warning);
            return;
        }

        using var dialog = new CodeEditorDialog($"Edit {ActiveOutputName}", artifact.Content);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        artifact.Content = dialog.Content;
        ActiveOutputBox.Text = artifact.Content.ReplaceLineEndings();
        SetStatus($"Updated the {ActiveOutputName}. Press Insert to write it into the solution.", Theme.TextSecondary);
    }

    /// <summary>
    /// Writes everything the last run produced - including any edits made through the Edit
    /// window - into the solution. This is the explicit step the user triggers after
    /// reviewing the output; nothing is written automatically any more.
    /// </summary>
    private void InsertToSolution()
    {
        if (_solutionProfile is not { CanInsert: true })
        {
            SetStatus("Link a solution in Settings first.", Theme.Warning);
            return;
        }

        if (_lastArtifacts.Count == 0)
        {
            SetStatus("Generate a test first.", Theme.Warning);
            return;
        }

        var written = SolutionWriter.Write(_solutionProfile, _lastArtifacts);

        if (written.Count == 0)
        {
            SetStatus("Nothing could be written to the solution. See Log.", Theme.Danger);
            return;
        }

        var names = string.Join(", ", written.Select(a => Path.GetFileName(a.WrittenPath!)));
        SetStatus($"Inserted into solution: {names}", Theme.Accent);

        ClearScreenshots();

        // Picking up a newly created (or newly extended) file requires a rescan.
        RescanSolution();
    }

    private void RescanSolution()
    {
        if (string.IsNullOrWhiteSpace(_settings.TestSolutionPath))
            return;

        try
        {
            var selectedFeature = SelectedFeature?.Path;
            var selectedPageObject = SelectedPageObject?.Path;
            var selectedStepFile = SelectedStepDefinitionFile?.Path;

            _solutionProfile = SolutionScanner.Scan(_settings.TestSolutionPath);
            _agent = NewAgent(BuildGenerator());

            PopulateTargetPickers();
            RestoreSelection(_featureBox, selectedFeature, (FeatureChoice c) => c.File?.Path);
            RestoreSelection(_pageObjectBox, selectedPageObject, (PageObjectChoice c) => c.File?.Path);
            RestoreSelection(_stepFileBox, selectedStepFile, (StepFileChoice c) => c.File?.Path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not rescan the solution: {ex.Message}");
        }
    }

    /// <summary>Re-selects whichever item still matches the given path after a rescan replaced the list.</summary>
    private static void RestoreSelection<TChoice>(DarkComboBox box, string? path, Func<TChoice, string?> pathOf)
        where TChoice : class
    {
        if (path == null)
            return;

        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is TChoice choice &&
                string.Equals(pathOf(choice), path, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
        }
    }

    private void UpdateInsertButtonState()
    {
        var solutionReady = _solutionProfile is { CanInsert: true };

        _openFolderButton.Enabled = solutionReady;
        _insertButton.Enabled = solutionReady && _lastArtifacts.Count > 0;
    }

    /// <summary>Reveals where generated files land, independent of whether anything has been inserted yet.</summary>
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
