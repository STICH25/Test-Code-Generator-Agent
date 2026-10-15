using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PlaywrightAgentAI.Services;

/// <summary>
/// What a merge did. When <see cref="Fallback"/> is true the existing text was left exactly as it
/// was and nothing was added; the caller decides what to do with the generated text instead.
/// </summary>
public sealed record MergeResult(string Content, int Added, int Skipped, bool Fallback, string? Note = null);

/// <summary>
/// Adds newly generated material to a file the user chose to extend, without ever rewriting what
/// is already there.
///
/// This replaces "ask the model to reproduce the whole file". A big feature file or step class
/// does not survive that: the prompt can only carry part of it, so the reproduction came back as
/// just the new scenario (or just the head of the file plus it) and Insert overwrote the real
/// file with that. Here the model returns only what is new, and this inserts it into the
/// untouched original text - existing characters are never changed or removed, only added to.
///
/// Anything that already exists is skipped instead of added again: a scenario with the same title,
/// a member with the same signature, a step whose keyword and pattern are already bound anywhere
/// in the solution. That is the same "reuse, do not duplicate" rule the prompt gives the model,
/// enforced here as well because a model is not a guarantee.
/// </summary>
public static partial class ArtifactMerger
{
    // ------------------------------------------------------------------ Gherkin

    public static MergeResult MergeFeature(string existing, string generated)
    {
        var current = existing.ReplaceLineEndings("\n");
        var known = ScenarioTitles(current);

        // The prompt marks a cut-short file with "[truncated]". A model that echoes that file back
        // puts the marker in its reply; what precedes it is a partial copy of the existing file,
        // possibly cut in the middle of a scenario, so only what follows can be new.
        var reply = generated.ReplaceLineEndings("\n");
        var marker = reply.LastIndexOf("[truncated]", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            var eol = reply.IndexOf('\n', marker);
            reply = eol < 0 ? "" : reply[(eol + 1)..];
        }

        // A block with no step is a scenario that was cut off, not a new one.
        var blocks = ScenarioBlocks(reply).Where(b => StepLine().IsMatch(b.Text)).ToList();
        if (blocks.Count == 0)
            return new MergeResult(existing, 0, 0, Fallback: true, "No scenario was found in the generated feature text.");

        var targetIndent = IndentOfFirstScenario(current) ?? blocks[0].Indent;
        var added = new List<string>();
        var skipped = 0;

        foreach (var block in blocks)
        {
            if (!known.Add(block.Title))
            {
                skipped++;
                continue;
            }

            added.Add(Reindent(block.Text, block.Indent, targetIndent));
        }

        if (added.Count == 0)
            return new MergeResult(existing, 0, skipped, Fallback: false, "Every generated scenario already exists in the file.");

        var sb = new StringBuilder(current.TrimEnd());
        foreach (var text in added)
            sb.Append("\n\n").Append(text.TrimEnd());
        sb.Append('\n');

        return new MergeResult(sb.ToString(), added.Count, skipped, Fallback: false);
    }

    private sealed record ScenarioBlock(string Title, string Indent, string Text);

    private static HashSet<string> ScenarioTitles(string text)
    {
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in ScenarioLine().Matches(text))
            titles.Add(m.Groups["title"].Value.Trim());
        return titles;
    }

    private static string? IndentOfFirstScenario(string text)
    {
        var m = ScenarioLine().Match(text);
        return m.Success ? m.Groups["indent"].Value : null;
    }

    /// <summary>
    /// Splits feature text into scenario blocks. A block runs from its tag/comment lines (the
    /// contiguous block directly above the Scenario line) to the line before the next block, so
    /// Examples tables and step data stay with their scenario. Everything before the first
    /// block - the Feature header, description, Background - is dropped: it is the model repeating
    /// what the file already has.
    /// </summary>
    private static List<ScenarioBlock> ScenarioBlocks(string text)
    {
        var lines = text.Split('\n');
        var starts = new List<(int Line, int Begin, string Title, string Indent)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var m = ScenarioLine().Match(lines[i]);
            if (!m.Success)
                continue;

            var begin = i;
            while (begin > 0 && IsTagOrComment(lines[begin - 1]))
                begin--;

            // Do not reach back into the previous block's own tail.
            if (starts.Count > 0 && begin <= starts[^1].Line)
                begin = i;

            starts.Add((i, begin, m.Groups["title"].Value.Trim(), m.Groups["indent"].Value));
        }

        var blocks = new List<ScenarioBlock>();
        for (var s = 0; s < starts.Count; s++)
        {
            var end = s + 1 < starts.Count ? starts[s + 1].Begin : lines.Length;
            var body = string.Join("\n", lines[starts[s].Begin..end]).TrimEnd();
            blocks.Add(new ScenarioBlock(starts[s].Title, starts[s].Indent, body));
        }

        return blocks;
    }

    private static bool IsTagOrComment(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith('@') || t.StartsWith('#');
    }

    private static string Reindent(string text, string from, string to)
    {
        if (from == to)
            return text;

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
                continue;

            var leading = lines[i].Length - lines[i].TrimStart().Length;
            var wanted = Math.Max(0, leading - from.Length + to.Length);
            lines[i] = new string(' ', wanted) + lines[i].TrimStart();
        }

        return string.Join("\n", lines);
    }

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?:Scenario Outline|Scenario Template|Scenario)[ \t]*:[ \t]*(?<title>.*?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex ScenarioLine();

    [GeneratedRegex(@"^[ \t]*(?:Given|When|Then|And|But|\*)[ \t]+\S", RegexOptions.Multiline)]
    private static partial Regex StepLine();

    // ------------------------------------------------------------------ C#

    /// <summary>
    /// Inserts the members of the first class in <paramref name="generated"/> that the first class
    /// in <paramref name="existing"/> does not already have, plus any missing using directives and
    /// constructor statements. <paramref name="knownSteps"/> are step bindings that exist anywhere
    /// in the solution; a generated step bound to one of them is skipped.
    /// </summary>
    public static MergeResult MergeCSharp(string existing, string generated, IEnumerable<StepBinding>? knownSteps = null)
    {
        var existingTree = CSharpSyntaxTree.ParseText(existing);
        var generatedTree = CSharpSyntaxTree.ParseText(generated);

        var existingRoot = existingTree.GetCompilationUnitRoot();
        var generatedRoot = generatedTree.GetCompilationUnitRoot();

        var target = existingRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        var source = generatedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (target == null || source == null)
            return new MergeResult(existing, 0, 0, Fallback: true, "Could not find a class to merge into.");

        var present = new HashSet<string>(target.Members.Select(KeyOf));
        var steps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in target.Members.OfType<MethodDeclarationSyntax>())
            foreach (var key in StepKeys(method))
                steps.Add(key);
        if (knownSteps != null)
            foreach (var step in knownSteps)
                steps.Add($"{step.Keyword}|{step.Pattern}");

        var existingCtors = target.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        var newMembers = new List<MemberDeclarationSyntax>();
        var ctorStatements = new List<(ConstructorDeclarationSyntax Into, List<string> Statements)>();
        var skipped = 0;

        foreach (var member in source.Members)
        {
            if (member is ConstructorDeclarationSyntax ctor)
            {
                var match = existingCtors.FirstOrDefault(c => SameParameters(c.ParameterList, ctor.ParameterList));

                if (match != null)
                {
                    var missing = MissingStatements(match, ctor);
                    if (missing.Count > 0)
                        ctorStatements.Add((match, missing));
                    else
                        skipped++;
                    continue;
                }

                if (existingCtors.Count > 0)
                    return new MergeResult(existing, 0, 0, Fallback: true,
                        "The generated class needs a constructor the existing class does not have.");

                newMembers.Add(member);
                continue;
            }

            if (present.Contains(KeyOf(member)))
            {
                skipped++;
                continue;
            }

            if (member is MethodDeclarationSyntax step && StepKeys(step).Any(k => Bound(steps, k)))
            {
                skipped++;
                continue;
            }

            newMembers.Add(member);
            foreach (var key in member is MethodDeclarationSyntax m2 ? StepKeys(m2) : [])
                steps.Add(key);
        }

        var missingUsings = generatedRoot.Usings
            .Where(u => !existingRoot.Usings.Any(e => Normalise(e.ToString()) == Normalise(u.ToString())))
            .ToList();

        if (newMembers.Count == 0 && ctorStatements.Count == 0 && missingUsings.Count == 0)
            return new MergeResult(existing, 0, skipped, Fallback: false, "Nothing new to add.");

        var edits = new List<(int Position, string Text)>();
        var memberIndent = MemberIndent(target, existing);

        // New fields and constants go beside the existing ones, where a reader looks for them;
        // everything else goes at the end of the class.
        var lastField = target.Members.OfType<FieldDeclarationSyntax>().LastOrDefault();
        var fieldLike = lastField == null ? [] : newMembers.OfType<FieldDeclarationSyntax>().ToList();
        var rest = newMembers.Where(m => !fieldLike.Contains(m)).ToList();

        if (fieldLike.Count > 0)
        {
            var text = string.Concat(fieldLike.Select(f => IndentedMember(f, memberIndent)));
            edits.Add((LineEnd(existing, lastField!.Span.End), text));
        }

        if (rest.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var member in rest)
                sb.Append('\n').Append(IndentedMember(member, memberIndent));

            var close = target.CloseBraceToken;
            edits.Add((LineStart(existing, close.SpanStart), BlankLineBefore(existing, close.SpanStart) + sb.ToString().TrimStart('\n')));
        }

        foreach (var (into, statements) in ctorStatements)
        {
            if (into.Body == null)
                continue;

            var indent = memberIndent + "    ";
            var text = string.Concat(statements.Select(s => indent + s.Trim() + "\n"));
            edits.Add((LineStart(existing, into.Body.CloseBraceToken.SpanStart), text));
        }

        if (missingUsings.Count > 0)
        {
            var text = string.Concat(missingUsings.Select(u => u.ToString().Trim() + "\n"));
            var last = existingRoot.Usings.LastOrDefault();
            var position = last != null ? LineEnd(existing, last.Span.End) : 0;
            if (last == null)
                edits.Add((0, text + "\n"));
            else
                edits.Add((position, text));
        }

        var merged = new StringBuilder(existing);
        foreach (var edit in edits.OrderByDescending(e => e.Position))
            merged.Insert(edit.Position, edit.Text);

        var result = merged.ToString();

        // Insertion should never turn a file that parsed into one that does not. If it did, the
        // generated text had something this approach cannot place; leave the file alone.
        if (CountErrors(result) > CountErrors(existing))
            return new MergeResult(existing, 0, skipped, Fallback: true, "The merged file would not parse.");

        return new MergeResult(result, newMembers.Count + ctorStatements.Count, skipped, Fallback: false);
    }

    /// <summary>
    /// Renames the first class (and its constructors) so generated code that could not be merged
    /// can be written beside the original without a duplicate-type compile error.
    /// </summary>
    public static string RenameClass(string generated, string newName)
    {
        var root = CSharpSyntaxTree.ParseText(generated).GetCompilationUnitRoot();
        var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (cls == null)
            return generated;

        var oldName = cls.Identifier.ValueText;
        var spans = new List<Microsoft.CodeAnalysis.Text.TextSpan> { cls.Identifier.Span };
        spans.AddRange(cls.Members.OfType<ConstructorDeclarationSyntax>().Select(c => c.Identifier.Span));
        spans.AddRange(cls.Members.OfType<DestructorDeclarationSyntax>().Select(d => d.Identifier.Span));

        var sb = new StringBuilder(generated);
        foreach (var span in spans.OrderByDescending(s => s.Start))
        {
            sb.Remove(span.Start, span.Length);
            sb.Insert(span.Start, newName);
        }

        return oldName == newName ? generated : sb.ToString();
    }

    private static string KeyOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m =>
            $"M:{m.Identifier.ValueText}({string.Join(",", m.ParameterList.Parameters.Select(p => Normalise(p.Type?.ToString() ?? "")))})",
        ConstructorDeclarationSyntax c =>
            $"C:({string.Join(",", c.ParameterList.Parameters.Select(p => Normalise(p.Type?.ToString() ?? "")))})",
        PropertyDeclarationSyntax p => $"P:{p.Identifier.ValueText}",
        FieldDeclarationSyntax f => "F:" + string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.ValueText)),
        _ => "X:" + Normalise(member.ToString())
    };

    private static bool SameParameters(ParameterListSyntax a, ParameterListSyntax b) =>
        a.Parameters.Select(p => Normalise(p.Type?.ToString() ?? ""))
            .SequenceEqual(b.Parameters.Select(p => Normalise(p.Type?.ToString() ?? "")));

    /// <summary>Statements in the generated constructor body that the existing one does not already contain.</summary>
    private static List<string> MissingStatements(ConstructorDeclarationSyntax existing, ConstructorDeclarationSyntax generated)
    {
        var have = new HashSet<string>(
            existing.Body?.Statements.Select(s => Normalise(s.ToString())) ?? []);

        return generated.Body?.Statements
                   .Where(s => !have.Contains(Normalise(s.ToString())))
                   .Select(s => s.ToString())
                   .ToList()
               ?? [];
    }

    /// <summary>"Keyword|pattern" for each step attribute on a method.</summary>
    private static IEnumerable<string> StepKeys(MethodDeclarationSyntax method)
    {
        foreach (var attribute in method.AttributeLists.SelectMany(l => l.Attributes))
        {
            var name = attribute.Name.ToString();
            name = name[(name.LastIndexOf('.') + 1)..];
            if (name.EndsWith("Attribute", StringComparison.Ordinal))
                name = name[..^"Attribute".Length];

            if (name is not ("Given" or "When" or "Then" or "StepDefinition"))
                continue;

            var literal = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression as LiteralExpressionSyntax;
            if (literal?.Token.Value is string pattern)
                yield return $"{(name == "StepDefinition" ? "*" : name)}|{pattern}";
        }
    }

    /// <summary>A [StepDefinition] binds every keyword, so it clashes with any of the three.</summary>
    private static bool Bound(HashSet<string> steps, string key)
    {
        if (steps.Contains(key))
            return true;

        var bar = key.IndexOf('|');
        var pattern = key[(bar + 1)..];
        return steps.Contains($"*|{pattern}") ||
               (key.StartsWith("*|", StringComparison.Ordinal) &&
                (steps.Contains($"Given|{pattern}") || steps.Contains($"When|{pattern}") || steps.Contains($"Then|{pattern}")));
    }

    private static string Normalise(string text) => Regex.Replace(text, @"\s+", "");

    private static int CountErrors(string text) =>
        CSharpSyntaxTree.ParseText(text).GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);

    // ------------------------------------------------------------------ text positions

    private static int LineStart(string text, int position)
    {
        var i = text.LastIndexOf('\n', Math.Max(0, position - 1));
        return i < 0 ? 0 : i + 1;
    }

    private static int LineEnd(string text, int position)
    {
        var i = text.IndexOf('\n', position);
        return i < 0 ? text.Length : i + 1;
    }

    /// <summary>A blank separator, unless the class body opens right above the insertion point.</summary>
    private static string BlankLineBefore(string text, int closeBracePosition)
    {
        var start = LineStart(text, closeBracePosition);
        var raw = text[..start];
        var trimmed = raw.TrimEnd();
        if (trimmed.EndsWith('{'))
            return "";

        // The class already ends with a blank line before its brace: do not stack another.
        return raw[trimmed.Length..].Count(c => c == '\n') >= 2 ? "" : "\n";
    }

    private static string MemberIndent(ClassDeclarationSyntax cls, string text)
    {
        var first = cls.Members.FirstOrDefault();
        if (first == null)
            return new string(' ', LineIndent(text, cls.Span.Start).Length + 4);

        return LineIndent(text, first.GetFirstToken().SpanStart);
    }

    private static string LineIndent(string text, int position)
    {
        var start = LineStart(text, position);
        var end = start;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            end++;
        return text[start..end];
    }

    /// <summary>The member's text re-indented to the target class's member indentation, comments included.</summary>
    private static string IndentedMember(MemberDeclarationSyntax member, string indent)
    {
        var source = member.SyntaxTree.ToString();
        var firstToken = member.GetFirstToken();
        var baseIndent = LineIndent(source, firstToken.SpanStart);

        var text = member.ToFullString().ReplaceLineEndings("\n").Trim('\n');
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0)
            {
                lines[i] = "";
                continue;
            }

            var line = lines[i].StartsWith(baseIndent, StringComparison.Ordinal) ? lines[i][baseIndent.Length..] : lines[i].TrimStart();
            lines[i] = indent + line.TrimEnd();
        }

        return string.Join("\n", lines) + "\n";
    }
}
