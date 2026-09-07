using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;

namespace Fubar.Studio.UI.Services;

/// <summary>Shows <see cref="ImportOpenApiDialog"/> modally over the active window, wiring it to a fresh
/// <see cref="ImportOpenApiViewModel"/>, and returns the user's confirmed <see cref="ImportDialogResult"/>
/// (or null on cancel). Resolves the owner window lazily so it doesn't depend on DI construction order.</summary>
public sealed class ImportDialogService : IImportDialogService
{
    private readonly IOpenApiImportService _openApi;
    private readonly IPostmanImportService _postman;
    private readonly IImportApplyService _apply;
    private readonly IFilePickerService _filePicker;
    private readonly IRequestStore _requests;
    private readonly IRequestSerializer _serializer;
    private readonly IDiffPreviewService _diffPreview;

    public ImportDialogService(
        IOpenApiImportService openApi,
        IPostmanImportService postman,
        IImportApplyService apply,
        IFilePickerService filePicker,
        IRequestStore requests,
        IRequestSerializer serializer,
        IDiffPreviewService diffPreview)
    {
        _openApi = openApi;
        _postman = postman;
        _apply = apply;
        _filePicker = filePicker;
        _requests = requests;
        _serializer = serializer;
        _diffPreview = diffPreview;
    }

    public Task<ImportDialogResult?> ShowAsync(string workspaceRoot) => ShowAsync(_openApi, workspaceRoot);

    public Task<ImportDialogResult?> ShowPostmanAsync(string workspaceRoot) => ShowAsync(_postman, workspaceRoot);

    /// <summary>One dialog, whichever planner produced the plan - the preview, the tick boxes and the
    /// apply are the same work regardless of what was read.</summary>
    private async Task<ImportDialogResult?> ShowAsync(IImportPlanner planner, string workspaceRoot)
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

        var viewModel = new ImportOpenApiViewModel(
            planner, _apply, _filePicker, _requests, _serializer, _diffPreview, workspaceRoot);
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
