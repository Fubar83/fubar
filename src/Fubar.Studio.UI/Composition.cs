using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Infrastructure;
using Fubar.Controls;
using Fubar.Studio.Application.Comparison;
using Fubar.Studio.Application.Requests;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Diagnostics;
using Fubar.Studio.Infrastructure.Diagnostics;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Infrastructure;
using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.Tabs;
using Fubar.Studio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fubar.Studio.UI;

/// <summary>
/// DI composition root. All view models and <c>Fubar.Studio.Core</c>/<c>Fubar.Studio.Infrastructure</c> services
/// are registered here via a generic <see cref="IHost"/>; nothing in the UI layer new()'s up a
/// service directly - see the Extensibility Architecture section of the project plan.
/// </summary>
internal static class Composition
{
    public static IHost BuildHost(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                services.AddFubarInfrastructure();

                // Application-layer use-case services (orchestration over the Core ports above).
                services.AddSingleton<IRequestExecutionService, RequestExecutionService>();
                services.AddSingleton<IRequestComparisonSettings, RequestComparisonSettings>();
                
                // The oracles. A run picks one; none of them judges for itself.
                services.AddSingleton<NoOracle>(_ => NoOracle.Instance);
                services.AddSingleton<SnapshotOracle>();
                services.AddSingleton<ISnapshotRecordingService, SnapshotRecordingService>();
                services.AddSingleton<IBatchPlanner, BatchPlanner>();
                // One instance behind both contracts: the paired run reuses the single-environment
                // path step for step, so a difference between them could only be a bug.
                services.AddSingleton<CollectionRunService>();
                services.AddSingleton<ICollectionRunService>(s => s.GetRequiredService<CollectionRunService>());
                services.AddSingleton<IEnvironmentPairRunService>(s => s.GetRequiredService<CollectionRunService>());

                // A workspace named on the command line, read once and injected - never fished out of
                // Environment.GetCommandLineArgs() somewhere untestable.
                services.AddSingleton(StartupWorkspace.FromArgs(args));

                services.AddSingleton<IFolderPickerService, FolderPickerService>();
                services.AddSingleton<IFilePickerService, FilePickerService>();
                services.AddSingleton<IClipboardService, ClipboardService>();
                services.AddSingleton<IImportDialogService, ImportDialogService>();
                services.AddSingleton<IRunDialogService, RunDialogService>();
                services.AddSingleton<IEnvironmentComparisonDialogService, EnvironmentComparisonDialogService>();
                // One object for the request editor's dependencies: it took 24 constructor parameters,
                // which made adding one a five-place edit.
                services.AddSingleton<RequestEditorServices>();
                // Moved out of Fubar.Diff.UI: API Studio could discard an unsaved request edit
                // without asking, while Diff has had the prompt from the start.
                services.AddSingleton<IConfirmationService, ConfirmationService>();

                // The diff engine, reused for the OpenAPI import preview and response comparisons -
                // the TEXT AND JSON half only.
                //
                // This used to be AddFubarDiffInfrastructure(), which binds every adapter Fubar Diff
                // has: the folder scanner, the file copier, the change watcher, the settings stores,
                // and the Roslyn C# parser. None of them mean anything to an API client, and the last
                // put Microsoft.CodeAnalysis.CSharp (7.1 MB) and Microsoft.CodeAnalysis (3.1 MB) in
                // this application's output - roughly twelve times the size of its own assembly.
                // Fubar.Studio.Architecture.Tests now fails if the reference comes back.
                services.AddFubarDiffTextAndJson();
                services.AddSingleton<SignInService>();
                services.AddSingleton<JsonSemanticPass>();

                // CodeStructurePass is deliberately NOT registered here. It is optional on
                // FileComparisonService, so leaving it out makes the structural pass inert - and
                // API Studio compares HTTP responses and OpenAPI documents, never source files, so
                // wiring it would mean paying for a C# parse whose answer nothing in this app can
                // show. It belongs to Fubar Diff's structure panel; see docs/diff.md.
                services.AddSingleton<IFileComparisonService, FileComparisonService>();
                services.AddSingleton<IDiffPreviewService, DiffPreviewService>();
                // The one place a response is judged - see DiffResponseComparer on why the adapter
                // sits here rather than in Infrastructure.
                services.AddSingleton<IResponseComparer, DiffResponseComparer>();
                // Singleton on purpose: a response pinned on one request must survive opening another.
                services.AddSingleton<IResponseBaselineService, ResponseBaselineService>();

                // Shared across every window (one theme, one log, all stateless services).
                // The log sink is what makes "send us your log" answerable; the strip alone forgot
                // everything on exit.
                services.AddSingleton<ILogSink, RollingFileLog>();
                services.AddSingleton<StatusLogViewModel>();
                services.AddSingleton<ThemeManagerViewModel>();

                // Per-window: each window gets its own scope, so its own tab set / left pane /
                // active-editor state. WindowManager creates a scope per window (see WindowManager.cs).
                services.AddScoped<WorkspaceExplorerViewModel>();
                services.AddScoped<EnvironmentManagerViewModel>();

                // Transient: each editor showing a response owns its own pane, and two editors
                // sharing one would show the other one's last response.
                services.AddTransient<ResponsePanelViewModel>();
                services.AddSingleton<IComparisonSettingsContext, ComparisonSettingsContextFactory>();
                services.AddScoped<EnvironmentsSectionViewModel>();
                services.AddScoped<AuthProfilesSectionViewModel>();
                services.AddScoped<LeftPaneViewModel>();
                services.AddScoped<IEditorViewModelFactory, EditorViewModelFactory>();
                services.AddScoped<MainViewModel>();

                services.AddSingleton<WindowManager>();

                // App bridge that lets the reusable TabStrip move/tear-off workspace tabs across
                // windows. Singleton (spans all windows); injected into each window's MainViewModel.
                services.AddSingleton<ITabDragHost, WorkspaceTabDragHost>();
            })
            .Build();
}
