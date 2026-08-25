using System.Text.Json.Serialization;

namespace Fubar.Studio.Core.Models;

/// <summary>How a variable's value is stored.</summary>
public enum VariableKind
{
    /// <summary>Plain value, persisted in the environment JSON.</summary>
    Normal,

    /// <summary>Value lives only in the host OS keyring; never written to disk.</summary>
    Secret,

    /// <summary>Value lives only in the in-memory session store; never written to disk and cleared on exit.</summary>
    Session,
}

/// <summary>
/// A single entry in an environment's <c>variables</c> array. The <see cref="Value"/> is persisted only
/// for <see cref="VariableKind.Normal"/>. For <see cref="VariableKind.Secret"/> the real value lives in
/// the host OS keyring (keyed <c>fubar:[WorkspaceId]:[Key]</c>); for <see cref="VariableKind.Session"/> it
/// lives only in the in-memory session store. Both are null on disk and resolved at request-execution time
/// via <c>IVariableResolver</c> / <c>ISecretStoreService</c> / <c>ISessionVariableStore</c>.
/// </summary>
public sealed class AppVariable
{
    public required string Key { get; set; }

    public string? Value { get; set; }

    /// <summary>How the value is stored (Normal/Secret/Session). Persisted; defaults to Normal.</summary>
    public VariableKind Kind { get; set; } = VariableKind.Normal;

    public string? Description { get; set; }

    /// <summary>Back-compat: older files persisted a boolean <c>isSecret</c> instead of <see cref="Kind"/>.
    /// Reading <c>true</c> maps to <see cref="VariableKind.Secret"/>; never re-serialized (the getter
    /// returns null, which <c>JsonIgnoreCondition.WhenWritingNull</c> omits) so new files carry
    /// <see cref="Kind"/> only.</summary>
    [JsonPropertyName("isSecret")]
    public bool? IsSecret
    {
        get => null;
        set
        {
            if (value == true && Kind == VariableKind.Normal)
            {
                Kind = VariableKind.Secret;
            }
        }
    }
}
