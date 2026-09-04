using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Variables;

/// <summary>
/// The single place a variable's value is put where its <see cref="VariableKind"/> says it belongs:
/// <see cref="VariableKind.Secret"/> to the OS keyring, <see cref="VariableKind.Session"/> to the
/// in-memory store, <see cref="VariableKind.Normal"/> to the environment file.
///
/// <para>This exists because the rule was previously enforced in exactly one caller. The environment
/// editor's Save applied it correctly; the CAPTURE path assigned <c>AppVariable.Value</c> directly, so
/// an Environment-scoped capture wrote a token into <c>environments/*.json</c> - the file the product
/// tells you to commit - and, when the target variable was already Secret, overwrote the on-disk
/// <c>Value</c> that <see cref="AppVariable"/> documents as always null. One rule with two callers is
/// how the two came to disagree; one implementation with two callers is the fix.</para>
/// </summary>
public interface IVariableWriter
{
    /// <summary>
    /// Writes <paramref name="value"/> for <paramref name="key"/> into the correct store for its kind,
    /// adding the variable to <paramref name="environment"/> if it is not already declared.
    ///
    /// <para>An existing variable KEEPS its kind: a capture naming a Secret variable writes to the
    /// keyring rather than silently demoting it to a plaintext file entry. Only
    /// <see cref="VariableKind.Normal"/> touches <see cref="AppVariable.Value"/>, so
    /// <paramref name="environment"/> needs persisting only when
    /// <see cref="VariableWriteResult.EnvironmentChanged"/> says so.</para>
    /// </summary>
    /// <param name="requestedKind">The kind to use when the variable does not exist yet. Ignored for one
    /// that does - changing a variable's kind is an explicit act, and belongs to the editor.</param>
    VariableWriteResult Write(
        Workspace workspace,
        WorkspaceEnvironment environment,
        string key,
        string? value,
        VariableKind requestedKind = VariableKind.Normal);
}

/// <summary>What a <see cref="IVariableWriter.Write"/> did, so the caller knows whether the environment
/// file needs saving and can report where the value actually went.</summary>
/// <param name="Kind">Where it went - the existing variable's kind, or the requested one for a new variable.</param>
/// <param name="EnvironmentChanged">True when the environment object was mutated and must be persisted.</param>
public readonly record struct VariableWriteResult(VariableKind Kind, bool EnvironmentChanged)
{
    /// <summary>Human-readable destination, for the status log and capture results.</summary>
    public string Destination => Kind switch
    {
        VariableKind.Secret => "keyring",
        VariableKind.Session => "session",
        _ => "environment",
    };
}
