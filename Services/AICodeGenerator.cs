using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System;

namespace PlaywrightAgentAI.Services
{
    public class AICodeGenerator
    {
        private readonly string _apiKey;

        public AICodeGenerator(IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            // Prefer configuration key "OpenAI:ApiKey", fall back to environment variable OPENAI_API_KEY.
            _apiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("OpenAI API key is not configured. Set configuration 'OpenAI:ApiKey' or environment variable 'OPENAI_API_KEY'.");
            }
        }

        public async Task<string> Generate(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt cannot be null or empty.", nameof(prompt));
            }

            try
            {
                var client = new OpenAIClient(_apiKey);
                var chatClient = client.GetChatClient("gpt-4o");

                var response = await chatClient.CompleteChatAsync(
                    ChatMessage.CreateSystemMessage("You are an automation engineer."),
                    ChatMessage.CreateUserMessage(prompt)
                );

                if (response.Value.Content is { Count: > 0 } contentParts && contentParts[0] is { } firstPart && firstPart.Kind == ChatMessageContentPartKind.Text)
                {
                    return firstPart.Text;
                }

                Console.Error.WriteLine("Warning: AI response was empty or malformed.");
                return string.Empty;
            }
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                Console.Error.WriteLine("Error: OpenAI quota exceeded (HTTP 429). Check your plan and billing at https://platform.openai.com/account/billing/overview");
                Console.Error.WriteLine($"Details: {ex.Message}");
                throw;
            }
            catch (ClientResultException ex) when (ex.Status == 401)
            {
                Console.Error.WriteLine("Error: OpenAI authentication failed (HTTP 401). Your API key may be invalid or revoked.");
                Console.Error.WriteLine($"Details: {ex.Message}");
                throw;
            }
            catch (ClientResultException ex) when (ex.Status == 400)
            {
                Console.Error.WriteLine("Error: Invalid request to OpenAI (HTTP 400).");
                Console.Error.WriteLine($"Details: {ex.Message}");
                throw;
            }
            catch (ClientResultException ex)
            {
                Console.Error.WriteLine($"Error: OpenAI API error (HTTP {ex.Status})");
                Console.Error.WriteLine($"Details: {ex.Message}");
                throw;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine("Error: Failed to connect to OpenAI API. Check your internet connection.");
                Console.Error.WriteLine($"Details: {ex.Message}");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                Console.Error.WriteLine("Error: Request to OpenAI timed out. The API took too long to respond.");
                Console.Error.WriteLine($"Details: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Unexpected error during AI code generation: {ex.GetType().Name}");
                Console.Error.WriteLine($"Details: {ex.Message}");
                throw;
            }
        }
    }
}