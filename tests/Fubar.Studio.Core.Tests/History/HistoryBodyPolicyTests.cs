using Fubar.Studio.Core.History;

namespace Fubar.Studio.Core.Tests.History;

public class HistoryBodyPolicyTests
{
    [Fact]
    public void Capture_KeepsAnOrdinaryBody()
    {
        Assert.Equal("{\"id\":1}", HistoryBodyPolicy.Capture("{\"id\":1}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Capture_DropsNothingToCompare(string? body)
    {
        Assert.Null(HistoryBodyPolicy.Capture(body));
    }

    [Fact]
    public void Capture_KeepsABodyExactlyAtTheCap()
    {
        var body = new string('x', HistoryBodyPolicy.MaxResponseBodyChars);

        Assert.Equal(body, HistoryBodyPolicy.Capture(body));
    }

    /// <summary>
    /// Dropped, not truncated: 200 entries per request times an unbounded body would balloon the
    /// workspace, and half a document cannot be meaningfully diffed.
    /// </summary>
    [Fact]
    public void Capture_DropsABodyOverTheCap()
    {
        var body = new string('x', HistoryBodyPolicy.MaxResponseBodyChars + 1);

        Assert.Null(HistoryBodyPolicy.Capture(body));
    }
}
