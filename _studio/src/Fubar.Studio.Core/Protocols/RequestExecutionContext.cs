using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Protocols;

/// <summary>
/// What an <see cref="IRequestExecutor"/> needs to resolve <c>{{variable}}</c> tokens while
/// sending a request: which workspace it belongs to (for secret lookups, keyed by
/// <see cref="Models.Workspace.WorkspaceId"/>) and which environment is currently active.
/// <para><see cref="SensitiveHeaderNames"/> lists the credential header names the auth prestep injected;
/// an executor that follows redirects must drop these (plus <c>Authorization</c>) on a cross-origin hop so
/// a captured token / API key is never replayed to a redirect target on another host.</para>
/// </summary>
public sealed record RequestExecutionContext(
    Workspace Workspace,
    WorkspaceEnvironment? ActiveEnvironment,
    IReadOnlyList<string>? SensitiveHeaderNames = null);
