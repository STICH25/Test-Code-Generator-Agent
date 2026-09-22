using System.Text;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Messages;

namespace PlaywrightAgentAI.Services;

/// <summary>Provider-agnostic seam so the agent is not bound to one vendor's SDK.</summary>
public interface ITestCodeGenerator
{
    Task<string> Generate(string prompt, CancellationToken cancellationToken = default);
}

public partial class ClaudeCodeGenerator : ITestCodeGenerator
{
    private const string SystemPrompt =
        "You are a senior QA automation engineer who writes Playwright tests in C#. " +
        "You return only compilable C# source, never prose and never markdown fences.";

    private readonly AnthropicClient _client;
    private readonly AppSettings _settings;

    public ClaudeCodeGenerator(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.HasApiKey)
            throw new InvalidOperationException("No Claude API key is configured. Add one in Settings.");

        _settings = settings;
        _client = new AnthropicClient { ApiKey = settings.ApiKey };
    }

    public async Task<string> Generate(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be null or empty.", nameof(prompt));

        var parameters = new MessageCreateParams
        {
            Model = _settings.Model,
            MaxTokens = _settings.MaxTokens,
            System = SystemPrompt,
            // Adaptive thinking is the current mode for Claude 4.6+; budget_tokens is
            // rejected on Opus 5. Effort controls how much of it we pay for.
            Thinking = new ThinkingConfigAdaptive(),
            OutputConfig = new OutputConfig { Effort = MapEffort(_settings.Effort) },
            Messages = [new() { Role = Role.User, Content = prompt }]
        };

        var builder = new StringBuilder();

        try
        {
            Console.WriteLine($"Asking {_settings.Model} for the test (effort: {_settings.Effort})...");

            // Streaming, because the DOM context can make these requests large and a
            // non-streaming call with a high MaxTokens risks an HTTP timeout. It also
            // sidesteps the old bug of reading only content[0]: with thinking enabled the
            // first block is a thinking block, and only text deltas arrive here.
            await foreach (var streamEvent in _client.Messages
                               .CreateStreaming(parameters)
                               .WithCancellation(cancellationToken))
            {
                if (streamEvent.TryPickContentBlockDelta(out var blockDelta) &&
                    blockDelta.Delta.TryPickText(out var text))
                {
                    builder.Append(text.Text);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Claude request cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Claude request failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        return StripCodeFences(builder.ToString());
    }

    /// <summary>
    /// Models routinely wrap output in ```csharp fences despite being told not to.
    /// Strip them rather than emitting code that will not compile.
    /// </summary>
    internal static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var match = FencedBlock().Match(trimmed);
        return match.Success ? match.Groups["body"].Value.Trim() : trimmed;
    }

    private static Effort MapEffort(string? effort) => effort?.Trim().ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "max" => Effort.Max,
        _ => Effort.High
    };

    [GeneratedRegex(@"\A```[a-zA-Z0-9#+-]*\s*\n(?<body>.*?)\n?```\s*\z", RegexOptions.Singleline)]
    private static partial Regex FencedBlock();
}
