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
    private readonly SpecForgeCommand? _specForge;

    public ExplorationAgent(
        ITestCodeGenerator? aiGenerator = null,
        SolutionProfile? profile = null,
        SpecForgeCommand? specForge = null)
    {
        _aiGenerator = aiGenerator;
        _profile = profile;
        _specForge = specForge;
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
            await using var capture = await _explorer.Capture(request.Url, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Pass TestObjective so analyzer can locate the exact section
            var dom = await _analyzer.Analyze(capture.Html, request.TestObjective);

            Console.WriteLine($"Detected {dom.Headings.Count} headings, {dom.Elements.Count} sections, {dom.Buttons.Count} buttons");

            if (dom.SectionHtml.Count > 0)
            {
                Console.WriteLine($"Found {dom.SectionHtml.Count} section(s): {string.Join(", ", dom.SectionHtml.Keys)}");
            }
            else
            {
                result.Warnings.Add("No page sections could be extracted; the prompt will only contain element lists.");
            }

            // A recording captures its own screenshot per action as it happens - only a
            // typed-objective run (no recorded actions) needs one taken here, of whichever
            // section actually matched, so the user can confirm the right part of the page
            // was tested.
            if (request.RecordedActions.Count == 0 && !string.IsNullOrWhiteSpace(request.ScreenshotsPath))
                await CaptureSectionScreenshot(capture, dom, request.ScreenshotsPath, result);

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

                var targetPageObject = ResolveTargetPageObject(request.TargetPageObjectPath);
                if (targetPageObject != null)
                    Console.WriteLine($"Extending page object {targetPageObject.FileName}.");

                var targetStepDefinitions = ResolveTargetStepDefinitions(request.TargetStepDefinitionsPath);
                if (targetStepDefinitions != null)
                    Console.WriteLine($"Appending bindings to {targetStepDefinitions.FileName}.");

                result.Pbi = await LookUpPbi(request, cancellationToken);

                var prompt = promptBuilder.Build(
                    userRequest, dom, _profile, request.RecordedActions,
                    targetFeature, targetPageObject, targetStepDefinitions, result.Pbi);
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

                // Stamp the explicit target back onto the matching artifact by identity
                // (the full path), rather than trusting the model to echo the filename
                // back exactly. SolutionWriter overwrites unconditionally wherever
                // TargetPath is set, so this is what makes "insert into this exact file"
                // reliable even if the model's FILE: name differs slightly.
                ApplyTarget(result, ArtifactKind.Feature, targetFeature?.Path);
                ApplyTarget(result, ArtifactKind.PageObject, targetPageObject?.Path);
                ApplyTarget(result, ArtifactKind.StepDefinitions, targetStepDefinitions?.Path);

                DescribeArtifacts(result);
                await RunReuseCheck(result, cancellationToken);
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

                if (_aiGenerator is ClaudeCliCodeGenerator cli && ClaudeCliCodeGenerator.IsAuthenticationFailure(ex.Message))
                    TryOpenSignInTerminal(cli, result);

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

    /// <summary>
    /// When the objective names a PBI, reads it (and its linked Test Cases) from Azure DevOps so
    /// the scenarios can come from the real test steps. Optional context: every way this can
    /// not happen - no PBI named, no CLI provider, no az, not logged in, a timeout - is a log
    /// line and a null, and generation proceeds exactly as it would have without it. Like the
    /// reuse check it must not throw into the surrounding AI-call handler.
    /// </summary>
    private async Task<PbiContext?> LookUpPbi(ExplorationRequest request, CancellationToken cancellationToken)
    {
        if (request.RecordedActions.Count > 0)
            return null;

        var reference = PbiReferenceFinder.Find(request.TestObjective);
        if (reference == null)
            return null;

        if (_aiGenerator is not ClaudeCliCodeGenerator cli)
        {
            Console.WriteLine($"PBI {reference.Id} noticed in the objective, but reading it from Azure DevOps needs the Claude Code CLI provider; continuing without it.");
            return null;
        }

        var organization = reference.Organization != null
            ? PbiReferenceFinder.NormalizeOrganization(reference.Organization)
            : PbiReferenceFinder.NormalizeOrganization(request.AdoOrganization);
        var project = reference.Project ?? (string.IsNullOrWhiteSpace(request.AdoProject) ? null : request.AdoProject.Trim());

        Console.WriteLine($"Reading PBI {reference.Id} from Azure DevOps (read-only, via the az CLI and your skills)...");

        try
        {
            var lookup = await AdoPbiLookup.Fetch(cli, reference, organization, project, _profile?.RootPath, cancellationToken);

            foreach (var problem in lookup.Problems)
                Console.Error.WriteLine($"Azure DevOps: {problem}");

            // The user's skills are meant to be the authority on how Azure DevOps is reached,
            // so say plainly which were used - and when none were, that the built-in recipe was.
            if (lookup.SkillsConsulted is { Count: > 0 })
                Console.WriteLine($"Azure DevOps: consulted skill(s) {string.Join(", ", lookup.SkillsConsulted)}.");
            else
                Console.WriteLine("Azure DevOps: no skill was consulted; the built-in az recipe was used instead.");

            if (lookup.Context == null)
                return null;

            Console.WriteLine($"Azure DevOps: {lookup.Context.Summary}.");

            if (lookup.Context.TestCases.Count == 0)
                Console.WriteLine("Azure DevOps: no Test Cases are linked, so scenarios will come from the acceptance criteria. Creating Test Cases in ADO first gives better coverage.");

            return lookup.Context;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Azure DevOps lookup failed ({ex.Message}); generating without it.");
            return null;
        }
    }

    /// <summary>
    /// Asks SpecForge which of the new steps reuse existing bindings. Strictly advisory: it
    /// runs after the artifacts are already in hand, and any failure here is reported as a
    /// log line and swallowed - it must never turn a good generation into the static-template
    /// fallback, which is what an exception reaching the surrounding AI-call handler would do.
    /// </summary>
    private async Task RunReuseCheck(AgentResult result, CancellationToken cancellationToken)
    {
        if (_profile is not { CanWriteGherkin: true } || !result.Artifacts.Any(a => a.Kind == ArtifactKind.Feature))
            return;

        if (_specForge == null)
        {
            Console.WriteLine("Step reuse check skipped (SpecForge not found - optional, see Settings).");
            return;
        }

        try
        {
            result.ReuseReport = await SpecForgeReuseCheck.Run(_specForge, _profile, result.Artifacts, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Step reuse check failed ({ex.Message}); generation is unaffected.");
        }
    }

    /// <summary>
    /// Screenshots the section the objective matched (or the viewport, if none did) into the
    /// configured debug folder, and records it on the result so the UI can show it.
    /// </summary>
    private static async Task CaptureSectionScreenshot(DomCapture capture, DomSnapshot dom, string screenshotsFolder, AgentResult result)
    {
        try
        {
            ScreenshotCapture.ClearFolder(screenshotsFolder);

            // The matched key IS the heading's own text (trimmed, lowercased) - passed
            // straight through rather than via SectionSelectors, whose selector for an
            // unclassed container is just a tag name and can match more than one element.
            var bytes = await capture.ScreenshotSectionAsync(dom.MatchedSectionKey);
            if (bytes == null)
                return;

            var path = Path.Combine(screenshotsFolder, "001.png");
            Directory.CreateDirectory(screenshotsFolder);
            await File.WriteAllBytesAsync(path, bytes);

            var description = dom.MatchedSectionKey != null
                ? $"Matched section: {dom.MatchedSectionKey}"
                : "Full page (no specific section matched)";

            result.Screenshots.Add(new ScreenshotEntry { Index = 1, FilePath = path, Description = description });
            Console.WriteLine($"Captured a screenshot of the tested area ({description}).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not capture a screenshot of the tested area: {ex.Message}");
        }
    }

    private FeatureFile? ResolveTargetFeature(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _profile == null)
            return null;

        return _profile.Features.FirstOrDefault(
            f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private PageObjectFile? ResolveTargetPageObject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _profile == null)
            return null;

        return _profile.PageObjects.FirstOrDefault(
            p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private StepDefinitionFile? ResolveTargetStepDefinitions(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _profile == null)
            return null;

        return _profile.StepDefinitionFiles.FirstOrDefault(
            f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The CLI session expired, so hand the login flow to the user directly rather than
    /// asking them to go find a terminal themselves: opens a real console window running
    /// `claude` interactively, which prompts its own sign-in (normally a browser OAuth
    /// flow). This app never touches or stores Claude credentials, so this is the full
    /// extent of what it can automate here.
    /// </summary>
    private static void TryOpenSignInTerminal(ClaudeCliCodeGenerator cli, AgentResult result)
    {
        try
        {
            Console.WriteLine("Opening a terminal so you can sign in to Claude Code...");
            cli.OpenSignInTerminal();
            result.Warnings.Add("Opened a terminal for you to sign in to Claude Code. Once you're signed in, retry.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not open a sign-in terminal: {ex.Message}");
        }
    }

    private static void ApplyTarget(AgentResult result, ArtifactKind kind, string? targetPath)
    {
        if (targetPath == null)
            return;

        var artifact = result.Artifacts.FirstOrDefault(a => a.Kind == kind);
        if (artifact != null)
            artifact.TargetPath = targetPath;
    }

    /// <summary>
    /// A Gherkin run must produce all three files. Saying so plainly beats silently
    /// writing a partial change the user then has to debug.
    /// </summary>
    private void DescribeArtifacts(AgentResult result)
    {
        foreach (var artifact in result.Artifacts)
            Console.WriteLine($"Produced {artifact.Kind}: {artifact.FileName}");

        if (_profile is not { CanWriteGherkin: true })
            return;

        if (!result.Artifacts.Any(a => a.Kind == ArtifactKind.Feature))
            result.Warnings.Add("No .feature file was produced - the model did not follow the Gherkin contract.");

        if (!result.Artifacts.Any(a => a.Kind == ArtifactKind.PageObject))
            result.Warnings.Add("No page object was produced; locators may be missing or stuck inline in the steps.");

        if (!result.Artifacts.Any(a => a.Kind == ArtifactKind.StepDefinitions))
            result.Warnings.Add("No step definitions file was produced; the scenario may have no bindings.");
    }
}
