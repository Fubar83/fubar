using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Studio.UI.Services;

/// <summary>Creates the main-canvas editor view models, supplying their many services from DI and only
/// their runtime data (the request/environment/profile being opened) by hand. This keeps
/// <see cref="MainViewModel"/> from having to inject-and-thread every editor dependency itself.</summary>
public interface IEditorViewModelFactory
{
    RequestEditorViewModel CreateRequestEditor(RequestModel request, string filePath, IProtocolProvider provider, Workspace workspace);

    EnvironmentEditorViewModel CreateEnvironmentEditor(WorkspaceEnvironment environment, Workspace workspace);

    AuthProfileEditorViewModel CreateAuthProfileEditor(AuthProfile profile, Workspace workspace);

    CaseEditorViewModel CreateCaseEditor(
        EndpointCase endpointCase, RequestModel endpoint, string filePath, Workspace workspace);

    BatchEditorViewModel CreateBatchEditor(
        Batch batch, string filePath, Workspace workspace, IReadOnlyList<string> environmentNames);

    FolderEditorViewModel CreateFolderEditor(
        FolderConfig config, string folderPath, Workspace workspace, IReadOnlyList<AuthProfile> authProfiles);
}

/// <summary>
/// <see cref="ActivatorUtilities"/>-based implementation: DI-registered services are resolved from the
/// (per-window) scope; the runtime arguments are passed positionally and matched by type. Registered
/// scoped so it captures the window's <see cref="System.IServiceProvider"/>.
/// </summary>
public sealed class EditorViewModelFactory : IEditorViewModelFactory
{
    private readonly IServiceProvider _provider;

    public EditorViewModelFactory(IServiceProvider provider) => _provider = provider;

    public RequestEditorViewModel CreateRequestEditor(RequestModel request, string filePath, IProtocolProvider provider, Workspace workspace) =>
        ActivatorUtilities.CreateInstance<RequestEditorViewModel>(_provider, request, filePath, provider, workspace);

    public EnvironmentEditorViewModel CreateEnvironmentEditor(WorkspaceEnvironment environment, Workspace workspace) =>
        ActivatorUtilities.CreateInstance<EnvironmentEditorViewModel>(_provider, environment, workspace);

    public AuthProfileEditorViewModel CreateAuthProfileEditor(AuthProfile profile, Workspace workspace) =>
        ActivatorUtilities.CreateInstance<AuthProfileEditorViewModel>(_provider, profile, workspace);

    public CaseEditorViewModel CreateCaseEditor(
        EndpointCase endpointCase, RequestModel endpoint, string filePath, Workspace workspace) =>
        ActivatorUtilities.CreateInstance<CaseEditorViewModel>(
            _provider, endpointCase, endpoint, filePath, workspace);

    public BatchEditorViewModel CreateBatchEditor(
        Batch batch, string filePath, Workspace workspace, IReadOnlyList<string> environmentNames) =>
        ActivatorUtilities.CreateInstance<BatchEditorViewModel>(
            _provider, batch, filePath, workspace, environmentNames);

    public FolderEditorViewModel CreateFolderEditor(
        FolderConfig config, string folderPath, Workspace workspace, IReadOnlyList<AuthProfile> authProfiles) =>
        ActivatorUtilities.CreateInstance<FolderEditorViewModel>(
            _provider, config, folderPath, workspace, authProfiles);
}
