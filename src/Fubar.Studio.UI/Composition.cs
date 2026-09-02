using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Infrastructure;
using Fubar.Controls;
using Fubar.Studio.Application.Requests;
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

                services.AddSingleton<IFolderPickerService, FolderPickerService>();
                services.AddSingleton<IFilePickerService, FilePickerService>();
                services.AddSingleton<IClipboardService, ClipboardService>();
                services.AddSingleton<IImportDialogService, ImportDialogService>();

                // The diff engine, reused for the OpenAPI import preview and response comparisons.
                // AddFubarDiffInfrastructure binds its Core ports (diff engine, JSON parser, text
                // normalizer) exactly as it does inside Fubar Diff.
                services.AddFubarDiffInfrastructure();
                services.AddSingleton<SignInService>();
                services.AddSingleton<JsonSemanticPass>();

                // CodeStructurePass is deliberately NOT registered here. It is optional on
                // FileComparisonService, so leaving it out makes the structural pass inert - and
                // API Studio compares HTTP responses and OpenAPI documents, never source files, so
                // wiring it would mean paying for a C# parse whose answer nothing in this app can
                // show. It belongs to Fubar Diff's structure panel; see docs/diff.md.
                services.AddSingleton<IFileComparisonService, FileComparisonService>();
                services.AddSingleton<IDiffPreviewService, DiffPreviewService>();
                // Singleton on purpose: a response pinned on one request must survive opening another.
                services.AddSingleton<IResponseBaselineService, ResponseBaselineService>();

                // Shared across every window (one theme, one log, all stateless services).
                services.AddSingleton<StatusLogViewModel>();
                services.AddSingleton<ThemeManagerViewModel>();

                // Per-window: each window gets its own scope, so its own tab set / left pane /
                // active-editor state. WindowManager creates a scope per window (see WindowManager.cs).
                services.AddScoped<WorkspaceExplorerViewModel>();
                services.AddScoped<EnvironmentManagerViewModel>();
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
