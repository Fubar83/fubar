using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Settings;

/// <summary>Loads/saves Fubar's global user preferences (theme, etc.) - see <see cref="AppSettings"/>.</summary>
public interface IAppSettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous load, for the one call site that genuinely needs it: applying the persisted
    /// theme before the first frame renders, during <c>App.OnFrameworkInitializationCompleted</c>.
    /// That runs on the UI thread before Avalonia's dispatcher loop is pumping, so blocking on the
    /// async path there (<c>LoadAsync().GetAwaiter().GetResult()</c>) deadlocks: each awaited
    /// continuation tries to resume on the UI thread, which is itself blocked waiting for it.
    /// </summary>
    AppSettings Load();
}
