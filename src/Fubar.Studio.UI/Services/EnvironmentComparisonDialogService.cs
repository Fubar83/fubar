using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Fubar.Diff.Application.Comparison;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;

namespace Fubar.Studio.UI.Services;

/// <summary>Opens the environment-comparison window for a plan.</summary>
public interface IEnvironmentComparisonDialogService
{
    void Show(
        RunPlan plan,
        Workspace workspace,
        IReadOnlyList<WorkspaceEnvironment> environments,
        string target);
}

/// <summary>
/// Shows <see cref="EnvironmentComparisonWindow"/> over the active window.
///
/// <para>Shown rather than ShowDialog'd, like the Run window: a comparison run takes as long as two
/// runs, and the whole point of it is to work on rules while it is still going - a modal would make the
/// request it is telling you about unreachable.</para>
/// </summary>
public sealed class EnvironmentComparisonDialogService : IEnvironmentComparisonDialogService
{
    private readonly IEnvironmentPairRunService _pairRun;
    private readonly IFileComparisonService _comparison;
    private readonly IResponseComparer _comparer;
    private readonly RequestEditorServices _services;
    private readonly Fubar.Controls.IConfirmationService? _confirmation;

    public EnvironmentComparisonDialogService(
        IEnvironmentPairRunService pairRun,
        IFileComparisonService comparison,
        IResponseComparer comparer,
        RequestEditorServices services,
        Fubar.Controls.IConfirmationService? confirmation = null)
    {
        _pairRun = pairRun;
        _comparison = comparison;
        _comparer = comparer;
        _services = services;
        _confirmation = confirmation;
    }

    public void Show(
        RunPlan plan,
        Workspace workspace,
        IReadOnlyList<WorkspaceEnvironment> environments,
        string target)
    {
        // Resolved lazily rather than injected, so this does not depend on DI construction order.
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return;
        }

        var owner = lifetime.Windows.FirstOrDefault(w => w.IsActive) ?? lifetime.MainWindow;
        if (owner is null)
        {
            return;
        }

        var window = new EnvironmentComparisonWindow(
            new EnvironmentComparisonViewModel(
                _pairRun, _comparison, _comparer, _services, plan, workspace, environments, target, _confirmation));

        window.Show(owner);
    }
}
