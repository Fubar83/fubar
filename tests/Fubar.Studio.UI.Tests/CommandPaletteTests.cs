using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The palette is worth more here than in most applications: there is no menu bar, so it is
/// simultaneously the shortcut surface and the only discovery surface.
///
/// <para>The ranking is the whole feature - a palette that finds the right entry and puts it fourth is
/// one nobody uses twice - so most of these are about order.</para>
/// </summary>
public class CommandPaletteTests
{
    private static PaletteEntry Entry(string title, string category = "Command", string? gesture = null) =>
        new(title, category, gesture, () => Task.CompletedTask);

    private static CommandPaletteViewModel Palette(params string[] titles) =>
        new(titles.Select(t => Entry(t)));

    [Fact]
    public void It_opens_showing_everything()
    {
        var palette = Palette("New Request", "New Folder", "Run selection");

        Assert.Equal(3, palette.Results.Count);
        Assert.NotNull(palette.Selected);
    }

    [Fact]
    public void Typing_narrows_the_list()
    {
        var palette = Palette("New Request", "New Folder", "Run selection");

        palette.Query = "folder";

        Assert.Equal("New Folder", Assert.Single(palette.Results).Title);
    }

    /// <summary>Subsequence matching is what people expect from a palette: "nr" finds "New Request".</summary>
    [Fact]
    public void Initials_find_an_entry()
    {
        var palette = Palette("New Request", "Run selection");

        palette.Query = "nr";

        Assert.Equal("New Request", palette.Results[0].Title);
    }

    /// <summary>Consecutive characters must outrank scattered ones, or the obvious answer sinks.</summary>
    [Fact]
    public void A_consecutive_match_outranks_a_scattered_one()
    {
        var palette = Palette("Import Postman collection...", "Run selection");

        palette.Query = "run";

        Assert.Equal("Run selection", palette.Results[0].Title);
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var palette = Palette("New Request");

        palette.Query = "NEW REQ";

        Assert.Single(palette.Results);
    }

    /// <summary>
    /// The one thing a palette must never do: run something the user is not looking at. Typing until
    /// nothing matches has to clear the selection, or Return fires whatever was selected before.
    /// </summary>
    [Fact]
    public void A_query_matching_nothing_clears_the_selection()
    {
        var palette = Palette("New Request", "New Folder");

        palette.Query = "zzzzzz";

        Assert.Empty(palette.Results);
        Assert.Null(palette.Selected);
    }

    [Fact]
    public async Task Accepting_runs_the_selected_entry()
    {
        var ran = "";
        var palette = new CommandPaletteViewModel([
            new PaletteEntry("New Request", "Command", null, () => { ran = "request"; return Task.CompletedTask; }),
            new PaletteEntry("New Folder", "Command", null, () => { ran = "folder"; return Task.CompletedTask; }),
        ]);

        palette.Query = "folder";
        await palette.AcceptCommand.ExecuteAsync(null);

        Assert.Equal("folder", ran);
    }

    /// <summary>Several entries open a modal of their own; a palette still on screen behind one is a
    /// window the user cannot get rid of.</summary>
    [Fact]
    public async Task The_palette_closes_before_the_action_runs()
    {
        var order = new List<string>();
        var palette = new CommandPaletteViewModel([
            new PaletteEntry("Open", "Command", null, () => { order.Add("action"); return Task.CompletedTask; }),
        ]);
        palette.Accepted += () => order.Add("closed");

        await palette.AcceptCommand.ExecuteAsync(null);

        Assert.Equal(["closed", "action"], order);
    }

    [Fact]
    public async Task Accepting_with_nothing_selected_does_nothing()
    {
        var palette = Palette("New Request");
        palette.Query = "zzzz";

        await palette.AcceptCommand.ExecuteAsync(null);
    }

    /// <summary>A keyboard list that stops dead at the end feels broken.</summary>
    [Fact]
    public void Moving_past_the_end_wraps()
    {
        var palette = Palette("a", "b");

        palette.MoveDownCommand.Execute(null);
        Assert.Equal("b", palette.Selected!.Title);

        palette.MoveDownCommand.Execute(null);
        Assert.Equal("a", palette.Selected!.Title);

        palette.MoveUpCommand.Execute(null);
        Assert.Equal("b", palette.Selected!.Title);
    }

    [Fact]
    public void Moving_in_an_empty_list_is_harmless()
    {
        var palette = Palette("a");
        palette.Query = "zzz";

        palette.MoveDownCommand.Execute(null);

        Assert.Null(palette.Selected);
    }

    // --- the matcher on its own ---------------------------------------------------------------

    [Fact]
    public void An_empty_query_matches_everything()
    {
        Assert.NotNull(FuzzyMatch.Score("anything", ""));
        Assert.NotNull(FuzzyMatch.Score("anything", null));
    }

    [Fact]
    public void A_query_with_no_subsequence_does_not_match()
    {
        Assert.Null(FuzzyMatch.Score("New Request", "xyz"));
    }

    /// <summary>Order matters: the letters must appear in sequence, not merely be present.</summary>
    [Fact]
    public void Letters_out_of_order_do_not_match()
    {
        Assert.Null(FuzzyMatch.Score("abc", "cba"));
    }

    [Fact]
    public void A_word_boundary_match_outranks_one_mid_word()
    {
        var boundary = FuzzyMatch.Score("New Request", "r");
        var midWord = FuzzyMatch.Score("Environment", "r");

        Assert.True(boundary > midWord, $"boundary {boundary} should beat mid-word {midWord}");
    }
}
