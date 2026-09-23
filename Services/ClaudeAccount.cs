using Anthropic;

namespace PlaywrightAgentAI.Services;

public record ClaudeModel(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public record ConnectionResult(bool Success, string Message, IReadOnlyList<ClaudeModel> Models);

/// <summary>
/// Verifies an API key and discovers which models the account can actually use.
///
/// Listing models beats hardcoding a dropdown: new models appear without an app update,
/// and the user never sees an option their account would reject.
/// </summary>
public static class ClaudeAccount
{
    public static async Task<ConnectionResult> TestConnection(string? apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new ConnectionResult(false, "Enter an API key first.", []);

        try
        {
            var client = new AnthropicClient { ApiKey = apiKey.Trim() };
            var page = await client.Models.List();

            var models = page.Items
                .Select(m => new ClaudeModel(m.ID, string.IsNullOrWhiteSpace(m.DisplayName) ? m.ID : m.DisplayName))
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            cancellationToken.ThrowIfCancellationRequested();

            return models.Count == 0
                ? new ConnectionResult(false, "Connected, but the account returned no models.", [])
                : new ConnectionResult(true, $"Connected. {models.Count} models available.", models);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ConnectionResult(false, Describe(ex), []);
        }
    }

    private static string Describe(Exception ex)
    {
        var message = ex.Message;

        // The SDK surfaces status codes in the message; map the common ones to something
        // a user can act on instead of showing a raw API error.
        if (message.Contains("401", StringComparison.Ordinal) ||
            message.Contains("authentication", StringComparison.OrdinalIgnoreCase))
        {
            return "Authentication failed - check the API key.";
        }

        if (message.Contains("403", StringComparison.Ordinal))
            return "That key is not permitted to list models.";

        if (ex is HttpRequestException)
            return "Could not reach the Claude API - check your connection.";

        return $"{ex.GetType().Name}: {message}";
    }
}
