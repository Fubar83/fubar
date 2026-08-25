using System.Collections.Concurrent;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Infrastructure.Variables;

/// <summary>In-memory <see cref="ISessionVariableStore"/>: a per-scope (workspace + environment) map of
/// transient variables (OAuth tokens/expiry, etc.) that live only for the app session and are never
/// written to disk.</summary>
public sealed class SessionVariableStore : ISessionVariableStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _byScope = new();

    public string? Get(string scope, string key) =>
        _byScope.TryGetValue(scope, out var map) && map.TryGetValue(key, out var value) ? value : null;

    public bool TryGet(string scope, string key, out string value)
    {
        if (_byScope.TryGetValue(scope, out var map) && map.TryGetValue(key, out var stored))
        {
            value = stored;
            return true;
        }

        value = "";
        return false;
    }

    public void Set(string scope, string key, string? value)
    {
        var map = _byScope.GetOrAdd(scope, _ => new ConcurrentDictionary<string, string>());
        if (value is null)
        {
            map.TryRemove(key, out _);
        }
        else
        {
            map[key] = value;
        }
    }

    public IReadOnlyDictionary<string, string> Snapshot(string scope) =>
        _byScope.TryGetValue(scope, out var map)
            ? new Dictionary<string, string>(map)
            : new Dictionary<string, string>();
}
