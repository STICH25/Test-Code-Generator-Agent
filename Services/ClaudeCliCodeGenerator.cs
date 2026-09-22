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
        new("opus", "Opus (most capable)"),
        new("sonnet", "Sonnet (balanced)"),
        new("haiku", "Haiku (fastest)")
    ];

    private async Task<string> Run(string instruction, string stdinPayload, CancellationToken cancellationToken)
    {
        // Run from a scratch directory so the CLI does not pick up CLAUDE.md files or
        // other project context from whatever folder the app happens to be in.
        var workingDirectory = Path.Combine(Path.GetTempPath(), "PlaywrightAgentAI.cli");
        Directory.CreateDirectory(workingDirectory);

        // Keep the exact prompt on disk for troubleshooting "why did it generate that?".
        // Local file, never transmitted.
        try
        {
            File.WriteAllText(Path.Combine(workingDirectory, "last-prompt.txt"), stdinPayload);
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

        foreach (var argument in BuildArguments(instruction))
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
                throw new InvalidOperationException($"Claude Code CLI exited with code {process.ExitCode}. {stderr.Trim()}");

            return ExtractResult(stdout);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            Console.WriteLine("Claude Code CLI cancelled.");
            throw;
        }
    }

    private IEnumerable<string> BuildArguments(string instruction)
    {
        yield return "--print";
        yield return instruction;

        yield return "--output-format";
        yield return "json";

        yield return "--model";
        yield return _settings.CliModel;

        // One turn only: this is a single text generation, not an agentic session, and a
        // loop here would burn the user's quota with nothing to show for it.
        yield return "--max-turns";
        yield return "1";
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
