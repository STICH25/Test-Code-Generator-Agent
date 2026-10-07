using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Services;

public enum TestRunOutcome
{
    Passed,
    Failed,

    /// <summary>The generated code did not compile - a defect in the output, not in the page or PBI.</summary>
    BuildFailed,

    /// <summary>It built, but the filter matched nothing, so nothing was actually verified.</summary>
    NoTestsFound,

    TimedOut,
    Cancelled,

    /// <summary>The run could not be set up or started (no dotnet, no test project, copy failed...).</summary>
    Error
}

public sealed record TestCaseResult(string Name, bool Passed, string? Message);

public sealed class TestRunResult
{
    public required TestRunOutcome Outcome { get; init; }

    public required string Summary { get; init; }

    public List<TestCaseResult> Tests { get; init; } = [];

    /// <summary>Compiler errors, de-duplicated, when <see cref="Outcome"/> is BuildFailed.</summary>
    public List<string> BuildErrors { get; init; } = [];

    /// <summary>The failure text worth showing a person, most specific first.</summary>
    public IEnumerable<string> FailureMessages =>
        Tests.Where(t => !t.Passed && !string.IsNullOrWhiteSpace(t.Message)).Select(t => t.Message!);
}

/// <summary>
/// Builds and runs the generated tests without touching the linked solution.
///
/// The solution is mirrored into a scratch folder under %TEMP%, the generated files are written
/// there exactly as Insert would write them (same folders, same collision rules), and
/// <c>dotnet test</c> runs against that copy. The real solution is never written to, so a
/// failing or half-finished test cannot leave anything behind in it. Nothing leaves the
/// machine: this is a local build and a local test run.
///
/// Only the new scenarios run. In the sandbox copy of a feature file each of them is given a
/// tag, and the run filters on that tag, which is independent of how Reqnroll happens to name
/// the generated test methods. An existing feature the user chose to extend therefore does not
/// re-run its older scenarios.
/// </summary>
public static partial class GeneratedTestRunner
{
    /// <summary>Tag added, in the scratch copy only, to every scenario that should run.</summary>
    internal const string VerifyTag = "pa_verify";

    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(10);
    private const int MaxLoggedBuildErrors = 15;

    public static async Task<TestRunResult> Run(
        SolutionProfile profile,
        IReadOnlyList<GeneratedArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        try
        {
            var sandbox = SandboxPathFor(profile.RootPath);
            Console.WriteLine($"Verify: mirroring the solution into {sandbox}");
            await Mirror(profile.RootPath, sandbox, cancellationToken);

            var sandboxProfile = SolutionScanner.Scan(sandbox);
            var written = SolutionWriter.Write(sandboxProfile, Remap(artifacts, profile.RootPath, sandbox));
            if (written.Count == 0)
                return Error("Nothing could be written into the scratch copy of the solution.");

            var filter = PrepareFilter(artifacts, written, profile.RootPath);
            if (filter == null)
                return Error("There is no scenario or test class in the generated code to run.");

            var project = FindTestProject(sandbox) ?? sandboxProfile.ProjectFile;
            if (project == null)
                return Error("No test project (.csproj) was found in the solution.");

            return await DotnetTest(project, filter, sandbox, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new TestRunResult { Outcome = TestRunOutcome.Cancelled, Summary = "Cancelled." };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Verify failed: {ex}");
            return Error(ex.Message);
        }
    }

    private static TestRunResult Error(string message) =>
        new() { Outcome = TestRunOutcome.Error, Summary = message };

    // ------------------------------------------------------------------ scratch copy

    internal static string SandboxPathFor(string solutionRoot)
    {
        var full = Path.GetFullPath(solutionRoot).TrimEnd(Path.DirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..8];
        var name = Path.GetFileName(full);

        // One folder per solution rather than per run, so bin/obj survive and the second run is
        // an incremental build instead of a cold one.
        return Path.Combine(Path.GetTempPath(), "PlaywrightAgentAI.run", $"{name}-{hash}");
    }

    private static async Task Mirror(string source, string destination, CancellationToken ct)
    {
        // /MIR deletes whatever is in the destination that is not in the source, so make sure the
        // destination is unmistakably our own scratch folder before handing it over.
        var root = Path.Combine(Path.GetTempPath(), "PlaywrightAgentAI.run") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(destination).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to mirror into {destination}: not a scratch folder.");

        Directory.CreateDirectory(destination);

        var result = await RunProcess("robocopy",
            // No trailing separator: robocopy reads `C:\dir\"` as an escaped quote.
            ["\"" + source.TrimEnd('\\', '/') + "\"", "\"" + destination.TrimEnd('\\', '/') + "\"", "/MIR",
             "/XD", "bin", "obj", ".git", ".vs", ".idea", "TestResults", "node_modules", "packages",
             "/XF", "*.bak",
             "/NFL", "/NDL", "/NJH", "/NJS", "/NP", "/R:1", "/W:1"],
            workingDirectory: null, TimeSpan.FromMinutes(5), ct, echo: false);

        // robocopy: 0-7 are success variants (files copied, extras removed...), 8+ is failure.
        if (result.ExitCode >= 8)
            throw new IOException($"Copying the solution failed (robocopy exit {result.ExitCode}).");
    }

    /// <summary>The same artifacts, aimed at the scratch copy. The originals are left alone.</summary>
    private static List<GeneratedArtifact> Remap(IEnumerable<GeneratedArtifact> artifacts, string realRoot, string sandbox)
    {
        var real = Path.GetFullPath(realRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return artifacts.Select(a =>
        {
            string? target = a.TargetPath;
            if (!string.IsNullOrWhiteSpace(target) &&
                Path.GetFullPath(target).StartsWith(real, StringComparison.OrdinalIgnoreCase))
            {
                target = Path.Combine(sandbox, Path.GetFullPath(target)[real.Length..]);
            }

            return new GeneratedArtifact
            {
                Kind = a.Kind,
                FileName = a.FileName,
                Content = a.Content,
                TargetPath = target
            };
        }).ToList();
    }

    // ------------------------------------------------------------------ what to run

    /// <summary>
    /// Tags the scenarios to run inside the scratch copy and returns the dotnet test filter, or
    /// null when there is nothing runnable.
    /// </summary>
    private static string? PrepareFilter(
        IReadOnlyList<GeneratedArtifact> artifacts, IReadOnlyList<GeneratedArtifact> written, string realRoot)
    {
        var parts = new List<string>();
        var tagged = false;

        foreach (var copy in written)
        {
            var original = artifacts.FirstOrDefault(a => a.Kind == copy.Kind && a.FileName == copy.FileName);

            if (copy.Kind == ArtifactKind.Feature && copy.WrittenPath != null)
            {
                var existing = original?.TargetPath is { } t && File.Exists(t) ? File.ReadAllText(t) : null;
                var content = TagScenarios(File.ReadAllText(copy.WrittenPath), existing);
                if (content != null)
                {
                    File.WriteAllText(copy.WrittenPath, content);
                    tagged = true;
                }
            }
            else if (copy.Kind == ArtifactKind.Test)
            {
                var name = ClassName().Match(copy.Content);
                if (name.Success)
                    parts.Add($"FullyQualifiedName~{name.Groups["name"].Value}");
            }
        }

        if (tagged)
            parts.Add($"TestCategory={VerifyTag}");

        return parts.Count == 0 ? null : string.Join("|", parts);
    }

    /// <summary>
    /// Adds the verify tag above every scenario that is new relative to <paramref name="existing"/>
    /// (the file the user chose to extend), or above all of them when none is new. Null when the
    /// text has no scenario at all.
    /// </summary>
    internal static string? TagScenarios(string featureText, string? existing)
    {
        var lines = featureText.ReplaceLineEndings("\n").Split('\n').ToList();

        var scenarios = lines
            .Select((line, index) => (Match: ScenarioLine().Match(line), Index: index))
            .Where(x => x.Match.Success)
            .ToList();

        if (scenarios.Count == 0)
            return null;

        var known = existing == null
            ? []
            : ScenarioLine().Matches(existing.ReplaceLineEndings("\n"))
                .Select(m => m.Groups["title"].Value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var chosen = scenarios.Where(s => !known.Contains(s.Match.Groups["title"].Value.Trim())).ToList();
        if (chosen.Count == 0)
            chosen = scenarios;

        // Bottom-up so earlier indexes stay valid while lines are inserted.
        foreach (var s in chosen.OrderByDescending(x => x.Index))
            lines.Insert(s.Index, $"{s.Match.Groups["indent"].Value}@{VerifyTag}");

        return string.Join("\n", lines);
    }

    private static string? FindTestProject(string sandbox)
    {
        // A solution can hold several projects; the one to run is the one that references the test SDK.
        foreach (var project in Directory.EnumerateFiles(sandbox, "*.csproj", SearchOption.AllDirectories))
        {
            if (project.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                project.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (File.ReadAllText(project).Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
                    return project;
            }
            catch (IOException)
            {
                // Unreadable project: skip it and let the fallback pick the first one.
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ dotnet test

    private static async Task<TestRunResult> DotnetTest(string project, string filter, string sandbox, CancellationToken ct)
    {
        var results = Path.Combine(sandbox, "TestResults", "pa");
        if (Directory.Exists(results))
            Directory.Delete(results, recursive: true);

        Console.WriteLine($"Verify: dotnet test {Path.GetFileName(project)} --filter \"{filter}\"");

        var run = await RunProcess("dotnet",
            ["test", "\"" + project + "\"",
             "--filter", "\"" + filter + "\"",
             "--logger", "\"trx;LogFileName=pa-run.trx\"",
             "--results-directory", "\"" + results + "\"",
             "--nologo", "-v", "minimal", "-nodeReuse:false"],
            Path.GetDirectoryName(project), RunTimeout, ct, echo: true);

        if (run.TimedOut)
            return new TestRunResult
            {
                Outcome = TestRunOutcome.TimedOut,
                Summary = $"Stopped after {RunTimeout.TotalMinutes:0} minutes without finishing."
            };

        var trx = Directory.Exists(results) ? Directory.GetFiles(results, "*.trx").FirstOrDefault() : null;
        if (trx == null)
        {
            var errors = BuildErrorsIn(run.Output);
            return errors.Count > 0
                ? new TestRunResult
                {
                    Outcome = TestRunOutcome.BuildFailed,
                    Summary = "The generated code did not build.",
                    BuildErrors = errors
                }
                : Error($"dotnet test produced no results (exit code {run.ExitCode}). See the Log.");
        }

        var tests = ParseTrx(trx);
        var failed = tests.Count(t => !t.Passed);

        if (tests.Count == 0)
            return new TestRunResult
            {
                Outcome = TestRunOutcome.NoTestsFound,
                Summary = "It built, but no test matched, so nothing was verified.",
                Tests = tests
            };

        return new TestRunResult
        {
            Outcome = failed == 0 ? TestRunOutcome.Passed : TestRunOutcome.Failed,
            Summary = failed == 0
                ? $"{tests.Count} test(s) passed."
                : $"{failed} of {tests.Count} test(s) failed.",
            Tests = tests
        };
    }

    internal static List<TestCaseResult> ParseTrx(string trxPath)
    {
        var results = new List<TestCaseResult>();
        var doc = XDocument.Load(trxPath);

        foreach (var r in doc.Descendants().Where(e => e.Name.LocalName == "UnitTestResult"))
        {
            var outcome = (string?)r.Attribute("outcome") ?? "";

            // NotExecuted (skipped / ignored) is neither a pass nor a failure of the generated code.
            if (outcome.Equals("NotExecuted", StringComparison.OrdinalIgnoreCase))
                continue;

            var message = r.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value;
            results.Add(new TestCaseResult(
                (string?)r.Attribute("testName") ?? "(unnamed)",
                outcome.Equals("Passed", StringComparison.OrdinalIgnoreCase),
                message?.Trim()));
        }

        return results;
    }

    private static List<string> BuildErrorsIn(string output) =>
        output.ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            .Select(l => Regex.Replace(l.Trim(), @"\s*\[[^\]]*\.csproj\]\s*$", ""))
            // The same error is reported once per target framework and again in the summary.
            .Select(l => Regex.Replace(l, @"^.*[\\/](?=[^\\/]+\(\d+,\d+\): error)", ""))
            .Distinct()
            .Take(MaxLoggedBuildErrors)
            .ToList();

    // ------------------------------------------------------------------ process plumbing

    private sealed record ProcessResult(int ExitCode, string Output, bool TimedOut);

    private static async Task<ProcessResult> RunProcess(
        string fileName, IEnumerable<string> arguments, string? workingDirectory,
        TimeSpan timeout, CancellationToken ct, bool echo)
    {
        var info = new ProcessStartInfo(fileName, string.Join(" ", arguments))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = new Process { StartInfo = info };
        var output = new StringBuilder();
        var gate = new Lock();

        void OnLine(string? line)
        {
            if (line == null)
                return;

            lock (gate)
                output.AppendLine(line);

            if (echo)
                Console.WriteLine($"  {line}");
        }

        process.OutputDataReceived += (s, e) => OnLine(e.Data);
        process.ErrorDataReceived += (s, e) => OnLine(e.Data);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not start {fileName}: {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
            // Draining: WaitForExitAsync returns when the process ends, the async readers a moment later.
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already gone */ }

            if (ct.IsCancellationRequested)
                throw;

            return new ProcessResult(-1, output.ToString(), TimedOut: true);
        }

        lock (gate)
            return new ProcessResult(process.ExitCode, output.ToString(), TimedOut: false);
    }

    [GeneratedRegex(@"^(?<indent>\s*)(?:Scenario Outline|Scenario Template|Scenario)\s*:\s*(?<title>.*?)\s*$", RegexOptions.Multiline)]
    private static partial Regex ScenarioLine();

    [GeneratedRegex(@"\bclass\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex ClassName();
}
