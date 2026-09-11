using System.Text;
using System.Text.RegularExpressions;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Variables;

/// <summary>
/// What is needed to say anything about the <c>{{key}}</c> tokens in one text box: how to resolve
/// them, against which workspace and environment, and whether secrets are currently revealed.
/// </summary>
public sealed record VariableTooltipContext(
    IVariableResolver Resolver,
    Workspace Workspace,
    WorkspaceEnvironment? ActiveEnvironment,
    bool SecretsRevealed);

/// <summary>One <c>{{name}}</c> occurrence: where it sits in the text, and what it is called.</summary>
/// <param name="Start">Index of the opening brace.</param>
/// <param name="Length">Length including both pairs of braces.</param>
public readonly record struct VariableToken(int Start, int Length, string Key)
{
    public int End => Start + Length;

    /// <summary>Whether a caret index falls inside this token. The end is exclusive, so the position
    /// just after <c>}}</c> belongs to the text that follows rather than to the variable.</summary>
    public bool Covers(int index) => index >= Start && index < End;
}

/// <summary>
/// What a hover over a field containing <c>{{variables}}</c> should say.
/// </summary>
/// <remarks>
/// <para>In Core and pure, so the rule can be tested without a pointer. The UI's job is the part only
/// it can do - hit-testing the pointer to a character index - and this decides what that index
/// means.</para>
/// <para>It answers the POINTER, not the box. Hovering a token is a question about that variable;
/// hovering the rest of a single-line field is the other question people actually ask of a URL
/// ("what will this send?"), which listing the tokens never answered.</para>
/// </remarks>
public static partial class VariableHover
{
    /// <summary>What a secret's value is shown as until secrets are revealed.</summary>
    public const string Masked = "••••••";

    /// <summary>Every <c>{{name}}</c> in the text, in the order they appear.</summary>
    public static IReadOnlyList<VariableToken> Tokens(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : [.. TokenRegex().Matches(text).Select(m => new VariableToken(m.Index, m.Length, m.Groups[1].Value))];

    /// <summary>
    /// The tooltip for a hover at <paramref name="index"/>, or null when there is nothing to say.
    /// </summary>
    /// <param name="index">
    /// Which character the pointer is over, or null when that is not known - the text just changed, or
    /// the pointer is past the end of the value. Null keeps the broad answer rather than guessing at a
    /// position.
    /// </param>
    /// <param name="multiLine">
    /// A multi-line field keeps the full list when the pointer is between tokens instead of
    /// substituting. A body of JSON rendered into one tooltip is a wall of text at hover size, and
    /// unlike a URL nobody thinks of it as a single value.
    /// </param>
    public static string? Describe(VariableTooltipContext context, string? text, int? index, bool multiLine)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tokens = Tokens(text);
        if (tokens.Count == 0)
        {
            return null;
        }

        if (index is { } caret && tokens.FirstOrDefault(t => t.Covers(caret)) is { Key.Length: > 0 } hovered)
        {
            return Line(context, hovered.Key);
        }

        return index is not null && !multiLine
            ? Substituted(context, text!, tokens)
            : string.Join("\n", tokens.Select(t => Line(context, t.Key)));
    }

    /// <summary>True when anything in the text does not resolve - what tints the box amber.</summary>
    public static bool AnyUndefined(VariableTooltipContext context, string? text)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Tokens(text).Any(t => !Resolve(context, t.Key).IsDefined);
    }

    /// <summary>One variable, its value and where that came from.</summary>
    public static string Line(VariableTooltipContext context, string key)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolution = Resolve(context, key);

        return resolution.IsDefined
            ? $"{{{{{key}}}}} = {Display(context, key, resolution.Value)}  ({resolution.SourceName})"
            : $"{{{{{key}}}}}: undefined - not found in \"{context.ActiveEnvironment?.Name ?? "active environment"}\"";
    }

    /// <summary>
    /// The whole value with every <c>{{token}}</c> replaced - what the field will actually send.
    /// </summary>
    /// <remarks>
    /// Built here rather than by calling <see cref="IVariableResolver.Substitute"/>, which returns the
    /// REAL value of a secret. A tooltip is the most screenshotted surface in the app and must not be
    /// the one place a token appears in clear, so the same masking the per-variable line uses applies
    /// here. An undefined token is left standing as <c>{{name}}</c>, exactly as the resolver leaves
    /// it, so a preview never quietly reads as a fully resolved request.
    /// </remarks>
    public static string Substituted(VariableTooltipContext context, string text, IReadOnlyList<VariableToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokens);

        var result = new StringBuilder(text.Length);
        var next = 0;

        foreach (var token in tokens)
        {
            result.Append(text, next, token.Start - next);

            var resolution = Resolve(context, token.Key);
            result.Append(resolution.IsDefined
                ? Display(context, token.Key, resolution.Value)
                : text.Substring(token.Start, token.Length));

            next = token.End;
        }

        return result.Append(text, next, text.Length - next).ToString();
    }

    private static VariableResolution Resolve(VariableTooltipContext context, string key) =>
        context.Resolver.Resolve(key, context.Workspace, context.ActiveEnvironment);

    private static string Display(VariableTooltipContext context, string key, string value) =>
        !context.SecretsRevealed && LooksSecret(key) ? Masked : value;

    /// <summary>
    /// Best-effort mask for what is DISPLAYED only. <see cref="IVariableResolver"/> knows the true
    /// IsSecret flag but does not surface it on <see cref="VariableResolution"/>; matching by name is
    /// a stand-in until it does, and it errs towards masking.
    /// </summary>
    public static bool LooksSecret(string key) =>
        key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenRegex();
}
