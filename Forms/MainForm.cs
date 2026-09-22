using Microsoft.Extensions.Configuration;
using PlaywrightAgentAI.Agents;
using PlaywrightAgentAI.Models;
using PlaywrightAgentAI.Services;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing.Drawing2D;

namespace PlaywrightAgentAI.Forms;

public partial class MainForm : Form
{
    private readonly ExplorationAgent _agent;
    private CancellationTokenSource? _cancellationTokenSource;
    private Label? _statusLabel;

    public MainForm()
    {
        InitializeComponent();
        ApplyModernTheme();
        SetupUI();
        _agent = InitializeAgent();
    }

    private void ApplyModernTheme()
    {
        BackColor = Color.FromArgb(18, 18, 18);
        ForeColor = Color.White;
        DoubleBuffered = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_COMPOSITED = 0x02000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_COMPOSITED;
            return cp;
        }
    }

    private Panel CreateRoundedPanel(int radius)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30),
            Margin = new Padding(10),
            Padding = new Padding(10)
        };

        panel.Paint += (s, e) =>
        {
            var rect = panel.ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using var path = new GraphicsPath();
            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            panel.Region = new Region(path);
        };

        return panel;
    }

    private Button CreateModernButton(string name, string text, Color backColor)
    {
        var btn = new Button
        {
            Name = name,
            Text = text,
            Width = 220,
            Height = 36,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = backColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 15, 0, 15),
            AutoSize = false
        };

        btn.FlatAppearance.BorderSize = 0;

        btn.Paint += (s, e) =>
        {
            var rect = btn.ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            using var path = new GraphicsPath();

            int radius = 18;
            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            btn.Region = new Region(path);
        };

        return btn;
    }

    private ExplorationAgent InitializeAgent()
    {
        try
        {
            var builder = new ConfigurationBuilder();
            if (File.Exists("appsettings.json"))
            {
                builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            }
            builder.AddEnvironmentVariables();
            var config = builder.Build();

            AICodeGenerator? aiGenerator = null;
            if (!string.IsNullOrWhiteSpace(config["OpenAI:ApiKey"]))
            {
                try
                {
                    aiGenerator = new AICodeGenerator(config);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Warning: AI not available: {ex.Message}", "AI Initialization", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            return new ExplorationAgent(aiGenerator);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing agent: {ex.Message}", "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            throw;
        }
    }

    private void SetupUI()
    {
        Text = "Playwright Test Generator";
        Size = new Size(1400, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10)
        };

        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        mainPanel.Controls.Add(CreateLeftPanel(), 0, 0);
        mainPanel.Controls.Add(CreateRightPanel(), 1, 0);

        Controls.Add(mainPanel);
    }

    private Panel CreateLeftPanel()
    {
        var panel = CreateRoundedPanel(20);

        // Use a TableLayoutPanel to allow children to stretch horizontally and to enable
        // one control (outputTextBox) to take the remaining vertical space.
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = false, // disable outer auto-scroll so inner textboxes show their own scrollbars
            Padding = new Padding(10),
            BackColor = Color.FromArgb(30, 30, 30)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Helper to add rows and configure child docking/spacing.
        void AddRow(Control ctrl, SizeType rowType = SizeType.AutoSize, float height = 0f, Padding? margin = null)
        {
            int rowIndex = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(rowType, height));

            ctrl.Margin = margin ?? new Padding(0, 0, 0, 10);

            if (rowType == SizeType.Percent)
            {
                ctrl.Dock = DockStyle.Fill;
            }
            else if (ctrl is Button)
            {
                // Keep buttons centered and at their designed size.
                ctrl.Dock = DockStyle.None;
                ctrl.Anchor = AnchorStyles.None;
            }
            else
            {
                ctrl.Dock = DockStyle.Top;
                ctrl.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            }

            layout.Controls.Add(ctrl, 0, rowIndex);
        }

        var title = new Label
        {
            Text = "Test Generation",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        AddRow(title);

        var urlLabel = new Label
        {
            Text = "URL:",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 200, 200),
            AutoSize = true
        };
        AddRow(urlLabel);

        var urlTextBox = new TextBox
        {
            Name = "urlTextBox",
            Height = 35,
            Font = new Font("Segoe UI", 10),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Enter the URL to test...",
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.White
        };
        // allow horizontal stretching
        urlTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        AddRow(urlTextBox);

        var objectiveLabel = new Label
        {
            Text = "Test Objective:",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 200, 200),
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        AddRow(objectiveLabel);

        var objectiveTextBox = new RoundedScrollTextEditor
        {
            Name = "objectiveTextBox",
            Height = 120,
            PlaceholderText = "Describe the test objective...",
            PlaceholderColor = Color.FromArgb(150, 150, 150)
        };
        objectiveTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        AddRow(objectiveTextBox);

        // New: Gherkin option checkbox and keyword combobox
        var gherkinPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 5, 0, 10)
        };

        var gherkinCheckBox = new CheckBox
        {
            Name = "gherkinCheckBox",
            Text = "Include Gherkin output",
            ForeColor = Color.FromArgb(200, 200, 200),
            AutoSize = true,
            Checked = false
        };
        gherkinPanel.Controls.Add(gherkinCheckBox);

        var gherkinKeywordComboBox = new ComboBox
        {
            Name = "gherkinKeywordComboBox",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 110
        };
        gherkinKeywordComboBox.Items.AddRange(new object[] { "Given", "When", "Then", "And", "But" });
        gherkinKeywordComboBox.SelectedIndex = 0;
        gherkinPanel.Controls.Add(gherkinKeywordComboBox);

        AddRow(gherkinPanel);

        var generateButton = CreateModernButton("generateButton", "Generate Test", Color.FromArgb(29, 185, 84));
        // start disabled until objective has text
        generateButton.Enabled = false;
        
        generateButton.Click += (s, e) =>
        {
            var url = urlTextBox.Text.Trim();
            var objective = objectiveTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Please enter a URL.", "Missing URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(objective))
            {
                MessageBox.Show("Please enter a test objective.", "Missing Objective", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            GenerateTestAsync(url, objective);
        };
        // let button stretch horizontally when the layout allows, but keep smaller default size
        generateButton.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        AddRow(generateButton);

        _statusLabel = new Label
        {
            Name = "statusLabel",
            Text = "Ready",
            ForeColor = Color.FromArgb(180, 180, 180),
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 0),
            Font = new Font("Segoe UI", 9, FontStyle.Italic)
        };
        AddRow(_statusLabel);

        var generatedLabel = new Label
        {
            Text = "Generated Test Code:",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 200, 200),
            AutoSize = true,
            Margin = new Padding(0, 15, 0, 5)
        };
        AddRow(generatedLabel);

        var outputTextBox = new RoundedScrollTextEditor
        {
            Name = "outputTextBox",
            Height = 200,
            Font = new Font("Courier New", 9),
            ReadOnly = true,
            WordWrap = false,
            BackColor = Color.FromArgb(25, 25, 25),
            ForeColor = Color.FromArgb(220, 220, 220),            
        };
        // Ensure output textbox fills its percent row and exposes scrollbars
        try
        {
            outputTextBox.GetType().GetProperty("ScrollBars")?.SetValue(outputTextBox, ScrollBars.Both);
        }
        catch
        {
            // If RoundedScrollTextEditor doesn't expose ScrollBars, ignore the reflection attempt.
        }
        AddRow(outputTextBox, SizeType.Percent, 60f, new Padding(0, 0, 0, 10));

        var copyButton = CreateModernButton("copyButton", "Copy Test Code", Color.FromArgb(50, 168, 82));
        
        // start disabled until output has text
        copyButton.Enabled = false;
        copyButton.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(outputTextBox.Text))
            {
                Clipboard.SetText(outputTextBox.Text);
                MessageBox.Show("Code copied to clipboard!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("No code to copy. Generate a test first.", "Empty Output", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        copyButton.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        AddRow(copyButton);

        // New: Gherkin output label and box
        var gherkinLabel = new Label
        {
            Text = "Generated Gherkin:",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 200, 200),
            AutoSize = true,
            Margin = new Padding(0, 15, 0, 5)
        };
        AddRow(gherkinLabel);

        var gherkinOutputTextBox = new RoundedScrollTextEditor
        {
            Name = "gherkinOutputTextBox",
            Height = 120,
            Font = new Font("Courier New", 9),
            ReadOnly = true,
            WordWrap = false,
            BackColor = Color.FromArgb(25, 25, 25),
            ForeColor = Color.FromArgb(220, 220, 220),            
        };
        try
        {
            gherkinOutputTextBox.GetType().GetProperty("ScrollBars")?.SetValue(gherkinOutputTextBox, ScrollBars.Both);
        }
        catch
        {
        }
        AddRow(gherkinOutputTextBox, SizeType.Percent, 35f, new Padding(0, 0, 0, 10));

        var copyGherkinButton = CreateModernButton("copyGherkinButton", "Copy Gherkin", Color.FromArgb(80, 140, 220));
        copyGherkinButton.Enabled = false;
        copyGherkinButton.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(gherkinOutputTextBox.Text))
            {
                Clipboard.SetText(gherkinOutputTextBox.Text);
                MessageBox.Show("Gherkin copied to clipboard!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("No Gherkin to copy. Generate a test first.", "Empty Output", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        copyGherkinButton.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        AddRow(copyGherkinButton);

        // Wire up text-change logic to enable/disable buttons
        objectiveTextBox.TextChanged += (_, _) =>
        {
            generateButton.Enabled = !string.IsNullOrWhiteSpace(objectiveTextBox.Text);
        };

        outputTextBox.TextChanged += (_, _) =>
        {
            copyButton.Enabled = !string.IsNullOrWhiteSpace(outputTextBox.Text);
        };

        gherkinOutputTextBox.TextChanged += (_, _) =>
        {
            copyGherkinButton.Enabled = !string.IsNullOrWhiteSpace(gherkinOutputTextBox.Text);
        };

        // Ensure initial state respects existing text (if any)
        generateButton.Enabled = !string.IsNullOrWhiteSpace(objectiveTextBox.Text);
        copyButton.Enabled = !string.IsNullOrWhiteSpace(outputTextBox.Text);
        copyGherkinButton.Enabled = !string.IsNullOrWhiteSpace(gherkinOutputTextBox.Text);

        panel.Controls.Add(layout);
        return panel;
    }

    private Panel CreateRightPanel()
    {
        var panel = CreateRoundedPanel(20);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(30, 30, 30)
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "Web Preview",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(10, 10, 10, 10)
        }, 0, 0);

        var webBrowser = new WebView2
        {
            Name = "webBrowser",
            Dock = DockStyle.Fill
        };

        layout.Controls.Add(webBrowser, 0, 1);
        panel.Controls.Add(layout);

        return panel;
    }

    private async Task EnsureWebViewReady(WebView2 browser)
    {
        if (browser.CoreWebView2 == null)
            await browser.EnsureCoreWebView2Async();
    }

    private async void GenerateTestAsync(string url, string testObjective)
    {
        try
        {
            var generateButton = FindControl("generateButton") as Button;
            generateButton!.Enabled = false;

            _statusLabel!.Text = "Generating test...";
            _statusLabel.ForeColor = Color.FromArgb(29, 185, 84);

            _cancellationTokenSource = new CancellationTokenSource();

            var gherkinCheckBox = FindControl("gherkinCheckBox") as CheckBox;
            var gherkinKeywordCombo = FindControl("gherkinKeywordComboBox") as ComboBox;

            var request = new ExplorationRequest
            {
                Url = url,
                TestObjective = testObjective,
                IncludeGherkin = gherkinCheckBox?.Checked ?? false,
                GherkinKeyword = gherkinKeywordCombo?.SelectedItem?.ToString() ?? "Given"
            };

            var result = await _agent.Run(request);

            var outputTextBox = FindControl("outputTextBox") as RoundedScrollTextEditor;
            if (outputTextBox != null)
            {
                outputTextBox.Text = result.GeneratedCode;
                // ensure caret stays at top
                //outputTextBox.SelectionStart = 0;
                //outputTextBox.SelectionLength = 0;
            }

            var gherkinOutputTextBox = FindControl("gherkinOutputTextBox") as RoundedScrollTextEditor;
            if (gherkinOutputTextBox != null)
            {
                gherkinOutputTextBox.Text = result.GherkinOutput;
                //gherkinOutputTextBox.SelectionStart = 0;
                //gherkinOutputTextBox.SelectionLength = 0;
            }

            var webBrowser = FindControl("webBrowser") as WebView2;

            if (webBrowser != null)
            {
                await EnsureWebViewReady(webBrowser);
                webBrowser.CoreWebView2.Navigate(url);
            }

            _statusLabel.Text = "Test generated successfully!";
            _statusLabel.ForeColor = Color.FromArgb(29, 185, 84);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error generating test: {ex.Message}", "Generation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel!.Text = $"Error: {ex.Message}";
            _statusLabel.ForeColor = Color.Red;
        }
        finally
        {
            var generateButton = FindControl("generateButton") as Button;
            generateButton!.Enabled = true;

            _cancellationTokenSource?.Dispose();
        }
    }

    private Control? FindControl(string name)
    {
        return Controls.Find(name, true).FirstOrDefault();
    }
}