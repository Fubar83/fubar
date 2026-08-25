using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.History;

namespace Fubar.Studio.Infrastructure.Tests.History;

public class HistoryServiceTests : IDisposable
{
    private readonly string _root;
    private readonly HistoryService _sut = new();

    public HistoryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fubar-history-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_Empty_WhenNoHistoryRecorded()
    {
        var result = await _sut.LoadAsync(_root, "req1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task AppendAsync_ThenLoadAsync_ReturnsNewestFirst()
    {
        await _sut.AppendAsync(_root, "req1", new ExecutionSnapshot { StatusCode = 200 });
        await _sut.AppendAsync(_root, "req1", new ExecutionSnapshot { StatusCode = 404 });

        var result = await _sut.LoadAsync(_root, "req1");

        Assert.Equal(2, result.Count);
        Assert.Equal(404, result[0].StatusCode);
        Assert.Equal(200, result[1].StatusCode);
    }

    /// <summary>The whole point of storing the body is comparing it later, which needs it back verbatim.</summary>
    [Fact]
    public async Task AppendAsync_RoundTripsTheResponseBody()
    {
        await _sut.AppendAsync(_root, "req1", new ExecutionSnapshot { StatusCode = 200, ResponseBody = "{\n  \"id\": 1\n}" });

        var result = await _sut.LoadAsync(_root, "req1");

        Assert.Equal("{\n  \"id\": 1\n}", result[0].ResponseBody);
    }

    [Fact]
    public async Task AppendAsync_DoesNotLeakAcrossDifferentRequestIds()
    {
        await _sut.AppendAsync(_root, "req1", new ExecutionSnapshot { StatusCode = 200 });
        await _sut.AppendAsync(_root, "req2", new ExecutionSnapshot { StatusCode = 500 });

        var req1History = await _sut.LoadAsync(_root, "req1");

        Assert.Single(req1History);
        Assert.Equal(200, req1History[0].StatusCode);
    }
}
