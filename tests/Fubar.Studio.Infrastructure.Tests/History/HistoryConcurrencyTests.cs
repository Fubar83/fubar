using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.History;

namespace Fubar.Studio.Infrastructure.Tests.History;

/// <summary>
/// Appending is read-modify-write, so two windows sending the same request would both read the same
/// list and the second write would drop the first's entry - silently, and only noticed later as
/// history that "lost" a send.
/// </summary>
public class HistoryConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fubar-history-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Concurrent_appends_keep_every_entry()
    {
        Directory.CreateDirectory(_root);
        var service = new HistoryService();
        const int count = 40;

        await Task.WhenAll(Enumerable.Range(0, count).Select(i =>
            service.AppendAsync(_root, "req-1", new ExecutionSnapshot { StatusCode = 200 + i })));

        var entries = await service.LoadAsync(_root, "req-1");

        Assert.Equal(count, entries.Count);
        Assert.Equal(count, entries.Select(e => e.StatusCode).Distinct().Count());
    }

    /// <summary>Different requests must not serialise on each other - a run of twenty would otherwise
    /// queue behind the slowest ledger.</summary>
    [Fact]
    public async Task Appends_to_different_requests_do_not_interfere()
    {
        Directory.CreateDirectory(_root);
        var service = new HistoryService();

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            service.AppendAsync(_root, $"req-{i}", new ExecutionSnapshot { StatusCode = 200 })));

        foreach (var i in Enumerable.Range(0, 10))
        {
            Assert.Single(await service.LoadAsync(_root, $"req-{i}"));
        }
    }
}
