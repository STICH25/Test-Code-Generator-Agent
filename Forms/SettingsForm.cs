using PlaywrightAgentAI.Services;
using PlaywrightAgentAI.UI;

namespace PlaywrightAgentAI.Forms;

/// <summary>
/// Claude account and model settings.
///
/// Opens on a working copy of the settings; nothing is persisted unless Save is pressed,
/// so cancelling out cannot leave a half-applied configuration behind.
/// </summary>
public class SettingsForm : Form
{
    private static readonly string[] EffortLevels = ["low", "medium", "high", "max"];

    private readonly AppSettings _draft;

    private FieldBox _apiKeyField = null!;
    private PillButton _revealButton = null!;
    private PillButton _testButton = null!;
    private PillButton _saveButton = null!;
    private PillButton _cancelButton = null!;
    private DarkComboBox _providerBox = null!;
    private DarkComboBox _modelBox = null!;
    private DarkComboBox _effortBox = null!;
    private FieldBox _maxTokensField = null!;
    private Label _statusLabel = null!;
    private Label _storagePathLabel = null!;
    private Control _apiKeyRow = null!;
    private Control _apiCaptionLabel = null!;
    private Control _tuningRow = null!;
    private Control _tuningCaption = null!;
    private TableLayoutPanel _layout = null!;
    private Panel _scrollHost = null!;
    private FieldBox _solutionField = null!;
    private PillButton _browseButton = null!;
    private Label _solutionStatusLabel = null!;
    private FieldBox _screenshotsField = null!;
    private PillButton _screenshotsBrowseButton = null!;
    private Label _screenshotsStatusLabel = null!;
    private FieldBox _adoOrgField = null!;
    private FieldBox _adoProjectField = null!;
    private FieldBox _specForgeField = null!;
    private PillButton _specForgeBrowseButton = null!;
    private Label _specForgeStatusLabel = null!;

    private CancellationTokenSource? _testCts;
    private bool _modelsLoaded;

    public SettingsForm(AppSettings current)
    {
        _draft = current.Clone();
        BuildLayout();
        LoadFromDraft();
    }

    /// <summary>The saved settings. Only meaningful when the dialog returns OK.</summary>
    public AppSettings Result => _draft;

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 920);
        MinimumSize = new Size(580, 520);
        BackColor = Theme.Page;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Ui(9f);
        Padding = new Padding(Theme.Gutter);

        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(22) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 27,
            BackColor = Theme.SurfaceAlt
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 0  title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // 1  provider caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 2  provider box
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // 3  api caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // 4  api row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // 5  test row
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // 6  model caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 7  model box
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // 8  tuning caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 9  effort + tokens
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // 10 storage note
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));  // 11 solution title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // 12 solution caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // 13 solution picker
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 14 solution notes
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));  // 15 screenshots title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // 16 screenshots caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // 17 screenshots picker
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 18 screenshots status
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));  // 19 specforge title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // 20 specforge caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // 21 specforge picker
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 22 specforge status
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));  // 23 azure devops title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // 24 azure devops caption
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // 25 organization + project
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));  // 26 azure devops note

        layout.Controls.Add(SectionTitle("Claude Account"), 0, 0);
        layout.Controls.Add(Caption("Connect using"), 0, 1);

        _providerBox = new DarkComboBox { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6) };
        _providerBox.Items.Add("Claude Code CLI (uses your existing login)");
        _providerBox.Items.Add("Anthropic API key");
        _providerBox.SelectedIndexChanged += (s, e) =>
        {
            _draft.Provider = _providerBox.SelectedIndex == 1 ? ClaudeProvider.ApiKey : ClaudeProvider.ClaudeCodeCli;
            ApplyProviderVisibility();
        };
        layout.Controls.Add(_providerBox, 0, 2);

        _apiCaptionLabel = Caption("API Key");
        layout.Controls.Add(_apiCaptionLabel, 0, 3);

        _apiKeyField = new FieldBox { Dock = DockStyle.Fill, PlaceholderText = "sk-ant-..." };
        _apiKeyField.Inner.UseSystemPasswordChar = true;
        _apiKeyField.Inner.TextChanged += (s, e) =>
        {
            // The loaded model list belongs to the previous key.
            _modelsLoaded = false;
            SetStatus("Key changed - test the connection to refresh the model list.", Theme.TextSecondary);
        };

        _revealButton = new PillButton { Text = "Show", Style = PillStyle.Outline, Width = 82, Dock = DockStyle.Fill };
        _revealButton.Click += (s, e) =>
        {
            var hidden = _apiKeyField.Inner.UseSystemPasswordChar;
            _apiKeyField.Inner.UseSystemPasswordChar = !hidden;
            _revealButton.Text = hidden ? "Hide" : "Show";
        };

        _apiKeyRow = SplitRow(_apiKeyField, _revealButton, 82);
        layout.Controls.Add(_apiKeyRow, 0, 4);

        _testButton = new PillButton { Text = "Test Connection", Style = PillStyle.Primary, Dock = DockStyle.Fill, Height = 36 };
        _testButton.Click += async (s, e) => await TestConnection();

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.Ui(9f),
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.SurfaceAlt,
            Margin = new Padding(12, 0, 0, 0),
            Text = "Not tested."
        };

        layout.Controls.Add(SplitRow(_testButton, _statusLabel, 300, swapWeights: true), 0, 5);

        layout.Controls.Add(Caption("Model"), 0, 6);

        _modelBox = new DarkComboBox { Dock = DockStyle.Fill, Height = 34, Margin = new Padding(0, 0, 0, 6) };
        _modelBox.SelectedIndexChanged += (s, e) =>
        {
            if (_modelBox.SelectedItem is not ClaudeModel model)
                return;

            if (_draft.Provider == ClaudeProvider.ClaudeCodeCli)
                _draft.CliModel = model.Id;
            else
                _draft.Model = model.Id;
        };
        layout.Controls.Add(_modelBox, 0, 7);

        _tuningCaption = Caption("Effort and output limit");
        layout.Controls.Add(_tuningCaption, 0, 8);

        _effortBox = new DarkComboBox { Dock = DockStyle.Top, Margin = Padding.Empty };
        _effortBox.Items.AddRange([.. EffortLevels]);
        _effortBox.SelectedIndexChanged += (s, e) =>
        {
            if (_effortBox.SelectedItem is string effort)
                _draft.Effort = effort;
        };

        // Control.Margin defaults to 3px all round, which was pushing this field out of
        // line with the combo beside it.
        _maxTokensField = new FieldBox { Dock = DockStyle.Top, PlaceholderText = "16000", Margin = Padding.Empty };

        var tuningRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt
        };
        tuningRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        tuningRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        _effortBox.Margin = new Padding(0, 0, 10, 0);
        tuningRow.Controls.Add(_effortBox, 0, 0);
        tuningRow.Controls.Add(_maxTokensField, 1, 0);
        _tuningRow = tuningRow;
        layout.Controls.Add(tuningRow, 0, 9);

        _storagePathLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = Theme.Ui(8.5f),
            ForeColor = Theme.TextDisabled,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.BottomLeft,
            Text = $"The key is encrypted with Windows DPAPI for your user account and stored at{Environment.NewLine}{SettingsStore.FilePath}"
        };
        layout.Controls.Add(_storagePathLabel, 0, 10);

        layout.Controls.Add(SectionTitle("Test Solution"), 0, 11);
        layout.Controls.Add(Caption("Folder to read conventions from and write tests into"), 0, 12);

        _solutionField = new FieldBox { Dock = DockStyle.Fill, PlaceholderText = @"C:\repos\YourTestFramework" };
        _solutionField.Inner.TextChanged += (s, e) => ScanSolution();

        _browseButton = new PillButton { Text = "Browse", Style = PillStyle.Outline, Width = 92, Dock = DockStyle.Fill };
        _browseButton.Click += (s, e) => BrowseForSolution();

        layout.Controls.Add(SplitRow(_solutionField, _browseButton, 92), 0, 13);

        _solutionStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = Theme.Ui(8.5f),
            ForeColor = Theme.TextDisabled,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(2, 6, 0, 0),
            Text = "No solution linked - generated tests will be self-contained."
        };
        layout.Controls.Add(_solutionStatusLabel, 0, 14);

        layout.Controls.Add(SectionTitle("Debug Screenshots"), 0, 15);
        layout.Controls.Add(Caption("Folder for per-click screenshots (blank disables this)"), 0, 16);

        _screenshotsField = new FieldBox { Dock = DockStyle.Fill, PlaceholderText = @"C:\Users\you\Desktop\TestScreenshots" };
        _screenshotsField.Inner.TextChanged += (s, e) => ScanScreenshotsFolder();

        _screenshotsBrowseButton = new PillButton { Text = "Browse", Style = PillStyle.Outline, Width = 92, Dock = DockStyle.Fill };
        _screenshotsBrowseButton.Click += (s, e) => BrowseForScreenshotsFolder();

        layout.Controls.Add(SplitRow(_screenshotsField, _screenshotsBrowseButton, 92), 0, 17);

        _screenshotsStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = Theme.Ui(8.5f),
            ForeColor = Theme.TextDisabled,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(2, 6, 0, 0),
            Text = "Not set - clicks will not be captured while recording."
        };
        layout.Controls.Add(_screenshotsStatusLabel, 0, 18);

        layout.Controls.Add(SectionTitle("Step Reuse Check"), 0, 19);
        layout.Controls.Add(Caption("SpecForge location (optional - blank looks on PATH)"), 0, 20);

        _specForgeField = new FieldBox { Dock = DockStyle.Fill, PlaceholderText = @"specforge.exe or SpecForge.Cli.dll" };
        _specForgeField.Inner.TextChanged += (s, e) => ScanSpecForge();

        _specForgeBrowseButton = new PillButton { Text = "Browse", Style = PillStyle.Outline, Width = 92, Dock = DockStyle.Fill };
        _specForgeBrowseButton.Click += (s, e) => BrowseForSpecForge();

        layout.Controls.Add(SplitRow(_specForgeField, _specForgeBrowseButton, 92), 0, 21);

        _specForgeStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = Theme.Ui(8.5f),
            ForeColor = Theme.TextDisabled,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(2, 6, 0, 0)
        };
        layout.Controls.Add(_specForgeStatusLabel, 0, 22);

        layout.Controls.Add(SectionTitle("Azure DevOps"), 0, 23);
        layout.Controls.Add(Caption("Organization and project for \"check PBI 1234\" objectives (optional)"), 0, 24);

        _adoOrgField = new FieldBox { Dock = DockStyle.Fill, PlaceholderText = "organization, e.g. acme", Margin = Padding.Empty };
        _adoProjectField = new FieldBox { Dock = DockStyle.Fill, PlaceholderText = "project", Margin = Padding.Empty };

        var adoRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt,
            Margin = new Padding(0, 0, 0, 6)
        };
        adoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        adoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _adoOrgField.Margin = new Padding(0, 0, 10, 0);
        adoRow.Controls.Add(_adoOrgField, 0, 0);
        adoRow.Controls.Add(_adoProjectField, 1, 0);
        layout.Controls.Add(adoRow, 0, 25);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = Theme.Ui(8.5f),
            ForeColor = Theme.TextDisabled,
            BackColor = Theme.SurfaceAlt,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(2, 6, 0, 0),
            Text = "Read-only. Needs the Claude Code CLI provider plus the az CLI (azure-devops extension) signed in " +
                   "with az login; no Azure DevOps credential is stored here. A full work-item link in the " +
                   "objective carries its own organization and project."
        }, 0, 26);

        _saveButton = new PillButton { Text = "Save", Style = PillStyle.Primary, Width = 120, Dock = DockStyle.Right };
        _saveButton.Click += (s, e) => Save();

        _cancelButton = new PillButton { Text = "Cancel", Style = PillStyle.Outline, Width = 110, Dock = DockStyle.Right };
        _cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        // Save/Cancel live outside the table, pinned to the bottom of the card, so they stay
        // reachable when the settings above them outgrow the window and scroll.
        var buttonRow = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.SurfaceAlt, Padding = new Padding(0, 10, 0, 0) };
        // Docked controls stack against their edge in reverse add order.
        buttonRow.Controls.Add(_cancelButton);
        buttonRow.Controls.Add(_saveButton);

        _layout = layout;

        // The sections stack taller than a sensible dialog once the API-key fields are shown
        // (each section added since the original two pushed it further), and squeezing rows
        // made them overlap. Scroll instead: the table keeps its natural height inside this
        // host, which grows a scrollbar only when the window is shorter than that.
        layout.Dock = DockStyle.Fill;
        _scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.SurfaceAlt };
        _scrollHost.Controls.Add(layout);
        NativeDark.UseDarkScrollBars(_scrollHost);
        UpdateScrollExtent();

        card.Controls.Add(_scrollHost);
        card.Controls.Add(buttonRow);
        Controls.Add(card);

        CancelButton = _cancelButton;
    }

    /// <summary>
    /// Sizes the scrolling area to the table's natural height: every fixed row, plus room for
    /// the solution notes (its one flexible row, which needs several lines to read). Called
    /// again whenever the provider switch collapses or restores rows.
    /// </summary>
    private void UpdateScrollExtent()
    {
        const int solutionNotesRow = 14;
        const int solutionNotesMinHeight = 130;

        var height = 0;
        for (var i = 0; i < _layout.RowStyles.Count; i++)
        {
            height += i == solutionNotesRow ? solutionNotesMinHeight : (int)_layout.RowStyles[i].Height;
        }

        _scrollHost.AutoScrollMinSize = new Size(0, height);
    }

    private static TableLayoutPanel SplitRow(Control first, Control second, int fixedWidth, bool swapWeights = false)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.SurfaceAlt,
            Margin = new Padding(0, 0, 0, 6)
        };

        if (swapWeights)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, fixedWidth));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        }
        else
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, fixedWidth + 10));
            first.Margin = new Padding(0, 0, 10, 0);
            second.Margin = Padding.Empty;
        }

        row.Controls.Add(first, 0, 0);
        row.Controls.Add(second, 1, 0);
        return row;
    }

    private static Label SectionTitle(string text) => new()
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

    // ---------------------------------------------------------------- behaviour

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        NativeDark.UseDarkTitleBar(this);
        AppIcon.ApplyTo(this);

        // A DropDownList ComboBox refuses an arbitrary height, so match the field to it
        // rather than the other way round.
        _maxTokensField.Height = _effortBox.Height;
    }

    /// <summary>
    /// The two providers need different inputs: the CLI path has no key and no
    /// effort/token knobs (those are API request parameters), so hide what does not apply
    /// rather than showing dead controls.
    /// </summary>
    private void ApplyProviderVisibility()
    {
        var usingApi = _draft.Provider == ClaudeProvider.ApiKey;

        _apiCaptionLabel.Visible = usingApi;
        _apiKeyRow.Visible = usingApi;
        _tuningCaption.Visible = usingApi;
        _tuningRow.Visible = usingApi;

        // Hiding a control does not reclaim its row: a TableLayoutPanel keeps the row at
        // its declared height regardless, which left a dead gap on the CLI path. Collapse
        // the rows to zero as well.
        _layout.RowStyles[3].Height = usingApi ? 26 : 0;   // api caption
        _layout.RowStyles[4].Height = usingApi ? 46 : 0;   // api key row
        _layout.RowStyles[8].Height = usingApi ? 26 : 0;   // tuning caption
        _layout.RowStyles[9].Height = usingApi ? 40 : 0;   // effort + tokens
        UpdateScrollExtent();

        _storagePathLabel.Text = usingApi
            ? $"The key is encrypted with Windows DPAPI for your user account and stored at{Environment.NewLine}{SettingsStore.FilePath}"
            : DescribeCli();

        PopulateModelsForProvider();
    }

    private string DescribeCli()
    {
        var path = ClaudeCliLocator.Locate(_draft.ClaudeCliPath);

        return path == null
            ? $"{ClaudeCliLocator.InstallHint}{Environment.NewLine}Nothing leaves this machine: the prompt is piped to the local CLI process."
            : $"Using the Claude Code CLI at{Environment.NewLine}{path}";
    }

    private void PopulateModelsForProvider()
    {
        if (_draft.Provider == ClaudeProvider.ClaudeCodeCli)
        {
            _modelBox.Items.Clear();
            foreach (var model in ClaudeCliCodeGenerator.AvailableModels)
                _modelBox.Items.Add(model);

            var cliIndex = ClaudeCliCodeGenerator.AvailableModels
                .ToList().FindIndex(m => m.Id == _draft.CliModel);
            _modelBox.SelectedIndex = Math.Max(0, cliIndex);
            return;
        }

        if (_modelBox.Items.Count == 0 || _modelsLoaded == false)
        {
            _modelBox.Items.Clear();
            _modelBox.Items.Add(new ClaudeModel(_draft.Model, _draft.Model));
            _modelBox.SelectedIndex = 0;
        }
    }

    private void BrowseForSolution()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the root folder of your test automation solution",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (Directory.Exists(_solutionField.Text))
            dialog.SelectedPath = _solutionField.Text;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _solutionField.Text = dialog.SelectedPath;
    }

    private void BrowseForScreenshotsFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder to save debug screenshots into",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (Directory.Exists(_screenshotsField.Text))
            dialog.SelectedPath = _screenshotsField.Text;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _screenshotsField.Text = dialog.SelectedPath;
    }

    private void BrowseForSpecForge()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select specforge.exe (or SpecForge.Cli.dll from a source build)",
            Filter = "SpecForge|specforge.exe;SpecForge.Cli.dll|All files|*.*",
            CheckFileExists = true
        };

        if (File.Exists(_specForgeField.Text))
            dialog.FileName = _specForgeField.Text;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _specForgeField.Text = dialog.FileName;
    }

    /// <summary>
    /// Reports whether SpecForge is reachable. Purely informational and never blocks Save -
    /// SpecForge is optional, and without it the step reuse check is just skipped.
    /// </summary>
    private void ScanSpecForge()
    {
        var path = _specForgeField.Text.Trim().Trim('"');
        _draft.SpecForgePath = string.IsNullOrEmpty(path) ? null : path;

        var found = SpecForgeLocator.Locate(_draft.SpecForgePath);

        if (found != null)
        {
            _specForgeStatusLabel.ForeColor = Theme.Accent;
            _specForgeStatusLabel.Text = $"Found: {found.Display}. New steps are checked for reuse after each generation.";
        }
        else if (string.IsNullOrEmpty(path))
        {
            _specForgeStatusLabel.ForeColor = Theme.TextDisabled;
            _specForgeStatusLabel.Text = "Not found on PATH - the step reuse check is skipped. Generation works without it.";
        }
        else
        {
            _specForgeStatusLabel.ForeColor = Theme.Warning;
            _specForgeStatusLabel.Text = "That file was not found, and SpecForge is not on PATH - the step reuse check is skipped.";
        }
    }

    /// <summary>
    /// Purely informational - unlike the test solution, there is nothing to read from this
    /// folder ahead of time. Just confirms the path exists (or will be created) and never
    /// blocks Save, since a not-yet-existing folder is created automatically on first capture.
    /// </summary>
    private void ScanScreenshotsFolder()
    {
        var path = _screenshotsField.Text.Trim();

        if (string.IsNullOrEmpty(path))
        {
            _draft.ScreenshotsPath = null;
            _screenshotsStatusLabel.ForeColor = Theme.TextDisabled;
            _screenshotsStatusLabel.Text = "Not set - clicks will not be captured while recording.";
            return;
        }

        _draft.ScreenshotsPath = path;
        _screenshotsStatusLabel.ForeColor = Directory.Exists(path) ? Theme.Accent : Theme.TextSecondary;
        _screenshotsStatusLabel.Text = Directory.Exists(path)
            ? "Screenshots will be saved here. Cleared automatically on a new recording, Clear, or Insert."
            : "Folder does not exist yet - it will be created the first time you record.";
    }

    /// <summary>
    /// Reads the solution and reports what the generator will be able to imitate. Runs on
    /// every keystroke, so it stays a shallow read - no build, no Roslyn workspace.
    /// </summary>
    private void ScanSolution()
    {
        var path = _solutionField.Text.Trim();

        if (string.IsNullOrEmpty(path))
        {
            _draft.TestSolutionPath = null;
            _solutionStatusLabel.ForeColor = Theme.TextDisabled;
            _solutionStatusLabel.Text = "No solution linked - generated tests will be self-contained.";
            return;
        }

        if (!Directory.Exists(path))
        {
            _solutionStatusLabel.ForeColor = Theme.Warning;
            _solutionStatusLabel.Text = "Folder not found.";
            return;
        }

        try
        {
            var profile = SolutionScanner.Scan(path);
            _draft.TestSolutionPath = path;

            if (!profile.IsUsable)
            {
                _solutionStatusLabel.ForeColor = Theme.Warning;
                _solutionStatusLabel.Text = "No tests found in that folder. " + string.Join(" ", profile.Notes);
                return;
            }

            var summary = new List<string>();

            // Lead with the output style, because it is the thing that most changes what
            // gets generated - and a Reqnroll suite producing NUnit fixtures is exactly
            // the mistake this line makes visible.
            if (profile.CanWriteGherkin)
            {
                summary.Add($"Gherkin (Reqnroll): {profile.Features.Count} feature file(s), {profile.StepBindings.Count} existing step(s) in {profile.StepDefinitionFiles.Count} file(s)");

                // Existing steps drive reuse and the Step definitions picker, so say so plainly
                // when none were recognised rather than leaving an empty list to puzzle over.
                if (profile.StepDefinitionFiles.Count == 0)
                    summary.Add("No step definition files recognised: looked for [Binding] classes with [Given]/[When]/[Then] attributes. Existing steps will not be offered for reuse.");
                summary.Add($"Features go in: {profile.FeaturesDirectory}");
                summary.Add($"Steps go in: {profile.StepDefinitionsDirectory}");
            }
            else
            {
                summary.Add("NUnit fixtures (no Reqnroll detected)");
                summary.Add($"Tests go in: {profile.TestDirectory}");
            }

            summary.Add($"Namespace: {profile.TestNamespace}");

            if (profile.BaseClassName != null)
                summary.Add($"Base class: {profile.BaseClassName}");

            if (profile.ExampleTestName != null)
                summary.Add($"Imitating: {profile.ExampleTestName}");

            if (profile.Helpers.Count > 0)
                summary.Add($"Helpers: {string.Join(", ", profile.Helpers.Take(5))}");

            _solutionStatusLabel.ForeColor = Theme.Accent;
            _solutionStatusLabel.Text = string.Join(Environment.NewLine, summary);
        }
        catch (Exception ex)
        {
            _solutionStatusLabel.ForeColor = Theme.Danger;
            _solutionStatusLabel.Text = $"Could not read that folder: {ex.Message}";
        }
    }

    private void LoadFromDraft()
    {
        _apiKeyField.Text = _draft.ApiKey ?? string.Empty;
        _solutionField.Text = _draft.TestSolutionPath ?? string.Empty;
        _screenshotsField.Text = _draft.ScreenshotsPath ?? string.Empty;
        _specForgeField.Text = _draft.SpecForgePath ?? string.Empty;
        ScanSpecForge();
        _adoOrgField.Text = _draft.AdoOrganization ?? string.Empty;
        _adoProjectField.Text = _draft.AdoProject ?? string.Empty;
        _maxTokensField.Text = _draft.MaxTokens.ToString();

        _effortBox.SelectedItem = EffortLevels.Contains(_draft.Effort) ? _draft.Effort : "high";
        _providerBox.SelectedIndex = _draft.Provider == ClaudeProvider.ApiKey ? 1 : 0;

        ApplyProviderVisibility();

        if (_draft.Provider == ClaudeProvider.ClaudeCodeCli)
        {
            SetStatus(
                ClaudeCliLocator.Locate(_draft.ClaudeCliPath) == null
                    ? "Claude Code CLI not found - install it, then test again."
                    : "CLI found. Test the connection to confirm it responds.",
                ClaudeCliLocator.Locate(_draft.ClaudeCliPath) == null ? Theme.Warning : Theme.TextSecondary);
        }
        else if (_draft.HasApiKey)
        {
            SetStatus("A key is saved. Test the connection to load available models.", Theme.TextSecondary);
        }
    }

    private async Task TestConnection()
    {
        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = new CancellationTokenSource();

        _testButton.Enabled = false;
        _testButton.Text = "Testing...";
        SetStatus("Contacting the Claude API...", Theme.TextSecondary);

        try
        {
            ConnectionResult result;

            if (_draft.Provider == ClaudeProvider.ClaudeCodeCli)
            {
                SetStatus("Starting the local Claude Code CLI...", Theme.TextSecondary);
                result = await ClaudeCliCodeGenerator.TestConnection(_draft, _testCts.Token);
            }
            else
            {
                result = await ClaudeAccount.TestConnection(_apiKeyField.Text, _testCts.Token);
            }

            if (!result.Success)
            {
                SetStatus(result.Message, Theme.Danger);
                return;
            }

            PopulateModels(result.Models);
            _modelsLoaded = true;
            SetStatus(result.Message, Theme.Accent);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Connection test cancelled.", Theme.TextSecondary);
        }
        finally
        {
            _testButton.Enabled = true;
            _testButton.Text = "Test Connection";
        }
    }

    private void PopulateModels(IReadOnlyList<ClaudeModel> models)
    {
        _modelBox.Items.Clear();
        foreach (var model in models)
            _modelBox.Items.Add(model);

        // Keep the current selection if the account still offers it, otherwise fall back
        // to the default model, and only then to whatever is first.
        var index = models.ToList().FindIndex(m => m.Id == _draft.Model);
        if (index < 0)
            index = models.ToList().FindIndex(m => m.Id == AppSettings.DefaultModel);

        _modelBox.SelectedIndex = Math.Max(0, index);
    }

    private void Save()
    {
        if (_draft.Provider == ClaudeProvider.ApiKey)
        {
            var key = _apiKeyField.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                SetStatus("Enter an API key, or press Cancel to leave settings unchanged.", Theme.Warning);
                return;
            }

            if (!int.TryParse(_maxTokensField.Text.Trim(), out var maxTokens) || maxTokens < 1024 || maxTokens > 64000)
            {
                SetStatus("Output limit must be a number between 1024 and 64000.", Theme.Warning);
                return;
            }

            _draft.ApiKey = key;
            _draft.MaxTokens = maxTokens;

            if (_effortBox.SelectedItem is string effort)
                _draft.Effort = effort;
        }

        if (_modelBox.SelectedItem is ClaudeModel model)
        {
            if (_draft.Provider == ClaudeProvider.ClaudeCodeCli)
                _draft.CliModel = model.Id;
            else
                _draft.Model = model.Id;
        }

        var solutionPath = _solutionField.Text.Trim();
        _draft.TestSolutionPath = string.IsNullOrEmpty(solutionPath) ? null : solutionPath;

        var screenshotsPath = _screenshotsField.Text.Trim();
        _draft.ScreenshotsPath = string.IsNullOrEmpty(screenshotsPath) ? null : screenshotsPath;

        var specForgePath = _specForgeField.Text.Trim().Trim('"');
        _draft.SpecForgePath = string.IsNullOrEmpty(specForgePath) ? null : specForgePath;

        var adoOrganization = _adoOrgField.Text.Trim();
        _draft.AdoOrganization = string.IsNullOrEmpty(adoOrganization) ? null : adoOrganization;

        var adoProject = _adoProjectField.Text.Trim();
        _draft.AdoProject = string.IsNullOrEmpty(adoProject) ? null : adoProject;

        try
        {
            SettingsStore.Save(_draft);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not save settings: {ex.Message}", Theme.Danger);
            return;
        }

        if (!_modelsLoaded)
            Console.WriteLine("Settings saved without a verified connection; the key is untested.");

        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _testCts?.Dispose();

        base.Dispose(disposing);
    }
}
