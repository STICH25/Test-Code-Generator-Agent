using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PlaywrightAgentAI.Services;

/// <summary>
/// Generates test code by driving the locally installed Claude Code CLI.
///
/// This is the provider for environments where the organisation supplies a Claude Code
/// seat but no API key: the CLI runs under the user's existing login. Everything happens
/// in a local subprocess - the prompt goes in on stdin and the answer comes back on
/// stdout, so no data leaves the machine except through Claude itself.
/// </summary>
/// <summary>How a single CLI invocation is constrained. The default is one text-only turn, which is what generation uses.</summary>
public sealed record CliRunOptions(
    int MaxTurns = 1,
    IReadOnlyList<string>? AllowedTools = null,
    IReadOnlyList<string>? DisallowedTools = null,
    string PromptLogName = "last-prompt.txt",
    string? WorkingDirectory = null);

public class ClaudeCliCodeGenerator : ITestCodeGenerator
{
    private const string GenerateInstruction =
        "The message piped to you on stdin is a complete specification for a C# Playwright test. " +
        "Follow it exactly and reply with nothing but the resulting C# source file. " +
        "Do not add commentary, explanation, or markdown code fences.";

    private readonly AppSettings _settings;
    private readonly string _cliPath;

    public ClaudeCliCodeGenerator(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _cliPath = ClaudeCliLocator.Locate(settings.ClaudeCliPath)
                   ?? throw new InvalidOperationException(ClaudeCliLocator.InstallHint);
    }

    public async Task<string> Generate(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be null or empty.", nameof(prompt));

        Console.WriteLine($"Asking Claude Code ({_settings.CliModel}) for the test...");

        var result = await Run(GenerateInstruction, prompt, cancellationToken);
        return ClaudeCodeGenerator.StripCodeFences(result);
    }

    /// <summary>Verifies the CLI is present and actually answers.</summary>
    public static async Task<ConnectionResult> TestConnection(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var path = ClaudeCliLocator.Locate(settings.ClaudeCliPath);
        if (path == null)
            return new ConnectionResult(false, ClaudeCliLocator.InstallHint, []);

        try
        {
            var generator = new ClaudeCliCodeGenerator(settings);
            var reply = await generator.Run("Answer the piped message.", "Reply with the single word: ready", cancellationToken);

            return reply.Contains("ready", StringComparison.OrdinalIgnoreCase)
                ? new ConnectionResult(true, $"Connected via Claude Code CLI ({Path.GetFileName(path)}).", AvailableModels)
                : new ConnectionResult(true, "CLI responded, but not as expected. It should still work.", AvailableModels);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ConnectionResult(false, ex.Message, []);
        }
    }

    /// <summary>
    /// The CLI takes an alias or a full model id. There is no key to enumerate models
    /// with on this path, so offer the aliases the CLI documents.
    /// </summary>
    public static IReadOnlyList<ClaudeModel> AvailableModels { get; } =
    [
        // Sonnet first: it is the default, and the Settings dropdown falls back to the first
        // entry when the saved model is not in the list.
        new("sonnet", "Sonnet (balanced, default)"),
        new("opus", "Opus (most capable)"),
        new("haiku", "Haiku (fastest)")
    ];

    /// <summary>
    /// A multi-turn run with an explicit tool allow-list, for the few jobs that need Claude to
    /// act rather than just write text (currently: reading a PBI from Azure DevOps). Callers
    /// choose the limits; tools outside <see cref="CliRunOptions.AllowedTools"/> are denied.
    /// </summary>
    public Task<string> RunRestricted(string instruction, string stdinPayload, CliRunOptions options, CancellationToken cancellationToken = default) =>
        Run(instruction, stdinPayload, options, cancellationToken);

    private Task<string> Run(string instruction, string stdinPayload, CancellationToken cancellationToken) =>
        Run(instruction, stdinPayload, new CliRunOptions(), cancellationToken);

    private async Task<string> Run(string instruction, string stdinPayload, CliRunOptions options, CancellationToken cancellationToken)
    {
        // Generation runs from a scratch directory so the CLI does not pick up CLAUDE.md files
        // or other project context from whatever folder the app happens to be in. A caller
        // that wants the opposite (the Azure DevOps lookup, which should behave like the user
        // running their skills inside their own solution) names a directory explicitly.
        var scratchDirectory = Path.Combine(Path.GetTempPath(), "PlaywrightAgentAI.cli");
        Directory.CreateDirectory(scratchDirectory);

        var workingDirectory = options.WorkingDirectory is { } requested && Directory.Exists(requested)
            ? requested
            : scratchDirectory;

        // Keep the exact prompt on disk for troubleshooting "why did it generate that?".
        // Local file, never transmitted. Always in the scratch directory, never in the
        // working directory: that may be the user's own solution, and nothing is written there.
        try
        {
            File.WriteAllText(Path.Combine(scratchDirectory, options.PromptLogName), stdinPayload);
        }
        catch
        {
            // Diagnostics only; never fail a run over this.
        }

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // CreateProcess cannot execute a .cmd or .bat directly; those need a shell.
        // npm's global shim on Windows is claude.cmd, so this is the normal case.
        var extension = Path.GetExtension(_cliPath);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(_cliPath);
        }
        else
        {
            startInfo.FileName = _cliPath;
        }

        foreach (var argument in BuildArguments(instruction, _settings.CliModel, options))
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not start the Claude Code CLI: {ex.Message}", ex);
        }

        // The prompt goes on stdin rather than argv: it embeds captured page markup and
        // would blow past the Windows command-line length limit as an argument.
        var writeStdin = Task.Run(async () =>
        {
            await process.StandardInput.WriteAsync(stdinPayload.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }, cancellationToken);

        var readStdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var readStderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await using var kill = cancellationToken.Register(() => TryKill(process));

            await writeStdin;
            var stdout = await readStdout;
            var stderr = await readStderr;
            await process.WaitForExitAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(stderr))
                Console.Error.WriteLine($"claude: {stderr.Trim()}");

            if (process.ExitCode != 0)
                throw new InvalidOperationException(DescribeFailure(process.ExitCode, stdout, stderr));

            return ExtractResult(stdout);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            Console.WriteLine("Claude Code CLI cancelled.");
            throw;
        }
    }

    /// <summary>
    /// Turns a non-zero exit into something the user can act on.
    ///
    /// The CLI still prints its JSON envelope when it fails, and the real reason lives in
    /// that envelope's "result" - an expired OAuth session, for instance. Reporting only
    /// "exited with code 1" throws that away and sends the user hunting for a bug in this
    /// app when the fix is to re-run `claude` and log in.
    /// </summary>
    private static string DescribeFailure(int exitCode, string stdout, string stderr)
    {
        var detail = "";

        try
        {
            var trimmed = stdout.Trim();
            if (trimmed.StartsWith('{'))
            {
                using var document = JsonDocument.Parse(trimmed);
                if (document.RootElement.TryGetProperty("result", out var result) &&
                    result.ValueKind == JsonValueKind.String)
                {
                    detail = result.GetString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall back to stderr below.
        }

        if (string.IsNullOrWhiteSpace(detail))
            detail = stderr.Trim();

        if (string.IsNullOrWhiteSpace(detail))
            return $"Claude Code CLI exited with code {exitCode} without explaining why.";

        // Point straight at the remedy for the failure that is by far the most common.
        if (IsAuthenticationFailure(detail))
            return $"{detail}  ->  run 'claude' in a terminal and sign in again, then retry.";

        return detail;
    }

    /// <summary>
    /// True when a failure message describes an expired or missing CLI login rather than
    /// some other error. Callers use this to decide whether opening a sign-in terminal
    /// (<see cref="OpenSignInTerminal"/>) would actually help.
    /// </summary>
    public static bool IsAuthenticationFailure(string? message) =>
        message != null && (
            message.Contains("authenticate", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("OAuth", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("log in", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Opens a normal, visible console window running the CLI interactively so the user can
    /// sign in themselves.
    ///
    /// This app never stores or handles Claude credentials, so there is no "log in for the
    /// user" path here - the only way to recover from an expired CLI session is the CLI's
    /// own interactive login, which starting `claude` with no arguments triggers (it prompts
    /// to authenticate, typically by opening the user's browser to complete OAuth). Routed
    /// through cmd.exe /k so the window stays open afterwards instead of closing the instant
    /// login finishes, and so a .cmd shim (npm's global install on Windows) launches the same
    /// way a .exe would.
    /// </summary>
    public void OpenSignInTerminal()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), "PlaywrightAgentAI.cli");
        Directory.CreateDirectory(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = $"/k \"{_cliPath}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };

        Process.Start(startInfo);
    }

    private static IEnumerable<string> BuildArguments(string instruction, string model, CliRunOptions options)
    {
        yield return "--print";
        yield return instruction;

        yield return "--output-format";
        yield return "json";

        yield return "--model";
        yield return model;

        // Generation is one turn: a single text generation, not an agentic session, and a
        // loop there would burn the user's quota with nothing to show for it.
        yield return "--max-turns";
        yield return options.MaxTurns.ToString();

        // Variadic options go last so they cannot swallow a flag that follows them. In
        // --print mode a tool that is not allowed is denied, not prompted for, so the allow
        // list is a hard boundary rather than a request.
        if (options.AllowedTools is { Count: > 0 })
        {
            yield return "--allowedTools";
            yield return string.Join(",", options.AllowedTools);
        }

        if (options.DisallowedTools is { Count: > 0 })
        {
            yield return "--disallowedTools";
            yield return string.Join(",", options.DisallowedTools);
        }
    }

    /// <summary>
    /// --output-format json wraps the answer in an envelope. The field layout is not
    /// contractual, so fall back to treating stdout as the answer if it does not parse.
    /// </summary>
    private static string ExtractResult(string stdout)
    {
        var trimmed = stdout.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return trimmed;

            if (root.TryGetProperty("is_error", out var isError) &&
                isError.ValueKind == JsonValueKind.True)
            {
                var detail = root.TryGetProperty("result", out var errorText) ? errorText.GetString() : null;
                throw new InvalidOperationException($"Claude Code reported an error: {detail ?? "no detail"}");
            }

            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
                return result.GetString() ?? string.Empty;

            return trimmed;
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or we lost the race with a normal exit.
        }
    }
}
