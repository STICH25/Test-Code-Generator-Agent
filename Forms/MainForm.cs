using Microsoft.Extensions.Configuration;
using PlaywrightAgentAI.Agents;
using PlaywrightAgentAI.Models;
using PlaywrightAgentAI.Services;
using Microsoft.Web.WebView2.WinForms;

namespace PlaywrightAgentAI.Forms;

public partial class MainForm : Form
{
    private readonly ExplorationAgent _agent;
    private CancellationTokenSource? _cancellationTokenSource;
    private Label? _statusLabel;

    public MainForm()
    {
        InitializeComponent();
        SetupUI();
        _agent = InitializeAgent();
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
        BackColor = Color.White;

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
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
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoScroll = true,
            Padding = new Padding(10),
            WrapContents = false
        };

        layout.Controls.Add(new Label
        {
            Text = "Test Generation",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        });

        layout.Controls.Add(new Label
        {
            Text = "URL:",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            AutoSize = true
        });

        var urlTextBox = new TextBox
        {
            Name = "urlTextBox",
            Width = 550,
            Height = 35,
            Font = new Font("Segoe UI", 10),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Enter the URL to test..."
        };
        layout.Controls.Add(urlTextBox);

        layout.Controls.Add(new Label
        {
            Text = "Test Objective:",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        });

        var objectiveTextBox = new TextBox
        {
            Name = "objectiveTextBox",
            Width = 550,
            Height = 80,
            Multiline = true,
            Font = new Font("Segoe UI", 10),
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
            AcceptsTab = true,
            AcceptsReturn = true,
            PlaceholderText = "Describe the test objective..."
        };
        layout.Controls.Add(objectiveTextBox);

        var generateButton = new Button
        {
            Name = "generateButton",
            Text = "Generate Test",
            Width = 550,
            Height = 40,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 10, 0, 0)
        };

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

        layout.Controls.Add(generateButton);

        _statusLabel = new Label
        {
            Name = "statusLabel",
            Text = "Ready",
            ForeColor = Color.Black,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 0),
            Font = new Font("Segoe UI", 9, FontStyle.Italic)
        };
        layout.Controls.Add(_statusLabel);

        layout.Controls.Add(new Label
        {
            Text = "Generated Test Code:",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 15, 0, 5)
        });

        var outputTextBox = new TextBox
        {
            Name = "outputTextBox",
            Width = 550,
            Height = 280,
            Multiline = true,
            Font = new Font("Courier New", 9),
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Both,
            ReadOnly = true,
            WordWrap = false
        };
        layout.Controls.Add(outputTextBox);

        var copyButton = new Button
        {
            Name = "copyButton",
            Text = "Copy to Clipboard",
            Width = 550,
            Height = 35,
            Font = new Font("Segoe UI", 10),
            BackColor = Color.FromArgb(50, 168, 82),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 5, 0, 0)
        };

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

        layout.Controls.Add(copyButton);

        panel.Controls.Add(layout);
        return panel;
    }

    private Panel CreateRightPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "Web Preview",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
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
            _statusLabel.ForeColor = Color.Blue;

            _cancellationTokenSource = new CancellationTokenSource();

            var request = new ExplorationRequest
            {
                Url = url,
                TestObjective = testObjective
            };

            var result = await _agent.Run(request);

            var outputTextBox = FindControl("outputTextBox") as TextBox;
            outputTextBox!.Text = result.GeneratedCode;

            var webBrowser = FindControl("webBrowser") as WebView2;

            if (webBrowser != null)
            {
                await EnsureWebViewReady(webBrowser);
                webBrowser.CoreWebView2.Navigate(url);
            }

            _statusLabel.Text = "Test generated successfully!";
            _statusLabel.ForeColor = Color.Green;
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
