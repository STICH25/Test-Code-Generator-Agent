using System.Text.RegularExpressions;
//using TechTalk.SpecFlow;

namespace PlaywrightAgentAI.Services;

public class GherkinBuilder
{
    /// <summary>
    /// Build a Gherkin step and a C# step-definition skeleton from a plain objective.
    /// Returns (featureStep, stepDefinitionSource).
    /// </summary>
    public (string FeatureStep, string StepDefinition) Build(string objective, string keyword = "Given")
    {
        if (string.IsNullOrWhiteSpace(objective))
        {
            throw new ArgumentException("Objective cannot be null or whitespace.", nameof(objective));
        }

        keyword = NormalizeKeyword(keyword);
        var cleaned = NormalizeObjective(objective);
        var stepText = EnsureReadableStep(cleaned);
        var methodName = BuildMethodName(keyword, stepText);

        var featureStep = $"{keyword} {stepText}";

        var stepDefinition =
        $@"[{keyword}(""{EscapeForAttribute(stepText)}"")]
        public void {methodName}()
        {{
            throw new PendingStepException();
        }}";

        return (featureStep, stepDefinition);
    }

    private static string NormalizeKeyword(string keyword)
    {
        keyword = (keyword ?? "Given").Trim();
        var allowed = new[] { "Given", "When", "Then", "And", "But" };
        return allowed.Contains(keyword, StringComparer.OrdinalIgnoreCase)
            ? allowed.First(k => k.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            : "Given";
    }

    private static string NormalizeObjective(string objective)
    {
        // Remove surrounding punctuation, normalize whitespace
        var s = objective.Trim();
        s = Regex.Replace(s, @"^\s*-\s*", "");             // trim bullet-like prefix
        s = Regex.Replace(s, @"[^\w\s'-]", "");            // remove punctuation except apostrophes/hyphens
        s = Regex.Replace(s, @"\s+", " ");                 // collapse spaces
        return s.Trim();
    }

    private static string EnsureReadableStep(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Lowercase first character to produce a natural Gherkin phrase
        text = text.Trim();
        text = char.ToLowerInvariant(text[0]) + text.Substring(1);
        return text;
    }

    private static string BuildMethodName(string keyword, string stepText)
    {
        // Create a PascalCase method name, prefix with keyword
        var words = Regex.Split(stepText, @"\s+")
                         .Select(w => Regex.Replace(w, @"[^A-Za-z0-9]", ""))
                         .Where(w => !string.IsNullOrWhiteSpace(w))
                         .Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1) : string.Empty));
        var body = string.Concat(words);
        if (string.IsNullOrEmpty(body)) body = "Step";
        return $"{keyword}{body}";
    }

    private static string EscapeForAttribute(string text)
    {
        // Escape double-quotes for attribute string literal
        return text.Replace("\"", "\\\"");
    }
}