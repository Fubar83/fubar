using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// The character-span contract. What matters here is that offsets address the strings the engine was
/// GIVEN - a span that is off by even one character highlights the wrong text, and does so silently.
/// </summary>
public class DiffPlexInlineDiffEngineTests
{
    private readonly DiffPlexInlineDiffEngine _engine = new();

    /// <summary>The substrings a set of spans actually selects, which is what the user ends up seeing.</summary>
    private static string[] Selected(string text, IReadOnlyList<CharSpan> spans) =>
        [.. spans.Select(s => text.Substring(s.Start, s.Length))];

    [Fact]
    public void Identical_lines_produce_no_spans()
    {
        var (left, right) = _engine.DiffWithinLine("same text", "same text");

        Assert.Empty(left);
        Assert.Empty(right);
    }

    [Fact]
    public void Two_empty_lines_produce_no_spans()
    {
        var (left, right) = _engine.DiffWithinLine(string.Empty, string.Empty);

        Assert.Empty(left);
        Assert.Empty(right);
    }

    [Fact]
    public void A_changed_word_is_selected_on_each_side()
    {
        var (left, right) = _engine.DiffWithinLine("the quick fox", "the slow fox");

        Assert.Contains("quick", Selected("the quick fox", left));
        Assert.Contains("slow", Selected("the slow fox", right));
    }

    [Fact]
    public void Unchanged_words_are_not_selected()
    {
        const string leftText = "the quick fox";
        var (left, _) = _engine.DiffWithinLine(leftText, "the slow fox");

        var selected = string.Concat(Selected(leftText, left));

        Assert.DoesNotContain("the", selected, StringComparison.Ordinal);
        Assert.DoesNotContain("fox", selected, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_span_lies_within_its_string()
    {
        // The silent-corruption guard: an out-of-range span throws inside a render pass, which takes
        // the window down rather than merely looking wrong.
        const string leftText = "{ \"name\": \"alpha\", \"count\": 1 }";
        const string rightText = "{ \"name\": \"omega\", \"count\": 42 }";

        var (left, right) = _engine.DiffWithinLine(leftText, rightText);

        Assert.All(left, s =>
        {
            Assert.InRange(s.Start, 0, leftText.Length);
            Assert.InRange(s.End, 0, leftText.Length);
            Assert.True(s.Length > 0);
        });

        Assert.All(right, s =>
        {
            Assert.InRange(s.Start, 0, rightText.Length);
            Assert.InRange(s.End, 0, rightText.Length);
            Assert.True(s.Length > 0);
        });
    }

    [Fact]
    public void Spans_are_ordered_and_do_not_overlap()
    {
        const string leftText = "alpha beta gamma delta";
        var (left, _) = _engine.DiffWithinLine(leftText, "alpha BETA gamma DELTA");

        var previousEnd = 0;
        foreach (var span in left)
        {
            Assert.True(span.Start >= previousEnd, "spans must be ordered and non-overlapping");
            previousEnd = span.End;
        }
    }

    [Fact]
    public void Deletions_are_marked_on_the_left_and_insertions_on_the_right()
    {
        var (left, right) = _engine.DiffWithinLine("alpha", "omega");

        Assert.All(left, s => Assert.Equal(ChangeKind.Deleted, s.Kind));
        Assert.All(right, s => Assert.Equal(ChangeKind.Inserted, s.Kind));
    }

    [Fact]
    public void A_line_added_against_an_empty_one_selects_the_whole_addition()
    {
        const string rightText = "brand new";
        var (left, right) = _engine.DiffWithinLine(string.Empty, rightText);

        Assert.Empty(left);
        Assert.Equal(rightText, string.Concat(Selected(rightText, right)));
    }

    [Fact]
    public void Punctuation_splits_words_so_a_value_change_does_not_select_the_whole_pair()
    {
        // Without punctuation separators the whole `"key": "value"` run reads as one chunk, and
        // changing the value highlights the key too.
        const string leftText = "\"key\": \"before\"";
        var (left, _) = _engine.DiffWithinLine(leftText, "\"key\": \"after\"");

        Assert.DoesNotContain("key", string.Concat(Selected(leftText, left)), StringComparison.Ordinal);
    }
}
