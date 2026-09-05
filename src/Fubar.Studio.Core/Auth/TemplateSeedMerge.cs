using System.Text.RegularExpressions;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// What survives when a template is applied over work already done.
///
/// <para>Applying a template used to replace everything: the URL, every header, every body field,
/// every capture rule. So the natural order of doing this - press Discover, get your endpoints, fill
/// in your client id, then realise you want a different grant - threw away all of it, and pressing
/// "Set up" a second time after correcting one field threw away the correction. A template that
/// punishes you for touching anything before it is not a starting point, it is a trap.</para>
///
/// <para>The rule: <b>a template seeds STRUCTURE; it never overwrites an answer only the user
/// has.</b> The template knows which fields exist, which grant is being used, where the endpoints are
/// for a named provider, and which response fields to capture. It does not know your client id, your
/// tenant, the scope list your API needs, the header your gateway wants, or that this provider spells
/// the token <c>data.access_token</c>. Where the template ships a <c>{{placeholder}}</c>, it is
/// SAYING it does not know - so anything already there wins.</para>
///
/// <para>Pure, and in Core, because it is the whole decision. A view model that merged inline would
/// put the one rule worth testing behind a UI.</para>
/// </summary>
public static class TemplateSeedMerge
{
    /// <summary>
    /// A value that is nothing but one <c>{{token}}</c> - the template's way of saying "your answer
    /// goes here".
    ///
    /// <para>Whole-value only. <c>{{BaseUrl}}/oauth/token</c> is a real answer someone composed and is
    /// not a placeholder; treating it as one would delete exactly the URLs people work hardest to get
    /// right.</para>
    /// </summary>
    public static bool IsPlaceholder(string? value) =>
        !string.IsNullOrWhiteSpace(value) && PlaceholderPattern.IsMatch(value.Trim());

    private static readonly Regex PlaceholderPattern =
        new(@"^\{\{[^{}]+\}\}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The URL to end up with.
    ///
    /// <para>A template that offers <c>{{token_url}}</c> or nothing at all must not overwrite an
    /// endpoint that Discover found or the user typed. A template naming a real endpoint - a provider
    /// preset - does overwrite, because that is the entire point of choosing one.</para>
    /// </summary>
    public static string Url(string? current, string? fromTemplate)
    {
        var template = fromTemplate ?? "";

        if (!string.IsNullOrWhiteSpace(template) && !IsPlaceholder(template))
        {
            return template;
        }

        return string.IsNullOrWhiteSpace(current) ? template : current;
    }

    /// <summary>
    /// A value the user may have supplied for a field the template also defines.
    ///
    /// <para>Kept whenever the template's own value is a placeholder and something is already there -
    /// covering both a literal typed into the box and a variable pointed somewhere else
    /// (<c>{{google_client_id}}</c> in place of <c>{{client_id}}</c>), which is a deliberate choice
    /// and not a value to silently reset.</para>
    /// </summary>
    public static string Value(string? current, string? fromTemplate) =>
        IsPlaceholder(fromTemplate) && !string.IsNullOrWhiteSpace(current) ? current : fromTemplate ?? "";

    /// <summary>
    /// Key/value rows - body fields, headers, authorize parameters - after applying a template.
    ///
    /// <para>Template rows first and in the template's order, each taking the user's value where the
    /// rule above says so; then every existing row the template says nothing about, appended
    /// unchanged. That second half is what keeps a header someone added for their gateway, or an
    /// <c>audience</c> parameter their provider needs.</para>
    ///
    /// <para>Matched case-insensitively: <c>Accept</c> and <c>accept</c> are one header, and treating
    /// them as two would quietly send both.</para>
    /// </summary>
    public static IReadOnlyList<KeyValueItem> Fields(
        IReadOnlyList<KeyValueItem>? current,
        IReadOnlyList<KeyValueItem>? fromTemplate)
    {
        var existing = current ?? [];
        var template = fromTemplate ?? [];

        var merged = template
            .Select(field =>
            {
                var match = existing.FirstOrDefault(e => SameKey(e.Key, field.Key));

                return new KeyValueItem
                {
                    Key = field.Key,
                    Value = Value(match?.Value, field.Value),
                    Description = match?.Description ?? field.Description,

                    // A row the user switched OFF stays off. Unticking is how someone tests whether a
                    // parameter is the problem, and an apply that silently re-enabled it would undo
                    // the experiment they are in the middle of.
                    Enabled = match?.Enabled ?? field.Enabled,
                };
            })
            .ToList();

        merged.AddRange(existing.Where(e => !template.Any(t => SameKey(t.Key, e.Key))));

        return merged;
    }

    /// <summary>
    /// Capture rules after applying a template.
    ///
    /// <para>Keyed on the VARIABLE each rule writes, because that is a rule's identity - two rules
    /// writing one variable is a conflict, not a pair. An existing rule for a variable the template
    /// also captures is kept WHOLE, expression and all: a corrected JSONPath
    /// (<c>$.data.access_token</c> for a provider that nests it) is the single most valuable thing on
    /// this screen and the hardest to work out again. Rules for variables the template knows nothing
    /// about are appended.</para>
    /// </summary>
    public static IReadOnlyList<CaptureRule> Captures(
        IReadOnlyList<CaptureRule>? current,
        IReadOnlyList<CaptureRule>? fromTemplate)
    {
        var existing = current ?? [];
        var template = fromTemplate ?? [];

        var merged = template
            .Select(rule => existing.FirstOrDefault(e => SameKey(e.VariableName, rule.VariableName)) ?? rule)
            .ToList();

        merged.AddRange(existing.Where(e =>
            !string.IsNullOrWhiteSpace(e.VariableName)
            && !template.Any(t => SameKey(t.VariableName, e.VariableName))));

        return merged;
    }

    /// <summary>
    /// A variable NAME the user may have chosen - the access-token or expiry variable.
    ///
    /// <para>Kept when set, because it is what the <c>Authorization: Bearer</c> header reads and
    /// renaming it is a deliberate act. A blank one takes the template's, which is what a fresh
    /// editor has.</para>
    /// </summary>
    public static string Name(string? current, string? fromTemplate) =>
        string.IsNullOrWhiteSpace(current) ? fromTemplate ?? "" : current;

    private static bool SameKey(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
