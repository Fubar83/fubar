namespace Fubar.Studio.Core.Workspaces;

/// <summary>
/// The full workspace storage surface for the Git-native layout: a root directory containing
/// <c>fubar.json</c>, an optional <c>auth-profiles.json</c>, an <c>environments/</c> directory, and a
/// <c>collections/</c> tree of <c>request.json</c> files.
///
/// This is an aggregate of the focused role interfaces (<see cref="IWorkspaceStore"/>,
/// <see cref="IRequestStore"/>, <see cref="IEnvironmentStore"/>, <see cref="IAuthProfileStore"/>,
/// <see cref="IFolderConfigStore"/>, <see cref="IInheritanceResolver"/>). Depend on the narrowest role
/// that covers your needs (ISP); only the broad workspace-mutation orchestrators (e.g. the importers)
/// need the whole surface.
/// </summary>
public interface IWorkspaceService :
    IWorkspaceStore,
    IRequestStore,
    IEnvironmentStore,
    IAuthProfileStore,
    IFolderConfigStore,
    IInheritanceResolver;
