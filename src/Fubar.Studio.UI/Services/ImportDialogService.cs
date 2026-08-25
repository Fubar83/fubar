using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Fubar.Studio.Core.Import;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;

namespace Fubar.Studio.UI.Services;

/// <summary>Shows <see cref="ImportOpenApiDialog"/> modally over the active window, wiring it to a fresh
/// <see cref="ImportOpenApiViewModel"/>, and returns the user's confirmed <see cref="ImportDialogResult"/>
/// (or null on cancel). Resolves the owner window lazily so it doesn't depend on DI construction order.</summary>
public sealed class ImportDialogService : IImportDialogService
{
    private readonly IOpenApiImportService _import;
    private readonly IFilePickerService _filePicker;

    public ImportDialogService(IOpenApiImportService import, IFilePickerService filePicker)
    {
        _import = import;
        _filePicker = filePicker;
    }

    public async Task<ImportDialogResult?> ShowAsync(string workspaceRoot)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return null;
        }

        var owner = lifetime.Windows.FirstOrDefault(w => w.IsActive) ?? lifetime.MainWindow;
        if (owner is null)
        {
            return null;
        }

        var viewModel = new ImportOpenApiViewModel(_import, _filePicker, workspaceRoot);
        var dialog = new ImportOpenApiDialog(viewModel);
        return await dialog.ShowDialog<ImportDialogResult?>(owner);
    }

    public async Task<string?> ShowCurlAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return null;
        }

        var owner = lifetime.Windows.FirstOrDefault(w => w.IsActive) ?? lifetime.MainWindow;
        if (owner is null)
        {
            return null;
        }

        return await new CurlImportDialog().ShowDialog<string?>(owner);
    }
}
