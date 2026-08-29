using System.Threading.Tasks;

namespace Fubar.Diff.UI.Services;

/// <summary>
/// Opens the platform file pickers. An interface so view models stay testable and never reach for a
/// window handle themselves.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Prompts for a single existing file. Returns null if the user cancels.</summary>
    Task<string?> PickFileAsync(string title);

    /// <summary>Prompts for a destination to write to. Returns null if the user cancels.</summary>
    Task<string?> PickSaveFileAsync(string title);

    /// <summary>Prompts for an existing folder, for a folder comparison. Returns null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);
}
