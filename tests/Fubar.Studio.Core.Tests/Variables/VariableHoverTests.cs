using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Core.Tests.Variables;

/// <summary>
/// What a hover over a field full of <c>{{variables}}</c> says.
///
/// <para>It used to list every token in the box whatever the pointer was near: a URL with five of
/// them answered a question about one with a five-line block, and never answered the other question
/// anybody asks of a URL bar - what will this actually send.</para>
/// </summary>
public class VariableHoverTests
{
    private static readonly Workspace Ws = new() { RootPath = "/w", Manifest = new AppManifest { Name = "t" } };

    private static readonly WorkspaceEnvironment Staging = new() { Id = "stg", Name = "Staging" };

    /// <summary>Resolves from a table; everything else is undefined, which is the interesting case.</summary>
    private sealed class TableResolver(params (string Key, string Value)[] known) : IVariableResolver
    {
        public VariableResolution Resolve(string key, Workspace workspace, WorkspaceEnvironment? activeEnvironment)
        {
            foreach (var (k, v) in known)
            {
                if (string.Equals(k, key, StringComparison.Ordinal))
                {
                    return new VariableResolution(true, v, "Staging");
                }
            }

            return new VariableResolution(false, "", "");
        }

        public string Substitute(string? input, Workspace workspace, WorkspaceEnvironment? activeEnvironment) =>
            throw new NotSupportedException("The hover builds its own preview so secrets stay masked.");

        public IReadOnlyList<VariableSuggestion> ListAvailable(Workspace workspace, WorkspaceEnvironment? activeEnvironment) => [];
    }

    private static VariableTooltipContext Context(bool secretsRevealed = false, params (string, string)[] known) =>
        new(new TableResolver(known), Ws, Staging, secretsRevealed);

    private const string Url = "{{baseUrl}}/orders/{{orderId}}?key={{page}}";

    /// <summary>Same shape, but the query variable is named so that LooksSecret matches it.</summary>
    private const string SecretUrl = "{{baseUrl}}/orders?key={{apiKey}}";

    // ---- Hovering one variable ------------------------------------------------------------------

    [Fact]
    public void Hovering_a_token_says_only_that_variable()
    {
        var context = Context(known: [("baseUrl", "https://staging.example"), ("orderId", "42")]);

        // Somewhere inside {{orderId}}, which starts at index 18.
        var tip = VariableHover.Describe(context, Url, index: 22, multiLine: false);

        Assert.Equal("{{orderId}} = 42  (Staging)", tip);
    }

    [Fact]
    public void The_first_and_last_character_of_a_token_still_count_as_inside_it()
    {
        var context = Context(known: [("baseUrl", "https://staging.example")]);

        Assert.StartsWith("{{baseUrl}}", VariableHover.Describe(context, Url, 0, false), StringComparison.Ordinal);
        Assert.StartsWith("{{baseUrl}}", VariableHover.Describe(context, Url, 10, false), StringComparison.Ordinal);
    }

    /// <summary>The position just past <c>}}</c> belongs to the text after it, not to the variable.</summary>
    [Fact]
    public void The_character_after_a_token_is_not_part_of_it()
    {
        var context = Context(known: [("baseUrl", "https://staging.example")]);

        var tip = VariableHover.Describe(context, Url, index: 11, multiLine: false);

        Assert.DoesNotContain("{{baseUrl}} =", tip, StringComparison.Ordinal);
    }

    [Fact]
    public void An_undefined_variable_says_so_and_names_the_environment()
    {
        var tip = VariableHover.Describe(Context(), Url, index: 22, multiLine: false);

        Assert.Equal("{{orderId}}: undefined - not found in \"Staging\"", tip);
    }

    // ---- Hovering the rest of a single-line field ------------------------------------------------

    [Fact]
    public void Hovering_between_tokens_in_one_line_shows_the_whole_value_resolved()
    {
        var context = Context(known: [("baseUrl", "https://staging.example"), ("orderId", "42"), ("page", "2")]);

        // Index 11 is the "/" after {{baseUrl}}.
        var tip = VariableHover.Describe(context, Url, index: 11, multiLine: false);

        Assert.Equal("https://staging.example/orders/42?key=2", tip);
    }

    /// <summary>Left standing exactly as the resolver leaves it, so a preview never quietly reads as a
    /// request that is ready to send.</summary>
    [Fact]
    public void An_undefined_token_stays_visible_in_the_resolved_preview()
    {
        var context = Context(known: [("baseUrl", "https://staging.example"), ("page", "2")]);

        var tip = VariableHover.Describe(context, Url, index: 11, multiLine: false);

        Assert.Equal("https://staging.example/orders/{{orderId}}?key=2", tip);
    }

    /// <summary>
    /// A tooltip is the most screenshotted surface in the app. The preview is built from the same
    /// masking rule as the per-variable line rather than from IVariableResolver.Substitute, which
    /// returns the real thing - the fake resolver throws from Substitute to keep it that way.
    /// </summary>
    [Fact]
    public void A_secret_is_masked_in_the_resolved_preview_too()
    {
        var context = Context(known: [("baseUrl", "https://staging.example"), ("apiKey", "sk-live-1234")]);

        var tip = VariableHover.Describe(context, SecretUrl, index: 11, multiLine: false);

        Assert.Equal($"https://staging.example/orders?key={VariableHover.Masked}", tip);
        Assert.DoesNotContain("sk-live", tip, StringComparison.Ordinal);
    }

    [Fact]
    public void Revealing_secrets_shows_them_in_the_preview()
    {
        var context = Context(
            secretsRevealed: true,
            known: [("baseUrl", "https://staging.example"), ("apiKey", "sk-live-1234")]);

        var tip = VariableHover.Describe(context, SecretUrl, index: 11, multiLine: false);

        Assert.EndsWith("key=sk-live-1234", tip, StringComparison.Ordinal);
    }

    // ---- The cases that keep the old, broader answer ----------------------------------------------

    /// <summary>A body of JSON substituted into one tooltip is a wall of text at hover size, and
    /// unlike a URL nobody thinks of it as a single value.</summary>
    [Fact]
    public void A_multi_line_field_keeps_the_list_when_the_pointer_is_between_tokens()
    {
        var context = Context(known: [("baseUrl", "https://staging.example"), ("orderId", "42"), ("page", "2")]);

        var tip = VariableHover.Describe(context, Url, index: 11, multiLine: true);

        Assert.Equal(3, tip!.Split('\n').Length);
    }

    /// <summary>A multi-line field still narrows to the one token under the pointer - only the
    /// fallback differs.</summary>
    [Fact]
    public void A_multi_line_field_still_narrows_to_the_hovered_token()
    {
        var context = Context(known: [("orderId", "42")]);

        Assert.Equal("{{orderId}} = 42  (Staging)", VariableHover.Describe(context, Url, 22, multiLine: true));
    }

    /// <summary>No pointer position - the text just changed - so answer broadly rather than guess at
    /// a position.</summary>
    [Fact]
    public void With_no_pointer_position_every_token_is_listed()
    {
        var context = Context(known: [("baseUrl", "b"), ("orderId", "42"), ("page", "2")]);

        var tip = VariableHover.Describe(context, Url, index: null, multiLine: false);

        Assert.Equal(3, tip!.Split('\n').Length);
    }

    [Fact]
    public void Text_with_no_variables_has_nothing_to_say()
    {
        Assert.Null(VariableHover.Describe(Context(), "https://example.test/orders", 5, false));
        Assert.Null(VariableHover.Describe(Context(), "", null, false));
        Assert.Null(VariableHover.Describe(Context(), null, null, false));
    }

    // ---- Tokenising -------------------------------------------------------------------------------

    [Fact]
    public void Tokens_carry_where_each_one_sits()
    {
        var tokens = VariableHover.Tokens(Url);

        Assert.Equal(["baseUrl", "orderId", "page"], tokens.Select(t => t.Key));
        Assert.Equal(0, tokens[0].Start);
        Assert.Equal(11, tokens[0].End);
    }

    [Fact]
    public void Anything_undefined_is_what_tints_the_box()
    {
        Assert.True(VariableHover.AnyUndefined(Context(known: [("baseUrl", "b")]), Url));
        Assert.False(VariableHover.AnyUndefined(
            Context(known: [("baseUrl", "b"), ("orderId", "42"), ("page", "2")]), Url));
        Assert.False(VariableHover.AnyUndefined(Context(), "no variables here"));
    }
}
