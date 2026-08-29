using TextMateSharp.Grammars;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// That the grammars the panes ask for are actually in the package we ship.
///
/// This exists because the failure it guards is SILENT. <c>DiffEditorPane.ScopeFor</c> treats an
/// unresolvable extension as "this file gets no colour", which is the correct behaviour for a
/// <c>.log</c> and indistinguishable from a broken dependency for a <c>.cs</c>. Nothing would throw,
/// nothing would be logged, and the diff would still be perfectly usable - just monochrome, forever,
/// until someone noticed.
/// </summary>
public class SyntaxGrammarTests
{
    private static readonly RegistryOptions Registry = new(ThemeName.DarkPlus);

    [Theory]
    [InlineData(".cs")]
    [InlineData(".ts")]
    [InlineData(".tsx")]
    [InlineData(".js")]
    [InlineData(".jsx")]
    [InlineData(".json")]
    [InlineData(".xml")]
    public void A_grammar_resolves_for_the_extensions_we_promise(string extension) =>
        Assert.False(string.IsNullOrEmpty(Registry.GetScopeByExtension(extension)));

    [Fact]
    public void An_unknown_extension_resolves_to_nothing_rather_than_throwing()
    {
        // The other half of the contract: the pane's "no grammar" path has to be reachable normally,
        // not only through its exception handler.
        var scope = Registry.GetScopeByExtension(".zzznotathing");

        Assert.True(string.IsNullOrEmpty(scope));
    }

    [Theory]
    [InlineData(ThemeName.DarkPlus)]
    [InlineData(ThemeName.LightPlus)]
    public void Both_themes_the_panes_switch_between_load(ThemeName theme) =>
        Assert.NotNull(Registry.LoadTheme(theme));
}
