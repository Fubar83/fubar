using System.Text.RegularExpressions;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Infrastructure.Variables;

/// <summary>
/// Resolves <c>{{key}}</c> tokens strictly against a workspace's active <see cref="WorkspaceEnvironment"/>
/// (RequestEditorPane.md §1.3), pulling secret-flagged variables from <see cref="ISecretStoreService"/>
/// instead of the (deliberately empty-on-disk) environment file value.
/// </summary>
public sealed partial class VariableResolver : IVariableResolver
{
    private readonly ISecretStoreService _secretStore;
    private readonly ISessionVariableStore _sessionStore;

    public VariableResolver(ISecretStoreService secretStore, ISessionVariableStore sessionStore)
    {
        _secretStore = secretStore;
        _sessionStore = sessionStore;
    }

    public VariableResolution Resolve(string key, Workspace workspace, WorkspaceEnvironment? activeEnvironment)
    {
        // Session state (tokens, session-scope captures) is scoped per (workspace, environment).
        var scope = SessionScope.For(workspace, activeEnvironment);

        var variable = activeEnvironment?.Variables.FirstOrDefault(v => v.Key == key);
        if (variable is null)
        {
            // Fall back to session-only variables (e.g. an OAuth token) - never persisted, so they don't
            // appear in any environment file.
            return _sessionStore.TryGet(scope, key, out var sessionValue)
                ? new VariableResolution(true, sessionValue, "session")
                : new VariableResolution(false, "", "");
        }

        switch (variable.Kind)
        {
            case VariableKind.Secret:
                var secret = _secretStore.TryGetSecret(workspace.WorkspaceId, key);
                return secret is null
                    ? new VariableResolution(false, "", $"{activeEnvironment!.Name} (secret not set)")
                    : new VariableResolution(true, secret, $"{activeEnvironment!.Name} (secret)");

            case VariableKind.Session:
                // Session values live only in the in-memory store (never on disk), like a captured token.
                return _sessionStore.TryGet(scope, key, out var sessionValue)
                    ? new VariableResolution(true, sessionValue, $"{activeEnvironment!.Name} (session)")
                    : new VariableResolution(false, "", $"{activeEnvironment!.Name} (session not set)");

            default:
                return new VariableResolution(true, variable.Value ?? "", activeEnvironment!.Name);
        }
    }

    public string Substitute(string? input, Workspace workspace, WorkspaceEnvironment? activeEnvironment)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("{{", StringComparison.Ordinal))
        {
            return input ?? "";
        }

        return VariableTokenRegex().Replace(input, match =>
        {
            var resolution = Resolve(match.Groups[1].Value, workspace, activeEnvironment);
            return resolution.IsDefined ? resolution.Value : match.Value;
        });
    }

    public IReadOnlyList<VariableSuggestion> ListAvailable(Workspace workspace, WorkspaceEnvironment? activeEnvironment)
    {
        var result = new List<VariableSuggestion>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (activeEnvironment is not null)
        {
            foreach (var variable in activeEnvironment.Variables)
            {
                if (seen.Add(variable.Key))
                {
                    result.Add(new VariableSuggestion(variable.Key, activeEnvironment.Name, IsSession: false));
                }
            }
        }

        foreach (var key in _sessionStore.Snapshot(SessionScope.For(workspace, activeEnvironment)).Keys)
        {
            if (seen.Add(key))
            {
                result.Add(new VariableSuggestion(key, "session", IsSession: true));
            }
        }

        return result;
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex VariableTokenRegex();
}
