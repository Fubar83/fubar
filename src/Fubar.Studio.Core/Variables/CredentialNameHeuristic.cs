using System.Text.RegularExpressions;

namespace Fubar.Studio.Core.Variables;

/// <summary>
/// Guesses whether a variable NAME describes a credential, so a capture about to persist one to a
/// tracked file can say so.
///
/// <para>A guess, and treated as one: it warns, it never blocks. A deliberately persisted API key for a
/// public sandbox is legitimate, and a tool that refused it would be wrong about the case the user
/// understands better than it does. The asymmetry is the point - a false positive costs one dismissed
/// warning, a false negative costs a committed token.</para>
/// </summary>
public static partial class CredentialNameHeuristic
{
    /// <summary>True when the name looks like it holds a credential.</summary>
    public static bool LooksLikeCredential(string? variableName) =>
        !string.IsNullOrWhiteSpace(variableName) && CredentialNameRegex().IsMatch(variableName);

    /// <summary>
    /// The warning to show, or null when there is nothing to warn about. Names the variable and says
    /// what to do instead, because "this looks like a secret" on its own leaves the reader to work out
    /// which of the three scopes they wanted.
    /// </summary>
    public static string? DescribeEnvironmentCaptureRisk(string? variableName) =>
        LooksLikeCredential(variableName)
            ? $"\"{variableName}\" looks like a credential and is captured to the environment, which is "
              + "written to environments/*.json and committed. Use Session scope to keep it in memory only, "
              + "or mark the variable Secret to store it in the OS keyring."
            : null;

    // Deliberately broad: these are substrings, so client_secret, apiKey and X-Auth-Token all match.
    [GeneratedRegex(
        "token|secret|password|passwd|api[-_]?key|apikey|auth|bearer|credential|private[-_]?key",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialNameRegex();
}
