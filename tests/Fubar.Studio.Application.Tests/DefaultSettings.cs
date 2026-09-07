using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;

namespace Fubar.Studio.Application.Tests;

/// <summary>
/// Settings as a first run has them, held in memory.
///
/// <para>These tests are about the send pipeline's orchestration, not about preferences - but the
/// pipeline now reads two of them (whether history is recorded at all, and how much of a body it
/// keeps), so it needs an answer rather than a null.</para>
/// </summary>
internal sealed class DefaultSettings : IAppSettingsService
{
    private AppSettings _settings = new();

    public static DefaultSettings Instance { get; } = new();

    /// <summary>A service whose history is switched off, for the test that asserts nothing is written.</summary>
    public static DefaultSettings WithHistoryDisabled() =>
        new() { _settings = new AppSettings { History = new HistorySettings { Enabled = false } } };

    /// <summary>A service with the response-body cap set, including to zero - which is a real setting
    /// ("keep the executions, keep no payloads") and not a value to be clamped away.</summary>
    public static DefaultSettings WithHistoryBodyKilobytes(int kilobytes) =>
        new() { _settings = new AppSettings { History = new HistorySettings { MaxResponseBodyKilobytes = kilobytes } } };

    public AppSettings Load() => _settings;

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        return Task.CompletedTask;
    }
}
