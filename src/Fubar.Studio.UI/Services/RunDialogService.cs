using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;

namespace Fubar.Studio.UI.Services;

/// <summary>Opens the Run window for a plan.</summary>
public interface IRunDialogService
{
    void Show(RunPlan plan, Workspace workspace, WorkspaceEnvironment? environment, string target);
}

/// <summary>
/// Shows <see cref="CollectionRunWindow"/> over the active window.
///
/// <para><b>Shown, not ShowDialog'd.</b> A run can take minutes; a modal would block the request editor
/// for its duration, which is exactly where you want to be while watching one request fail. The owner is
/// still set so the window stays with the app rather than becoming a stray top-level.</para>
/// </summary>
public sealed class RunDialogService : IRunDialogService
{
    private readonly ICollectionRunService _runService;

    public RunDialogService(ICollectionRunService runService)
    {
        _runService = runService;
    }

    public void Show(RunPlan plan, Workspace workspace, WorkspaceEnvironment? environment, string target)
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

        var window = new CollectionRunWindow(
            new CollectionRunViewModel(_runService, plan, workspace, environment, target));

        window.Show(owner);
    }
}
