using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Variables;

/// <summary>
/// Resolves <c>{{key}}</c> tokens strictly against a workspace's active <see cref="WorkspaceEnvironment"/>
/// (RequestEditorPane.md §1.3 - "Environment-Only Variables"), transparently pulling secret-flagged
/// variables from <c>ISecretStoreService</c> instead of the environment file. Used both by the
/// Universal Variable Tooltip system (URL/header/param/body highlighting) and by
/// <c>IRequestExecutor</c> to substitute the real outgoing values.
/// </summary>
public interface IVariableResolver
{
    /// <summary>Resolves a single variable name (without the <c>{{ }}</c> delimiters).</summary>
    VariableResolution Resolve(string key, Workspace workspace, WorkspaceEnvironment? activeEnvironment);

    /// <summary>Substitutes every <c>{{key}}</c> token in <paramref name="input"/>; unresolved tokens are left as-is.</summary>
    string Substitute(string? input, Workspace workspace, WorkspaceEnvironment? activeEnvironment);

    /// <summary>Every variable name available for a <c>{{key}}</c>: the active environment's variables plus
    /// the session-only variables (OAuth tokens, captured values). Environment variables win on a name
    /// clash. Used by the variable autocomplete/lists so session variables are visible too.</summary>
    IReadOnlyList<VariableSuggestion> ListAvailable(Workspace workspace, WorkspaceEnvironment? activeEnvironment);
}

/// <summary>One selectable variable name and where it comes from - an environment (by name) or the
/// in-memory session store. <see cref="IsSession"/> lets the UI mark session variables distinctly.</summary>
public sealed record VariableSuggestion(string Key, string Source, bool IsSession);
