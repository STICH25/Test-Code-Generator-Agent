using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Services;

/// <summary>
/// Writes generated artifacts into the linked solution.
///
/// Files are written automatically, so the safety net matters: appending a scenario means
/// rewriting an existing .feature file in full, and the target solution may not be under
/// version control. Any file about to be overwritten is copied to a timestamped .bak
/// first, so nothing the user wrote can be lost without a way back.
/// </summary>
public static class SolutionWriter
{
    public static IReadOnlyList<GeneratedArtifact> Write(SolutionProfile profile, IEnumerable<GeneratedArtifact> artifacts)
    {
        var written = new List<GeneratedArtifact>();

        foreach (var artifact in artifacts)
        {
            try
            {
                string path;

                if (!string.IsNullOrWhiteSpace(artifact.TargetPath))
                {
                    // An explicit target: the user picked this exact file from a dropdown,
                    // and the prompt gave the model that file's full content to reproduce.
                    // Safe to overwrite outright regardless of kind - the backup below is
                    // the safety net, not a same-name coincidence to guard against.
                    path = artifact.TargetPath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                }
                else
                {
                    var directory = DirectoryFor(profile, artifact.Kind);

                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        Console.Error.WriteLine($"Nowhere to write {artifact.FileName}: the solution has no folder for {artifact.Kind}.");
                        continue;
                    }

                    Directory.CreateDirectory(directory);
                    path = Path.Combine(directory, SafeFileName(artifact.FileName));

                    // A feature file is deliberately rewritten in full when no target was
                    // chosen - that is the "new feature file" convention. Page objects and
                    // step-definition files are different here: the model was not shown any
                    // specific file's content in this branch, so a same-named collision is
                    // accidental, not intentional, and blind-overwriting it could delete
                    // methods or bindings the model never saw. Write beside it instead.
                    if (artifact.Kind is ArtifactKind.StepDefinitions or ArtifactKind.PageObject && File.Exists(path))
                    {
                        path = NextAvailable(path);
                        Console.WriteLine($"  {artifact.FileName} exists; writing to {Path.GetFileName(path)} instead");
                    }
                }

                BackUpIfPresent(path);

                // CRLF so files match the rest of a Windows C# project.
                File.WriteAllText(path, artifact.Content.ReplaceLineEndings());

                artifact.WrittenPath = path;
                written.Add(artifact);

                Console.WriteLine($"Wrote {path}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not write {artifact.FileName}: {ex.Message}");
            }
        }

        return written;
    }

    private static string? DirectoryFor(SolutionProfile profile, ArtifactKind kind) => kind switch
    {
        ArtifactKind.Feature => profile.FeaturesDirectory,
        ArtifactKind.PageObject => profile.PageObjectsDirectory,
        ArtifactKind.StepDefinitions => profile.StepDefinitionsDirectory,
        _ => profile.TestDirectory
    };

    private static void BackUpIfPresent(string path)
    {
        if (!File.Exists(path))
            return;

        var backup = $"{path}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";

        try
        {
            File.Copy(path, backup, overwrite: false);
            Console.WriteLine($"  previous version saved as {Path.GetFileName(backup)}");
        }
        catch (Exception ex)
        {
            // Better to refuse the write than to destroy an unbacked-up file.
            throw new IOException($"Refusing to overwrite {Path.GetFileName(path)} - could not create a backup: {ex.Message}", ex);
        }
    }

    /// <summary>Finds a free name beside an existing file: Foo.cs, Foo2.cs, Foo3.cs ...</summary>
    private static string NextAvailable(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; i < 100; i++)
        {
            var candidate = Path.Combine(directory, $"{stem}{i}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{stem}_{DateTime.Now:yyyyMMddHHmmss}{extension}");
    }

    private static string SafeFileName(string fileName)
    {
        var cleaned = new string(fileName
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "Generated.cs" : cleaned;
    }
}
