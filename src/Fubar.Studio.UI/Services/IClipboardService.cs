namespace Fubar.Studio.UI.Services;

/// <summary>Abstracts OS clipboard access behind an interface view models can depend on (same
/// pattern as <see cref="IFolderPickerService"/>).</summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}
