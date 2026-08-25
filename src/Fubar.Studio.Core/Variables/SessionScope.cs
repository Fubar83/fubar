using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Variables;

/// <summary>
/// Computes the key under which session variables (OAuth tokens, session-scope captures,
/// <see cref="VariableKind.Session"/> values) are stored, so that transient auth/session state is scoped
/// <b>per (workspace, environment)</b> - a DEV token is never reused against PROD. Secrets are deliberately
/// NOT scoped this way (they stay keyed by the raw <see cref="Workspace.WorkspaceId"/> in the OS keyring).
/// </summary>
public static class SessionScope
{
    public static string For(Workspace workspace, WorkspaceEnvironment? environment) =>
        For(workspace, environment?.Id);

    public static string For(Workspace workspace, string? environmentId) =>
        $"{workspace.WorkspaceId}::{environmentId ?? "default"}";
}
