using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;

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

    // --- initials, best alignment, and where it matched ------------------------------------------

    [Fact]
    public void Initials_of_separate_words_outrank_the_same_letters_mid_word()
    {
        // "hj" is Henrik Johansson, and it is the initials that make it so - both letters land on a
        // word start, which is worth more than the same two letters found in the middle of words.
        var initials = FuzzyMatch.Score("Henrik Johansson", "hj");
        var scattered = FuzzyMatch.Score("The high jump", "hj");

        Assert.NotNull(initials);
        Assert.True(initials > scattered, $"initials {initials} should beat scattered {scattered}");
    }

    [Fact]
    public void The_BEST_alignment_wins_not_the_first_one()
    {
        // The old matcher walked greedily and took the p of "Copy", then scored the scattered thing it
        // had just chosen to make. Every alignment is considered now, so "posh" lands on PowerShell -
        // which is also what makes the highlight honest.
        var match = FuzzyMatch.Match("Copy as cURL (PowerShell)", "posh");

        Assert.NotNull(match);
        Assert.Equal("PoSh", string.Concat(match!.Value.Positions.Select(i => "Copy as cURL (PowerShell)"[i])));
    }

    [Fact]
    public void A_run_of_characters_beats_the_same_letters_spread_out()
    {
        var contiguous = FuzzyMatch.Score("Import from curl...", "impo");
        var spread = FuzzyMatch.Score("Inspect my post office", "impo");

        Assert.True(contiguous > spread, $"contiguous {contiguous} should beat spread {spread}");
    }

    [Fact]
    public void Positions_are_ascending_and_inside_the_candidate()
    {
        var match = FuzzyMatch.Match("orders/create-order", "ocr");

        Assert.NotNull(match);
        var positions = match!.Value.Positions;
        Assert.Equal(3, positions.Count);
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.All(positions, p => Assert.InRange(p, 0, "orders/create-order".Length - 1));
    }

    // --- the highlight the row draws --------------------------------------------------------------

    private static PaletteRow Row(string title, string query) =>
        PaletteRow.For(
            new PaletteEntry(title, "Command", null, () => Task.CompletedTask),
            FuzzyMatch.Match(title, query)!.Value.Positions);

    [Fact]
    public void The_runs_reassemble_into_the_title()
    {
        // The one property that must hold whatever the query: the row draws the runs and nothing else,
        // so a splitter that dropped or doubled a character would rewrite what the entry says.
        foreach (var query in new[] { "hj", "h", "hjohansson", "nk", "" })
        {
            var row = Row("Henrik Johansson", query);

            Assert.Equal("Henrik Johansson", string.Concat(row.Runs.Select(r => r.Text)));
        }
    }

    [Fact]
    public void Matched_characters_are_the_ones_the_matcher_found()
    {
        var row = Row("Henrik Johansson", "hj");

        Assert.Equal("HJ", string.Concat(row.Runs.Where(r => r.IsMatch).Select(r => r.Text)));
    }

    [Fact]
    public void Adjacent_matches_become_one_run_rather_than_four()
    {
        // Four bold TextBlocks in a row draw differently from one: the spacing between them is where
        // the letters of "Impo" would visibly come apart.
        var row = Row("Import from curl...", "impo");

        Assert.Equal(["Impo", "rt from curl..."], row.Runs.Select(r => r.Text));
        Assert.Equal([true, false], row.Runs.Select(r => r.IsMatch));
    }

    [Fact]
    public void With_no_query_the_whole_title_is_one_unmatched_run()
    {
        var row = Row("New Request", "");

        var run = Assert.Single(row.Runs);
        Assert.Equal("New Request", run.Text);
        Assert.False(run.IsMatch);
    }

    [Fact]
    public void Filtering_highlights_what_it_matched()
    {
        var palette = Palette("Copy as cURL", "Copy as cURL (PowerShell)");
        palette.Query = "cacp";

        var row = palette.Results.First();

        Assert.Equal("Copy as cURL (PowerShell)", row.Title);
        Assert.Equal("CacP", string.Concat(row.Runs.Where(r => r.IsMatch).Select(r => r.Text)));
    }

    /// <summary>
    /// The window, not the view model: the runs reach the screen as separate TextBlocks carrying a
    /// style class, and a class name is a string in markup that no compiler checks. Renaming the style
    /// or the class would leave every character drawn in the same colour, with every test above still
    /// green.
    /// </summary>
    [AvaloniaFact]
    public void The_matched_characters_are_drawn_in_their_own_style()
    {
        var palette = Palette("Henrik Johansson", "New Request");
        palette.Query = "hj";

        var window = new CommandPalette(palette);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var highlighted = window.GetVisualDescendants()
            .OfType<Avalonia.Controls.TextBlock>()
            .Where(t => t.Classes.Contains("matched"))
            .Select(t => t.Text)
            .ToList();

        Assert.Equal(["H", "J"], highlighted);
    }
}
