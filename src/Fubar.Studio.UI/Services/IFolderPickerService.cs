namespace Fubar.Studio.UI.Services;

/// <summary>Abstracts the native folder/file-picker dialogs behind an interface view models can depend on.</summary>
public interface IFolderPickerService
{
    /// <summary>Shows a native folder picker and returns the chosen local path, or null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Shows a native file picker filtered to <paramref name="fileName"/> and returns the
    /// chosen file's local path, or null if cancelled.</summary>
    Task<string?> PickFileAsync(string title, string fileName);
}
