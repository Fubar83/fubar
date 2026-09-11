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
    /// <param name="preferredLeft">The environment to start on the left, by NAME, or null to let the
    /// window pick. A batch already says which pair it is about, and asking again from a window opened
    /// from that batch is a question the file has answered.</param>
    /// <param name="preferredRight">The same for the right-hand side.</param>
    void Show(
        RunPlan plan,
        Workspace workspace,
        IReadOnlyList<WorkspaceEnvironment> environments,
        string target,
        string? preferredLeft = null,
        string? preferredRight = null);
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
        string target,
        string? preferredLeft = null,
        string? preferredRight = null)
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
                _pairRun, _comparison, _comparer, _services.ComparisonSettingsContext, plan, workspace, environments, target, _confirmation,
                preferredLeft, preferredRight));

        window.Show(owner);
    }
}
