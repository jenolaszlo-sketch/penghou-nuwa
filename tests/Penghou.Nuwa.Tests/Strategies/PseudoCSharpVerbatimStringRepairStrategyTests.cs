using System.Text.Json;
using FluentAssertions;
using Penghou.Nuwa.Strategies;

namespace Penghou.Nuwa.Tests.Strategies;

public sealed class PseudoCSharpVerbatimStringRepairStrategyTests
{
    private readonly PseudoCSharpVerbatimStringRepairStrategy _sut = new();

    // ---------------------------------------------------------------
    // Guard clauses
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RepairAsync_NullOrWhitespaceInput_ReturnsNotApplicable(string? input)
    {
        var attempt = Repair(_sut, input!);

        attempt.Outcome.Should()
            .Be(RepairOutcome.NotApplicable);
    }

    [Fact]
    public void RepairAsync_NoVerbatimLiteral_ReturnsNotApplicable()
    {
        const string input = """{"name": "emit_files", "arguments": {"content": "already fine"}}""";

        var attempt = Repair(_sut, input);

        attempt.Outcome.Should()
            .Be(RepairOutcome.NotApplicable);
    }

    // ---------------------------------------------------------------
    // Happy path: single verbatim literal, nothing else wrong
    // ---------------------------------------------------------------

    [Fact]
    public void RepairAsync_SingleVerbatimLiteral_NoOtherDefects_ProducesFullyValidJson()
    {
        const string input = "{\"content\": @\"line1\nline2\"}";

        var attempt = Repair(_sut, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);
        repaired.Should().NotContain("@\"");

        var act = () => JsonDocument.Parse(repaired);
        act.Should().NotThrow();

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("content").GetString().Should().Be("line1\nline2");
    }

    [Fact]
    public void RepairAsync_DoubledInnerQuotes_AreNormalizedToSingleEscapedQuotes()
    {
        // Real C# verbatim escaping ("") for an embedded quote, partially applied by the model.
        const string input = "{\"content\": @\"Console.WriteLine(\"\"Hello, World!\"\");\"}";

        var attempt = Repair(_sut, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("content").GetString()
            .Should().Be("Console.WriteLine(\"Hello, World!\");");
    }

    // ---------------------------------------------------------------
    // Regression: nearest terminator must win, even when a farther
    // candidate would also produce technically-valid JSON.
    //
    // Without OrderBy(ascending) + first-candidate preference, the far
    // candidate gets tried first, silently swallows "notes" into
    // "content", and the result is valid JSON with the WRONG shape —
    // worse than failing, because nothing downstream flags it.
    // ---------------------------------------------------------------

    [Fact]
    public void RepairAsync_PrefersNearestTerminator_OverFartherOneThatAlsoParses()
    {
        const string input = "{\"content\": @\"hello\", \"notes\": \"world\"}";

        var attempt = Repair(_sut, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("content").GetString().Should().Be("hello");
        doc.RootElement.GetProperty("notes").GetString().Should().Be("world");

        // The regression this guards against: "notes" silently merged into "content".
        doc.RootElement.GetProperty("content").GetString().Should().NotContain("notes");
    }

    // ---------------------------------------------------------------
    // Partial fix: verbatim string converted correctly, but a separate
    // structural defect (missing closing bracket) elsewhere means the
    // result can never be made fully valid by this strategy alone.
    // Per the ITextRepair contract, this must still report
    // "changed" so the tolerant syntax-tree parser gets a chance downstream —
    // it must NOT discard the conversion just because full validity
    // wasn't reached.
    // ---------------------------------------------------------------

    // Line breaks are explicit "\n" escapes (not a multi-line raw string literal) so this
    // test is identical regardless of the .cs file's on-disk line-ending setting (CRLF vs LF).
    private const string StructuralDefectSourceContent =
        "using System;\nnamespace Solo.Generated\n{\n    public class HelloWorld\n    {\n" +
        "        public static void Main(string[] args)\n        {\n" +
        "            Console.WriteLine(\"Hello, World!\");\n        }\n    }\n}";

    [Fact]
    public void RepairAsync_VerbatimLiteralWithUnrelatedStructuralDefect_StillConvertsAndReportsChanged()
    {
        var csharpVerbatimSource = StructuralDefectSourceContent.Replace("\"", "\"\"");

        var input =
            "{\"name\": \"emit_files\", \"arguments\": {\"files\": [{\"path\": \"HelloWorld.cs\", " +
            "\"content\": @\"" + csharpVerbatimSource + "\"}, " +
            "\"notes\": \"Generated a C# file with a HelloWorld class and a Main method.\"}}";

        var attempt = Repair(_sut, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired,
                "the strategy converted the verbatim string, which counts as a change" +
                " even though the missing ']' elsewhere means full validity isn't reached yet");

        repaired.Should().NotContain("@\"");

        // Compare against what JsonSerializer actually produces, rather than a hand-typed
        // escaped literal — .NET's default encoder may legitimately choose \" or \u0022.
        var expectedContentJson = JsonSerializer.Serialize(StructuralDefectSourceContent);
        repaired.Should().Contain(expectedContentJson);

        // The array is still unclosed — that's the tolerant syntax-tree parser's job, not this one's.
        var act = () => JsonDocument.Parse(repaired);
        act.Should().Throw<JsonException>("the missing ']' on \"files\" is a separate defect" +
                                           " this strategy is not responsible for fixing");
    }

    [Fact]
    public void RepairAsync_VerbatimLiteralWithUnrelatedStructuralDefect_DoesNotInsertOrRemoveWhitespace()
    {
        var csharpVerbatimSource = StructuralDefectSourceContent.Replace("\"", "\"\"");

        // "extra" is a sibling key inside the SAME still-open object (missing the
        // final closing brace) — matching the real HelloWorld.cs defect shape, where
        // a sibling key sits inside an unclosed structure. Trailing garbage after an
        // already-closed object is a different (and here, misleading) shape: a farther
        // candidate can accidentally produce syntactically valid JSON by swallowing it,
        // short-circuiting past the correct nearer match.
        var input = "{\"content\": @\"" + csharpVerbatimSource + "\", \"extra\": \"trailing\"";

        var repaired = Repair(_sut, input).Repaired!;

        // Guard against accidental reformatting (e.g. inserted/removed line breaks) creeping
        // into generated source content during repair — the pipeline must be byte-faithful.
        var expectedContentJson = JsonSerializer.Serialize(StructuralDefectSourceContent);
        repaired.Should().Contain(expectedContentJson,
            "no characters in the original verbatim source should be added, removed, or reformatted");
    }

    // ---------------------------------------------------------------
    // Multiple literals: cascading recursion should fix both when the
    // combination is otherwise well-formed.
    // ---------------------------------------------------------------

    [Fact]
    public void RepairAsync_MultipleVerbatimLiterals_BothConverted_WhenResultIsOtherwiseValid()
    {
        const string input = "{\"files\": [{\"content\": @\"first\"}, {\"content\": @\"second\"}]}";

        var attempt = Repair(_sut, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);
        repaired.Should().NotContain("@\"");

        using var doc = JsonDocument.Parse(repaired);
        var files = doc.RootElement.GetProperty("files");
        files.GetArrayLength().Should().Be(2);
        files[0].GetProperty("content").GetString().Should().Be("first");
        files[1].GetProperty("content").GetString().Should().Be("second");
    }

    // ---------------------------------------------------------------
    // Depth guard: shouldn't infinite-loop or stack overflow on
    // pathological input with many unterminated-looking literals.
    // ---------------------------------------------------------------

    [Fact]
    public void RepairAsync_ManyUnterminatedLookingLiterals_DoesNotThrowOrHang()
    {
        var input = "{\"content\": " + string.Concat(Enumerable.Repeat("@\"x", 100)) + "\"}";

        var act = () => Repair(_sut, input);

        act.Should().NotThrow();
    }

    private static TextRepairAttempt Repair(
        ITextRepair strategy,
        string input) =>
        strategy.RepairAsync(input).GetAwaiter().GetResult();
}
