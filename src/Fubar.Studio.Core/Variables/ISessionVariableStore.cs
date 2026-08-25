namespace Fubar.Studio.Core.Variables;

/// <summary>
/// An in-memory store for variables that must NOT be persisted to disk - OAuth access tokens, their
/// expiry, refresh tokens, and any other transient runtime values. Keyed by a <b>session scope</b>
/// (per workspace + environment, see <see cref="SessionScope"/>) so tokens/captures never leak across
/// environments. Resolved by <c>IVariableResolver</c> as a fallback under the active environment, so
/// <c>{{token}}</c> works everywhere while never being written to an environment file. Cleared on exit.
/// </summary>
public interface ISessionVariableStore
{
    string? Get(string scope, string key);

    void Set(string scope, string key, string? value);

    bool TryGet(string scope, string key, out string value);

    /// <summary>A point-in-time copy of every session variable currently set for the scope - so the
    /// variable autocomplete/lists can show them alongside the environment's variables.</summary>
    IReadOnlyDictionary<string, string> Snapshot(string scope);
}
