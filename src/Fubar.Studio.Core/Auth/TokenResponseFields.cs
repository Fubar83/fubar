using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// One value in a token response, with the JSONPath that reaches it.
/// </summary>
/// <param name="Path">A JSONPath a <see cref="Models.CaptureRule"/> can use verbatim, e.g. <c>$.access_token</c>.</param>
/// <param name="Preview">The value, shortened and with anything token-shaped masked.</param>
/// <param name="IsLikelyToken">
/// True for the fields a token response usually carries the credential in. Used to offer the obvious
/// captures first rather than making the user pick them out of a provider's twenty-field response.
/// </param>
public sealed record TokenResponseField(string Path, string Preview, bool IsLikelyToken);

/// <summary>
/// Reads a token response into the list of paths you could capture from it.
///
/// This exists because of the worst step in setting OAuth up: a capture rule needs a JSONPath like
/// <c>$.access_token</c>, and until now the response it addresses was never shown. The one step
/// requiring exact knowledge of the payload was the one step with no way to see it - so people guessed
/// at field names, and a wrong guess fails identically to a wrong endpoint, wrong secret, or wrong
/// grant.
///
/// Only the shapes a token endpoint actually returns are walked: the top level, and one level into
/// nested objects. A general JSON walker would be more code and would bury <c>$.access_token</c> in a
/// list of everything, which is the opposite of the point.
/// </summary>
public static class TokenResponseFields
{
    /// <summary>Field names that usually hold the credential, in the order worth offering them.</summary>
    private static readonly string[] Likely =
        ["access_token", "id_token", "token", "refresh_token", "expires_in", "token_type", "scope"];

    /// <summary>
    /// The capturable fields in <paramref name="body"/>, or empty when it is not a JSON object -
    /// some endpoints return form-encoded or XML, and inventing paths into those would be worse than
    /// showing none.
    /// </summary>
    public static IReadOnlyList<TokenResponseField> From(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            // A token endpoint that returned HTML - a login page, a proxy error - is a completely
            // ordinary failure, and not one to throw over.
            return [];
        }

        if (root is not JsonObject obj)
        {
            return [];
        }

        var fields = new List<TokenResponseField>();

        foreach (var (name, value) in obj)
        {
            if (value is JsonObject nested)
            {
                foreach (var (childName, childValue) in nested)
                {
                    if (childValue is not JsonObject and not JsonArray)
                    {
                        fields.Add(Field($"$.{name}.{childName}", childName, childValue));
                    }
                }

                continue;
            }

            if (value is not JsonArray)
            {
                fields.Add(Field($"$.{name}", name, value));
            }
        }

        // Likely fields first, in the order above, then everything else as it came. A provider that
        // returns twenty fields should still put access_token at the top.
        return
        [
            .. fields.Where(f => f.IsLikelyToken).OrderBy(f => Array.IndexOf(Likely, LeafOf(f.Path))),
            .. fields.Where(f => !f.IsLikelyToken),
        ];
    }

    /// <summary>
    /// The capture rules worth offering for this response without being asked - the access token, and
    /// a refresh token when one came back.
    /// </summary>
    public static IReadOnlyList<TokenResponseField> Suggested(IReadOnlyList<TokenResponseField> fields) =>
        [.. (fields ?? []).Where(f => LeafOf(f.Path) is "access_token" or "id_token" or "refresh_token")];

    private static TokenResponseField Field(string path, string name, JsonNode? value)
    {
        var text = value?.ToString() ?? "";

        // Never echo a token in full. The point of showing the response is to find the FIELD, and the
        // shape of the value is enough for that - a preview pane that spills a live credential into a
        // screenshot or a screen share is a bad trade for information nobody needed.
        var masked = Likely.Contains(name, StringComparer.OrdinalIgnoreCase)
            && name is not ("expires_in" or "token_type" or "scope")
            && text.Length > 12
                ? text[..6] + "…" + $" ({text.Length} chars)"
                : Shorten(text);

        return new TokenResponseField(path, masked, Likely.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    private static string Shorten(string text) =>
        text.Length <= 60 ? text : text[..60] + "…";

    private static string LeafOf(string path) =>
        path[(path.LastIndexOf('.') + 1)..];
}
