using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// One <c>{{variable}}</c> a token request depends on, and whether it currently resolves.
/// </summary>
/// <param name="Name">The variable name, without braces.</param>
/// <param name="IsResolved">True when the active environment or the workspace defines it.</param>
public sealed record TokenRequestVariable(string Name, bool IsResolved);

/// <summary>
/// Works out which variables a token request READS, and which of them are actually defined.
///
/// The point is to answer the question before the request is sent rather than after it fails. The
/// per-field tooltip already tints one box at a time, which tells you about the box you are hovering;
/// what setting OAuth up needs is the list - "this needs authHost, clientId and clientSecret; two of
/// them exist" - because the missing one is usually in a field you are not looking at.
///
/// Deliberately reads only the request INPUTS: the URL, header values, and whatever the body carries.
/// Captures are excluded because they WRITE variables rather than read them, and listing them as
/// undefined dependencies would be exactly backwards - they are undefined until the request succeeds,
/// which is the normal state of affairs and not a problem to report.
/// </summary>
public static class TokenRequestVariables
{
    /// <summary>
    /// Every variable the request reads, in first-appearance order, each marked resolved or not.
    ///
    /// <paramref name="substitute"/> is the resolver's own substitution, passed in so this stays pure
    /// and testable: a name counts as resolved when substituting it changes it into something else,
    /// which is precisely what the resolver means by resolving it. Asking the resolver directly for a
    /// value would need a second code path that could disagree with the one the request will use.
    /// </summary>
    public static IReadOnlyList<TokenRequestVariable> Of(AuthTokenRequest? request, Func<string, string> substitute)
    {
        ArgumentNullException.ThrowIfNull(substitute);

        if (request is null)
        {
            return [];
        }

        var texts = new List<string?> { request.Url };

        foreach (var header in request.Headers ?? [])
        {
            // The name as well as the value: `{{tenant}}-Api-Key: …` is unusual but legal, and a
            // request that cannot even name its header is just as broken as one that cannot fill it.
            texts.Add(header.Key);
            texts.Add(header.Value);
        }

        if (request.Body is { } body)
        {
            texts.Add(body.Raw);

            foreach (var field in (body.FormData ?? []).Concat(body.UrlEncoded ?? []))
            {
                texts.Add(field.Key);
                texts.Add(field.Value);
            }
        }

        var names = UnresolvedVariables.In([.. texts]);
        var found = new List<TokenRequestVariable>(names.Count);

        foreach (var name in names)
        {
            var token = $"{{{{{name}}}}}";

            found.Add(new TokenRequestVariable(name, !string.Equals(substitute(token), token, StringComparison.Ordinal)));
        }

        return found;
    }

    /// <summary>
    /// A one-line summary for the editor, or null when the request reads no variables at all - in
    /// which case there is nothing useful to say and a permanent "0 variables" line would be noise.
    /// </summary>
    public static string? Describe(IReadOnlyList<TokenRequestVariable> variables)
    {
        if (variables is null || variables.Count == 0)
        {
            return null;
        }

        var missing = variables.Where(v => !v.IsResolved).Select(v => $"{{{{{v.Name}}}}}").ToList();

        return missing.Count == 0
            ? $"All {variables.Count} variable(s) this request uses are defined."
            : $"Not defined: {string.Join(", ", missing)}";
    }
}
