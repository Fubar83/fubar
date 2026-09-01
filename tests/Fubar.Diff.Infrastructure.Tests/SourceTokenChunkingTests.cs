using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// The inline differ once it is told what language it is looking at. Everything here is about WHERE
/// the highlight lands, which is the entire value of a character-level diff - a span that covers the
/// wrong run of characters is worse than no span at all, because it is confidently wrong.
/// </summary>
public class SourceTokenChunkingTests
{
    private readonly DiffPlexInlineDiffEngine _engine = new();

    private static string[] Selected(string text, IReadOnlyList<CharSpan> spans) =>
        [.. spans.Select(s => text.Substring(s.Start, s.Length))];

    private (string[] Left, string[] Right) Diff(string left, string right, SourceLanguage language)
    {
        var (leftSpans, rightSpans) = _engine.DiffWithinLine(left, right, language);

        // The contract every consumer relies on, checked on every case below rather than once.
        foreach (var span in leftSpans)
        {
            Assert.True(span.Start >= 0 && span.End <= left.Length, "a span must address the string it was computed from");
        }

        foreach (var span in rightSpans)
        {
            Assert.True(span.Start >= 0 && span.End <= right.Length, "a span must address the string it was computed from");
        }

        return (Selected(left, leftSpans), Selected(right, rightSpans));
    }

    [Fact]
    public void A_comparison_operator_that_gained_a_character_highlights_as_one_operator()
    {
        // The case that motivates the whole token chunker. Split on punctuation, "==" and "===" share
        // their first two characters, so the only thing highlighted is a lone third "=" - the least
        // legible possible rendering of a change in what a comparison MEANS.
        var (_, right) = Diff("if (a == b) {", "if (a === b) {", SourceLanguage.JavaScript);

        Assert.Contains("===", right);
    }

    [Fact]
    public void An_operator_that_grew_highlights_as_the_new_operator()
    {
        // Same shape, different mistake: a boundary condition changing from > to >= is a one-character
        // edit whose whole meaning is in the operator. Highlighting the lone "=" says nothing.
        var (left, right) = Diff("items.filter(x => x > 0)", "items.filter(x => x >= 0)", SourceLanguage.TypeScript);

        Assert.Contains(">", left);
        Assert.Contains(">=", right);
    }

    [Fact]
    public void A_changed_identifier_is_highlighted_whole()
    {
        var (left, right) = Diff("var total = count;", "var total = amount;", SourceLanguage.CSharp);

        Assert.Contains("count", left);
        Assert.Contains("amount", right);
    }

    [Fact]
    public void One_word_of_a_message_does_not_highlight_the_whole_string()
    {
        // Strings are a single token to the scanner, and rightly so - but a sentence is not one
        // indivisible thing to a reader, so the chunker breaks literals down into words.
        var (_, right) = Diff(
            "throw new Exception(\"could not open the file\");",
            "throw new Exception(\"could not read the file\");",
            SourceLanguage.CSharp);

        Assert.Contains("read", right);
        Assert.DoesNotContain(right, s => s.Contains("could not", StringComparison.Ordinal));
    }

    [Fact]
    public void A_changed_string_still_highlights_when_the_code_around_it_did_not()
    {
        var (left, right) = Diff("var s = \"a\";", "var s = \"b\";", SourceLanguage.CSharp);

        Assert.Contains(left, s => s.Contains('a', StringComparison.Ordinal));
        Assert.Contains(right, s => s.Contains('b', StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_language_still_produces_a_correct_answer()
    {
        // The port's promise: the language only changes how FINELY the difference is reported.
        var (left, right) = Diff("the quick fox", "the slow fox", SourceLanguage.None);

        Assert.Contains("quick", left);
        Assert.Contains("slow", right);
    }

    [Fact]
    public void Identical_code_lines_produce_no_spans()
    {
        var (left, right) = Diff("var x = 1;", "var x = 1;", SourceLanguage.CSharp);

        Assert.Empty(left);
        Assert.Empty(right);
    }

    [Fact]
    public void A_null_coalescing_assignment_is_one_token()
    {
        var (_, right) = Diff("x = y;", "x ??= y;", SourceLanguage.CSharp);

        Assert.Contains("??=", right);
    }
}
