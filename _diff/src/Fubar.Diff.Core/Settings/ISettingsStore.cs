using System.Threading;
using System.Threading.Tasks;

namespace Fubar.Diff.Core.Settings;

/// <summary>
/// PORT. Loads and saves <see cref="AppSettings"/>.
///
/// Both halves are deliberately forgiving: settings are a convenience, and no failure to read or write
/// them is worth interrupting the user's work over. <see cref="Load"/> returns defaults rather than
/// throwing, and <see cref="SaveAsync"/> reports failure without raising.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Reads the settings, returning <see cref="AppSettings.Default"/> when there are none or they
    /// cannot be read.
    ///
    /// Synchronous on purpose: it is called once at startup on the UI thread before the dispatcher is
    /// pumping, where awaiting would deadlock - the continuation could never resume on the very thread
    /// that is blocked waiting for it.
    /// </summary>
    AppSettings Load();

    /// <summary>Writes the settings. Returns false if it could not, rather than throwing.</summary>
    Task<bool> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
