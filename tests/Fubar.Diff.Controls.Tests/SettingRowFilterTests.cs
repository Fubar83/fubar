using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Fubar.Controls;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// The settings search. A row decides for ITSELF whether it survives a filter, driven by an inherited
/// attached property the window sets once - so a row added later is searchable without anyone
/// remembering to register it, which is the failure mode a per-row binding would have.
/// </summary>
public class SettingRowFilterTests
{
    private static (StackPanel Host, SettingRow Row) Build(string header, string? description = null)
    {
        var row = new SettingRow { Header = header, Description = description };
        var host = new StackPanel();
        host.Children.Add(row);

        return (host, row);
    }

    [AvaloniaFact]
    public void With_nothing_typed_every_row_survives()
    {
        var (_, row) = Build("Ignore blank lines");

        Assert.True(row.MatchesFilter);
    }

    [AvaloniaFact]
    public void A_filter_set_on_the_container_reaches_the_rows_inside_it()
    {
        // The whole point of the property being inherited: one line at the top of the window.
        var (host, row) = Build("Ignore blank lines");

        SettingRow.SetFilter(host, "blank");

        Assert.True(row.MatchesFilter);
    }

    [AvaloniaFact]
    public void A_row_that_does_not_match_filters_itself_out()
    {
        var (host, row) = Build("Syntax highlighting");

        SettingRow.SetFilter(host, "blank");

        Assert.False(row.MatchesFilter);
    }

    [AvaloniaFact]
    public void The_description_is_searched_as_well_as_the_header()
    {
        // Where the words people actually know live. Nobody searches for "NormalizeUnicode"; they
        // search for "encoding" or "accented", which is what the sentence under the header says.
        var (host, row) = Build(
            "Ignore invisible encoding differences",
            "An accented e can be stored two ways, and macOS and Windows disagree about which.");

        SettingRow.SetFilter(host, "accented");

        Assert.True(row.MatchesFilter);
    }

    [AvaloniaFact]
    public void Case_does_not_matter()
    {
        var (host, row) = Build("Ignore blank lines");

        SettingRow.SetFilter(host, "BLANK");

        Assert.True(row.MatchesFilter);
    }

    [AvaloniaFact]
    public void Whitespace_alone_is_not_a_search()
    {
        // The clear button and a half-deleted term both leave this behind; treating it as a filter
        // would empty the window for no reason.
        var (host, row) = Build("Ignore blank lines");

        SettingRow.SetFilter(host, "   ");

        Assert.True(row.MatchesFilter);
    }

    [AvaloniaFact]
    public void Clearing_the_search_brings_every_row_back()
    {
        var (host, row) = Build("Syntax highlighting");

        SettingRow.SetFilter(host, "blank");
        Assert.False(row.MatchesFilter);

        SettingRow.SetFilter(host, "");
        Assert.True(row.MatchesFilter);
    }

    [AvaloniaFact]
    public void A_row_added_after_the_filter_was_set_is_filtered_too()
    {
        // Inheritance has to apply on attach, not only on change - otherwise a row inside a section
        // that renders lazily would ignore a search that was already running.
        var host = new StackPanel();
        SettingRow.SetFilter(host, "blank");

        var row = new SettingRow { Header = "Syntax highlighting" };
        host.Children.Add(row);

        Assert.False(row.MatchesFilter);
    }
}
