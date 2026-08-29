using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The Json view's detail pane isolates a change's own lines rather than showing the whole document -
/// these pin the line-extraction and renumbering <see cref="JsonSpanExcerpt.Build"/> does for it.
/// </summary>
public class JsonSpanExcerptTests
{
    private const string Document = "line1\nline2\nline3\nline4\nline5";

    [Fact]
    public void A_single_line_span_extracts_just_that_line()
    {
        var (text, span) = JsonSpanExcerpt.Build(Document, new SourceSpan(3, 2, 3, 5));

        Assert.Equal("line3", text);
        Assert.Equal(new SourceSpan(1, 2, 1, 5), span);
    }

    [Fact]
    public void A_multi_line_span_extracts_every_line_it_covers()
    {
        var (text, span) = JsonSpanExcerpt.Build(Document, new SourceSpan(2, 3, 4, 1));

        Assert.Equal("line2\nline3\nline4", text);
        Assert.Equal(new SourceSpan(1, 3, 3, 1), span);
    }

    [Fact]
    public void A_span_already_starting_at_line_one_needs_no_renumbering()
    {
        var (text, span) = JsonSpanExcerpt.Build(Document, new SourceSpan(1, 1, 2, 1));

        Assert.Equal("line1\nline2", text);
        Assert.Equal(new SourceSpan(1, 1, 2, 1), span);
    }

    [Fact]
    public void An_unknown_span_yields_an_empty_excerpt()
    {
        var (text, span) = JsonSpanExcerpt.Build(Document, SourceSpan.None);

        Assert.Equal(string.Empty, text);
        Assert.Equal(SourceSpan.None, span);
        Assert.False(span.IsKnown);
    }

    /// <summary>
    /// Columns are untouched - only line numbers shift - since RawJsonPane's highlight renderer reads
    /// nothing but the line range.
    /// </summary>
    [Fact]
    public void Columns_pass_through_unchanged()
    {
        var (_, span) = JsonSpanExcerpt.Build(Document, new SourceSpan(4, 7, 5, 12));

        Assert.Equal(7, span.StartColumn);
        Assert.Equal(12, span.EndColumn);
    }
}
