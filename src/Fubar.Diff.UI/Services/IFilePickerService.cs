using System.Threading.Tasks;

namespace Fubar.Diff.UI.Services;

/// <summary>
/// Opens the platform file picker. An interface so view models stay testable and never reach for a
/// window handle themselves.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Prompts for a single existing file. Returns null if the user cancels.</summary>
    Task<string?> PickFileAsync(string title);
}
