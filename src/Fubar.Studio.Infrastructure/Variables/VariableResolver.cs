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
    private readonly IExternalVariableSource _external;

    public VariableResolver(
        ISecretStoreService secretStore,
        ISessionVariableStore sessionStore,
        IExternalVariableSource? external = null)
    {
        _secretStore = secretStore;
        _sessionStore = sessionStore;
        _external = external ?? EmptyExternalSource.Instance;
    }

    public VariableResolution Resolve(string key, Workspace workspace, WorkspaceEnvironment? activeEnvironment)
    {
        // Session state (tokens, session-scope captures) is scoped per (workspace, environment).
        var scope = SessionScope.For(workspace, activeEnvironment);

        // Highest precedence, and above the keyring on purpose. This is the only source that works on
        // a build agent, where there is no OS keyring at all - without it a Secret variable resolved to
        // nothing and the literal {{token}} went out over the wire. It also has to beat the environment
        // file, or a committed placeholder would shadow the real value the pipeline just supplied.
        if (_external.TryGet(key) is { } externalValue)
        {
            return new VariableResolution(true, externalValue, "external (--var / --env-file / FUBAR_VAR_*)");
        }

        var variable = activeEnvironment?.Variables.FirstOrDefault(v => v.Key == key);
        if (variable is null)
        {
            // Fall back to session-only variables (e.g. an OAuth token) - never persisted, so they don't
            // appear in any environment file.
            if (_sessionStore.TryGet(scope, key, out var sessionValue))
            {
                return new VariableResolution(true, sessionValue, "session");
            }

            // Then the workspace manifest, the LOWEST precedence source: values that are true for the
            // workspace and do not vary by environment.
            //
            // fubar.json documented these as "public workspace variables … committed to Git" from the
            // start and nothing ever read them - built and never wired, the failure CLAUDE.md's closing
            // section warns about. Bottom of the chain on purpose: an environment must always be able to
            // override a workspace default, or picking one would stop meaning anything.
            return ResolveFromManifest(workspace, key);
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
                // A declared-but-empty environment entry falls through to the workspace default rather
                // than shadowing it with "". Otherwise adding a key to one environment would silently
                // blank it for that environment only, which reads as the manifest value being wrong.
                if (string.IsNullOrEmpty(variable.Value) && ResolveFromManifest(workspace, key) is { IsDefined: true } fromManifest)
                {
                    return fromManifest;
                }

                return new VariableResolution(true, variable.Value ?? "", activeEnvironment!.Name);
        }
    }

    /// <summary>
    /// The workspace manifest's own variables - <c>fubar.json</c>'s <c>variables</c> array.
    ///
    /// <para>Committed with the workspace and shared by every environment, so this is for what is true
    /// about the API rather than about one deployment of it: a base path, a version segment, an
    /// account id. Secrets have no business here - the file is committed - so a Secret or Session kind
    /// declared at this level is refused rather than quietly read from disk.</para>
    /// </summary>
    private static VariableResolution ResolveFromManifest(Workspace workspace, string key)
    {
        if (workspace.Manifest.Variables.FirstOrDefault(v => v.Key == key) is not { } variable)
        {
            return new VariableResolution(false, "", "");
        }

        if (variable.Kind != VariableKind.Normal)
        {
            // Not silently ignored: the user marked it Secret expecting protection, and resolving it
            // from a committed file would be the opposite of what they asked for.
            return new VariableResolution(false, "", "workspace (secrets belong in an environment, not fubar.json)");
        }

        return new VariableResolution(true, variable.Value ?? "", "workspace");
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

        // Listed first, matching the precedence: what autocomplete offers should be what would actually
        // resolve. Names only - these values came from the caller's secret manager.
        foreach (var key in _external.Keys)
        {
            if (seen.Add(key))
            {
                result.Add(new VariableSuggestion(key, "external", IsSession: false));
            }
        }

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

        // Last, matching precedence - autocomplete should offer what would actually resolve, in the
        // order it would resolve.
        foreach (var variable in workspace.Manifest.Variables.Where(v => v.Kind == VariableKind.Normal))
        {
            if (seen.Add(variable.Key))
            {
                result.Add(new VariableSuggestion(variable.Key, "workspace", IsSession: false));
            }
        }

        return result;
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex VariableTokenRegex();

    /// <summary>The GUI's case: nothing supplied from outside, so the resolver behaves exactly as it
    /// did. A null object rather than a null check at each use, since there are two.</summary>
    private sealed class EmptyExternalSource : IExternalVariableSource
    {
        public static EmptyExternalSource Instance { get; } = new();

        public IReadOnlyCollection<string> Keys => [];

        public string? TryGet(string key) => null;
    }
}
