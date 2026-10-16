using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Services;

/// <summary>
/// Writes generated artifacts into the linked solution.
///
/// Nothing here relies on a backup. A file the user chose to extend arrives already merged -
/// the generated material has been inserted into its current text by <see cref="ArtifactMerger"/>,
/// so overwriting it writes everything that was there plus the new code. Any other file is
/// written beside what exists (numbered suffix) rather than over it, so no existing file is
/// ever replaced by something that is not a superset of it. No .bak files are created.
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
                    // An explicit target: the user picked this exact file from a dropdown, and
                    // the content is that file's own text with the new material merged in, so
                    // writing it back loses nothing.
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

                    // With no target chosen, a same-named file is an accident of naming, not an
                    // instruction to replace it: the generated text only holds the new material
                    // and would drop everything already in that file. Write beside it instead.
                    // (Feature files used to be rewritten in place here, back when appending a
                    // scenario meant reproducing the whole file; the target dropdown replaced that.)
                    if (File.Exists(path))
                    {
                        path = NextAvailable(path);
                        Console.WriteLine($"  {artifact.FileName} exists; writing to {Path.GetFileName(path)} instead");
                    }
                }


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
