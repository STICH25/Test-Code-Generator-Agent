using PlaywrightAgentAI.Models;
using PlaywrightAgentAI.Services;
using PlaywrightAgentAI.Tools;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightAgentAI.Agents;

public class ExplorationAgent
{
    private readonly DomExplorer _explorer = new();
    private readonly HtmlAnalyzer _analyzer = new();
    private readonly TestCodeBuilder _builder = new();
    private readonly ITestCodeGenerator? _aiGenerator;
    private readonly SolutionProfile? _profile;

    public ExplorationAgent(ITestCodeGenerator? aiGenerator = null, SolutionProfile? profile = null)
    {
        _aiGenerator = aiGenerator;
        _profile = profile;
    }

    public async Task<AgentResult> Run(ExplorationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.Url))
            throw new ArgumentException("Request URL cannot be null or empty.", nameof(request));

        var result = new AgentResult();

        try
        {
            Console.WriteLine($"Analyzing {request.Url}...");
            var html = await _explorer.CaptureDom(request.Url, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Pass TestObjective so analyzer can locate the exact section
            var dom = await _analyzer.Analyze(html, request.TestObjective);

            Console.WriteLine($"Detected {dom.Headings.Count} headings, {dom.Elements.Count} sections, {dom.Buttons.Count} buttons");

            if (dom.SectionHtml.Count > 0)
            {
                Console.WriteLine($"Found {dom.SectionHtml.Count} section(s): {string.Join(", ", dom.SectionHtml.Keys)}");
            }
            else
            {
                result.Warnings.Add("No page sections could be extracted; the prompt will only contain element lists.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_aiGenerator == null)
            {
                Console.WriteLine("Using basic template (AI generator is not configured)");
                result.Warnings.Add("AI generator is not configured, so a static template was produced instead of a real test.");
                result.GeneratedCode = _builder.Build(request.Url, dom);
                return result;
            }

            if (string.IsNullOrWhiteSpace(request.TestObjective) && request.RecordedActions.Count == 0)
            {
                Console.WriteLine("Using basic template (no test objective supplied)");
                result.Warnings.Add("No test objective was supplied, so a static template was produced instead of a real test.");
                result.GeneratedCode = _builder.Build(request.Url, dom);
                return result;
            }

            try
            {
                Console.WriteLine(_profile is { IsUsable: true }
                    ? $"Generating test in the house style of {_profile.Describe()}..."
                    : "Generating test (no solution linked - self-contained style)...");
                var promptBuilder = new PromptBuilder();
                var userRequest = new UserPromptRequest
                {
                    Url = request.Url,
                    // Optional when steps were recorded: the steps are the specification.
                    Action = request.TestObjective ?? string.Empty
                };

                if (request.RecordedActions.Count > 0)
                    Console.WriteLine($"Including {request.RecordedActions.Count} recorded step(s) in the prompt.");

                var targetFeature = ResolveTargetFeature(request.TargetFeaturePath);
                if (targetFeature != null)
                    Console.WriteLine($"Appending a scenario to {targetFeature.FileName}.");

                var prompt = promptBuilder.Build(userRequest, dom, _profile, request.RecordedActions, targetFeature);
                var code = await _aiGenerator.Generate(prompt, cancellationToken);

                if (string.IsNullOrWhiteSpace(code))
                {
                    Console.Error.WriteLine("Warning: AI generated empty code. Falling back to basic template.");
                    result.Warnings.Add("The AI returned an empty response; fell back to the static template.");
                    result.GeneratedCode = _builder.Build(request.Url, dom);
                    return result;
                }

                result.UsedAi = true;
                result.GeneratedCode = code;
                result.Artifacts = ArtifactParser.Parse(code);

                DescribeArtifacts(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: AI code generation failed: {ex.Message}");
                Console.Error.WriteLine("Falling back to basic template...");
                result.Warnings.Add($"AI code generation failed ({ex.Message}); fell back to the static template.");
                result.GeneratedCode = _builder.Build(request.Url, dom);
                return result;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Run cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Agent execution failed: {ex.Message}");
            throw;
        }
    }

    private FeatureFile? ResolveTargetFeature(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _profile == null)
            return null;

        return _profile.Features.FirstOrDefault(
            f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A Gherkin run must produce both a feature and its bindings. Saying so plainly
    /// beats silently writing half a change the user then has to debug.
    /// </summary>
    private void DescribeArtifacts(AgentResult result)
    {
        foreach (var artifact in result.Artifacts)
            Console.WriteLine($"Produced {artifact.Kind}: {artifact.FileName}");

        if (_profile is not { CanWriteGherkin: true })
            return;

        if (!result.Artifacts.Any(a => a.Kind == ArtifactKind.Feature))
            result.Warnings.Add("No .feature file was produced - the model did not follow the Gherkin contract.");

        if (!result.Artifacts.Any(a => a.Kind == ArtifactKind.StepDefinitions))
            result.Warnings.Add("No step definitions file was produced; the scenario may have no bindings.");
    }
}
