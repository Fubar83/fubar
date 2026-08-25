namespace Fubar.Studio.Core.Secrets;

/// <summary>
/// Abstracts the host OS keyring for <see cref="Fubar.Studio.Core.Models.AppVariable"/> entries with
/// <c>IsSecret = true</c> - their value is never written to disk (see <see cref="Fubar.Studio.Core.Models.AppVariable"/>'s
/// doc comment), only to this store, keyed <c>fubar:[WorkspaceId]:[VariableKey]</c>.
/// </summary>
public interface ISecretStoreService
{
    /// <summary>Returns the stored secret, or null if none is set.</summary>
    string? TryGetSecret(string workspaceId, string key);

    void SetSecret(string workspaceId, string key, string value);

    void DeleteSecret(string workspaceId, string key);
}
