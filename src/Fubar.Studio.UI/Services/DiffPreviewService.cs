using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Fubar.Diff.Application.Comparison;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Shows <see cref="DiffPreviewDialog"/> modally over the active window. Resolves the owner lazily so
/// it does not depend on DI construction order, matching the other dialog services.
/// </summary>
public sealed class DiffPreviewService : IDiffPreviewService
{
    private readonly IFileComparisonService _comparison;

    public DiffPreviewService(IFileComparisonService comparison) => _comparison = comparison;

    public async Task ShowAsync(
        string leftText,
        string rightText,
        string leftLabel,
        string rightLabel,
        string title)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return;
        }

        var owner = lifetime.Windows.FirstOrDefault(w => w.IsActive) ?? lifetime.MainWindow;
        if (owner is null)
        {
            return;
        }

        var viewModel = new DiffPreviewViewModel(_comparison);
        var dialog = new DiffPreviewDialog(viewModel);

        // Load before showing so the window opens with content rather than flashing empty - the
        // comparison itself runs on a background thread inside the service.
        await viewModel.LoadAsync(leftText, rightText, leftLabel, rightLabel, title).ConfigureAwait(true);

        await dialog.ShowDialog(owner);
    }
}
