using Fubar.Studio.Core.Secrets;
using KeySharp;

namespace Fubar.Studio.Infrastructure.Secrets;

/// <summary>
/// <see cref="ISecretStoreService"/> backed by the host OS keyring via KeySharp (Windows
/// Credential Manager / macOS Keychain / Linux Secret Service). Addressed as
/// <c>(package: "fubar", service: workspaceId, username: key)</c>, matching the
/// <c>fubar:[WorkspaceId]:[VariableKey]</c> scheme documented on <see cref="Fubar.Studio.Core.Models.AppVariable"/>.
/// </summary>
public sealed class KeySharpSecretStoreService : ISecretStoreService
{
    private const string Package = "fubar";

    public string? TryGetSecret(string workspaceId, string key)
    {
        try
        {
            return Keyring.GetPassword(Package, workspaceId, key);
        }
        catch (KeyringException)
        {
            // No secret set for this key yet - not an error condition for a resolver lookup.
            return null;
        }
    }

    public void SetSecret(string workspaceId, string key, string value) =>
        Keyring.SetPassword(Package, workspaceId, key, value);

    public void DeleteSecret(string workspaceId, string key)
    {
        try
        {
            Keyring.DeletePassword(Package, workspaceId, key);
        }
        catch (KeyringException)
        {
            // Already absent - deleting a non-existent secret is a no-op, not a failure.
        }
    }
}
