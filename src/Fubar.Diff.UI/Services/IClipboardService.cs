using System.Threading.Tasks;

namespace Fubar.Diff.UI.Services;

/// <summary>
/// Puts text on the system clipboard. An interface so view models stay testable and never reach for a
/// window handle themselves - the same bargain <see cref="IFilePickerService"/> makes.
/// </summary>
public interface IClipboardService
{
    /// <summary>Copies text, or does nothing when there is no window to copy through.</summary>
    Task SetTextAsync(string text);
}
