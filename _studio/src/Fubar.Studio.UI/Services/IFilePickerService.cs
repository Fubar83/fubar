namespace Fubar.Studio.UI.Services;

/// <summary>Abstracts the native "Save File" dialog behind an interface view models can depend on
/// (same pattern as <see cref="IFolderPickerService"/>).</summary>
public interface IFilePickerService
{
    /// <summary>Shows a native save-file dialog and returns the chosen local path, or null if cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName);

    /// <summary>Shows a native open-file dialog (any file type) and returns the chosen local path,
    /// or null if cancelled - used by the Body tab's Binary File type.</summary>
    Task<string?> PickOpenFileAsync(string title);
}
