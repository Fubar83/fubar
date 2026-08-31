using System.Collections.Generic;
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

    /// <summary>
    /// Prompts for one or more existing files in ONE dialog. Empty if the user cancels.
    ///
    /// What the toolbar's single Open button uses: a comparison needs two files, and asking for them
    /// in two consecutive dialogs made the commonest action in the app a two-step ceremony. Selecting
    /// both in the same folder - which is most pairs - is now one shift-click. The caller decides what
    /// to do with a count other than two (see <see cref="ViewModels.ComparisonViewModel.OpenFilesAsync"/>);
    /// this only asks.
    /// </summary>
    Task<IReadOnlyList<string>> PickFilesAsync(string title);

    /// <summary>Prompts for a destination to write to. Returns null if the user cancels.</summary>
    Task<string?> PickSaveFileAsync(string title);

    /// <summary>Prompts for an existing folder, for a folder comparison. Returns null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);
}
