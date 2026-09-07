using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Application.Tests;

/// <summary>
/// A resolver that resolves nothing and changes nothing - the send pipeline's tests are about
/// orchestration, not substitution.
///
/// <para>Pass-through rather than empty-string on purpose: the unresolved-variable guard reads what
/// comes back out of Substitute, so a resolver that blanked its input would make every request look
/// fully resolved and the guard untestable from here.</para>
/// </summary>
internal sealed class PassThroughResolver : IVariableResolver
{
    public static PassThroughResolver Instance { get; } = new();

    public VariableResolution Resolve(string key, Workspace workspace, WorkspaceEnvironment? activeEnvironment) =>
        new(false, "", "");

    public string Substitute(string? input, Workspace workspace, WorkspaceEnvironment? activeEnvironment) => input ?? "";

    public IReadOnlyList<VariableSuggestion> ListAvailable(Workspace workspace, WorkspaceEnvironment? activeEnvironment) => [];
}
