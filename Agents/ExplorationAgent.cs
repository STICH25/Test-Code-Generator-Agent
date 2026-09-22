using PlaywrightAgentAI.Models;
using PlaywrightAgentAI.Services;
using PlaywrightAgentAI.Tools;
using System;
using System.Threading.Tasks;

namespace PlaywrightAgentAI.Agents;

public class ExplorationAgent
{
    private readonly DomExplorer _explorer = new();
    private readonly HtmlAnalyzer _analyzer = new();
    private readonly TestCodeBuilder _builder = new();
    private readonly AICodeGenerator? _aiGenerator;
    private readonly GherkinBuilder _gherkinBuilder = new();

    private const string PlaywrightMarker = "<<<PLAYWRIGHT>>>";
    private const string GherkinMarker = "<<<GHERKIN>>>";

    public ExplorationAgent(AICodeGenerator? aiGenerator = null)
    {
        _aiGenerator = aiGenerator;
    }

    public async Task<AgentResult> Run(ExplorationRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.Url))
            throw new ArgumentException("Request URL cannot be null or empty.", nameof(request));

        try
        {
            Console.WriteLine($"Analyzing {request.Url}...");
            var html = await _explorer.CaptureDom(request.Url);

            // Pass TestObjective so analyzer can locate the exact section
            var dom = await _analyzer.Analyze(html, request.TestObjective);

            Console.WriteLine($"Detected {dom.Headings.Count} headings, {dom.Elements.Count} sections, {dom.Buttons.Count} buttons");

            if (dom.SectionHtml.Count > 0)
            {
                Console.WriteLine($"Found {dom.SectionHtml.Count} section(s): {string.Join(", ", dom.SectionHtml.Keys)}");
            }

            string generatedCode;
            string gherkinOutput = string.Empty;

            if (_aiGenerator != null && !string.IsNullOrWhiteSpace(request.TestObjective))
            {
                try
                {
                    Console.WriteLine("Generating test with AI...");
                    var promptBuilder = new PromptBuilder();
                    var userRequest = new UserPromptRequest
                    {
                        Url = request.Url,
                        Action = request.TestObjective
                    };

                    var prompt = promptBuilder.Build(userRequest, dom);
                    var aiResponse = await _aiGenerator.Generate(prompt);

                    // Parse AI response using strict markers.
                    if (!string.IsNullOrWhiteSpace(aiResponse) &&
                        aiResponse.Contains(PlaywrightMarker) &&
                        aiResponse.Contains(GherkinMarker))
                    {
                        var pStart = aiResponse.IndexOf(PlaywrightMarker, StringComparison.Ordinal) + PlaywrightMarker.Length;
                        var gStart = aiResponse.IndexOf(GherkinMarker, StringComparison.Ordinal);

                        if (gStart > pStart)
                        {
                            generatedCode = aiResponse.Substring(pStart, gStart - pStart).Trim();
                            gherkinOutput = aiResponse.Substring(gStart + GherkinMarker.Length).Trim();
                        }
                        else
                        {
                            // Unexpected order — fall back to full response as code
                            generatedCode = aiResponse.Trim();
                        }
                    }
                    else
                    {
                        // AI didn't follow markers; treat full response as Playwright code
                        generatedCode = string.IsNullOrWhiteSpace(aiResponse) ? _builder.Build(request.Url, dom) : aiResponse;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: AI code generation failed: {ex.Message}");
                    Console.Error.WriteLine("Falling back to basic template...");
                    generatedCode = _builder.Build(request.Url, dom);
                }
            }
            else
            {
                Console.WriteLine("Using basic template (AI not available or no test objective)");
                generatedCode = _builder.Build(request.Url, dom);
            }

            // If Gherkin was requested and AI did not provide it, build locally as a fallback.
            if (request.IncludeGherkin && string.IsNullOrWhiteSpace(gherkinOutput) && !string.IsNullOrWhiteSpace(request.TestObjective))
            {
                try
                {
                    var (featureStep, stepDefinition) = _gherkinBuilder.Build(request.TestObjective, request.GherkinKeyword ?? "Given");
                    gherkinOutput = $"# Feature Step\n{featureStep}\n\n# Step Definition\n{stepDefinition}";
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to build local Gherkin: {ex.Message}");
                }
            }

            return new AgentResult
            {
                GeneratedCode = generatedCode,
                GherkinOutput = gherkinOutput
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Agent execution failed: {ex.Message}");
            throw;
        }
    }
}