using System.Text.RegularExpressions;

namespace Fubar.Studio.Core.Variables;

/// <summary>
/// Finds <c>{{name}}</c> tokens that survived substitution.
///
/// <see cref="IVariableResolver.Substitute"/> leaves what it cannot resolve exactly as it found it,
/// which is the right behaviour - guessing, or blanking, would both be worse - but it means an
/// unresolved variable travels onward as literal text and fails somewhere far away from the cause. A
/// token URL of <c>{{authHost}}/oauth/token</c> reaches the HTTP client as that string and comes back
/// as an invalid-URI error, which says nothing whatsoever about a variable.
///
/// This turns that into the sentence the user actually needs: WHICH variable, and that it is the
/// reason. Pure and in Core so both the auth path and anything else that resolves text can use it.
/// </summary>
public static partial class UnresolvedVariables
{
    /// <summary>
    /// The distinct variable names still unresolved in <paramref name="text"/>, in the order they
    /// first appear. Empty when everything resolved, which is the overwhelmingly common case and
    /// costs one <c>Contains</c>.
    /// </summary>
    public static IReadOnlyList<string> In(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return [];
        }

        var names = new List<string>();

        foreach (Match match in TokenRegex().Matches(text))
        {
            var name = match.Groups[1].Value;

            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// The same, across several pieces of one request - so a token request with an unresolved host
    /// AND an unresolved client id reports both rather than one, one fix at a time.
    /// </summary>
    public static IReadOnlyList<string> In(params string?[] texts)
    {
        var names = new List<string>();

        foreach (var text in texts ?? [])
        {
            foreach (var name in In(text))
            {
                if (!names.Contains(name, StringComparer.Ordinal))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// A sentence naming them, or null when there is nothing to say.
    ///
    /// Written once here rather than at each call site so the wording cannot drift, and phrased as
    /// the two things that are actually wrong: the variables are not defined, and the place to define
    /// them is an environment or the workspace.
    /// </summary>
    public static string? Describe(IReadOnlyList<string> names)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        var list = string.Join(", ", names.Select(name => $"{{{{{name}}}}}"));

        return names.Count == 1
            ? $"{list} is not defined in the active environment or the workspace."
            : $"{list} are not defined in the active environment or the workspace.";
    }

    // Deliberately the same shape VariableResolver substitutes, so what it leaves behind is exactly
    // what this finds. Two patterns that could disagree would report a variable as unresolved that
    // was never a variable, or miss one that was.
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenRegex();
}
