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
                    var code = await _aiGenerator.Generate(prompt);

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        Console.Error.WriteLine("Warning: AI generated empty code. Falling back to basic template.");
                        return new AgentResult { GeneratedCode = _builder.Build(request.Url, dom) };
                    }

                    return new AgentResult { GeneratedCode = code };
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: AI code generation failed: {ex.Message}");
                    Console.Error.WriteLine("Falling back to basic template...");
                    return new AgentResult { GeneratedCode = _builder.Build(request.Url, dom) };
                }
            }

            Console.WriteLine("Using basic template (AI not available or no test objective)");
            var basicCode = _builder.Build(request.Url, dom);

            return new AgentResult { GeneratedCode = basicCode };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Agent execution failed: {ex.Message}");
            throw;
        }
    }
}