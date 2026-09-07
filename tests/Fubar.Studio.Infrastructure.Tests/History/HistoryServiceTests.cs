using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;
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

    [Fact]
    public async Task The_ledger_is_trimmed_to_the_user_s_limit_not_the_built_in_one()
    {
        // The cap was a constant. It is what stops a workspace turning into a cache nobody asked for,
        // and 200 executions of a request returning a large payload is a lot of disk for someone who
        // only ever wanted the last few.
        var sut = new HistoryService(new FixedSettings(new HistorySettings { MaxEntriesPerRequest = 3 }));

        for (var i = 0; i < 6; i++)
        {
            await sut.AppendAsync(_root, "req1", new ExecutionSnapshot { StatusCode = 200 + i });
        }

        var kept = await sut.LoadAsync(_root, "req1");

        Assert.Equal(3, kept.Count);
        Assert.Equal(205, kept[0].StatusCode); // newest first, oldest evicted
    }

    [Fact]
    public async Task A_limit_of_zero_still_keeps_the_last_execution()
    {
        // Clamped, unlike the body cap. "Record history but keep none of it" is a slip rather than a
        // request - the setting for wanting nothing kept is History.Enabled, which writes no file.
        var sut = new HistoryService(new FixedSettings(new HistorySettings { MaxEntriesPerRequest = 0 }));

        await sut.AppendAsync(_root, "req1", new ExecutionSnapshot { StatusCode = 200 });
        await sut.AppendAsync(_root, "req1", new ExecutionSnapshot { StatusCode = 201 });

        Assert.Equal(201, Assert.Single(await sut.LoadAsync(_root, "req1")).StatusCode);
    }

    /// <summary>Settings that never change, so the limit under test is the only thing in play.</summary>
    private sealed class FixedSettings(HistorySettings history) : IAppSettingsService
    {
        private readonly AppSettings _settings = new() { History = history };

        public AppSettings Load() => _settings;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
