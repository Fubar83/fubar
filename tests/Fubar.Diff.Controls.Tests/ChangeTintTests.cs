using Avalonia.Headless.XUnit;
using Fubar.Diff.Controls.Rendering;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// Which colour a changed row is tinted, and how loudly.
///
/// Two rules, and both were wrong until recently. Every changed row now carries a background - modified
/// rows included, where the character spans used to be the only mark and a file of nothing but modified
/// lines therefore had no row-level "here" at all. And a row is drawn QUIETLY unless it is the
/// difference being read: with everything tinted, drawing them all at full strength (which is what an
/// unnavigated document used to do) is a wall of colour with nothing standing out in it.
/// </summary>
public class ChangeTintTests
{
    [AvaloniaTheory]
    [InlineData(ChangeKind.Inserted)]
    [InlineData(ChangeKind.Deleted)]
    public void A_one_sided_row_is_tinted_by_its_own_kind(ChangeKind kind)
    {
        // Nothing to decide: an inserted row exists only on the right and a deleted one only on the
        // left, so the kind already says which colour.
        Assert.Equal(kind, ChangeLineBackgroundRenderer.TintKind(kind, DiffSide.Left));
        Assert.Equal(kind, ChangeLineBackgroundRenderer.TintKind(kind, DiffSide.Right));
    }

    [AvaloniaFact]
    public void A_modified_row_takes_the_colour_of_the_side_it_is_on()
    {
        // The left document lost this text and the right gained some - which is also how the words
        // inside the row are already highlighted, so row and spans agree.
        Assert.Equal(ChangeKind.Deleted, ChangeLineBackgroundRenderer.TintKind(ChangeKind.Modified, DiffSide.Left));
        Assert.Equal(ChangeKind.Inserted, ChangeLineBackgroundRenderer.TintKind(ChangeKind.Modified, DiffSide.Right));
    }

    [AvaloniaFact]
    public void A_modified_row_in_a_pane_that_is_neither_side_keeps_its_own_colour()
    {
        // The unified view and a three-way base column. Neither produces a modified row today, so this
        // is the safety net rather than a case to design around - what it must NOT do is guess a side
        // and paint half a merge in the removal colour.
        Assert.Equal(ChangeKind.Modified, ChangeLineBackgroundRenderer.TintKind(ChangeKind.Modified, null));
    }

    [AvaloniaFact]
    public void Every_changed_kind_has_a_tint()
    {
        // The regression this file exists for: Modified used to return null here, which is what left
        // the commonest kind of change with no row-level mark.
        var host = Host();

        Assert.NotNull(DiffLineColors.LineBackground(host, ChangeKind.Inserted));
        Assert.NotNull(DiffLineColors.LineBackground(host, ChangeKind.Deleted));
        Assert.NotNull(DiffLineColors.LineBackground(host, ChangeKind.Modified));
    }

    [AvaloniaFact]
    public void An_unchanged_row_has_none()
    {
        Assert.Null(DiffLineColors.LineBackground(Host(), ChangeKind.Unchanged));
    }

    [AvaloniaFact]
    public void The_current_difference_is_tinted_more_strongly_than_the_rest()
    {
        // "Low contrast for all of them, highlighted for the one you are on" is the whole arrangement,
        // and it only works while these two are actually different.
        var host = Host();

        var faded = Opacity(DiffLineColors.LineBackground(host, ChangeKind.Deleted, DiffEmphasis.Faded));
        var current = Opacity(DiffLineColors.LineBackground(host, ChangeKind.Deleted, DiffEmphasis.Normal));

        Assert.True(faded < current, $"faded {faded} should be quieter than current {current}");
    }

    [AvaloniaFact]
    public void The_words_that_changed_stay_louder_than_the_row_under_them()
    {
        // The row says WHERE, the span says WHAT. If the row tint ever caught up with the span tint,
        // the precise half of the signal would disappear into the imprecise one.
        var host = Host();

        foreach (var emphasis in new[] { DiffEmphasis.Faded, DiffEmphasis.Normal })
        {
            var line = Opacity(DiffLineColors.LineBackground(host, ChangeKind.Deleted, emphasis));
            var span = Opacity(DiffLineColors.SpanBackground(host, ChangeKind.Deleted, emphasis));

            Assert.True(span > line, $"{emphasis}: span {span} should be louder than line {line}");
        }
    }

    /// <summary>
    /// A shown window as the host: the palette these tints resolve from lives in the test app's
    /// resources, and a control that is not in a tree reaching it finds nothing - DiffLineColors
    /// returns null for a missing token rather than throwing, so the assertions would quietly be
    /// testing an uncoloured build of the thing under test.
    /// </summary>
    private static Avalonia.Controls.Window Host()
    {
        var window = new Avalonia.Controls.Window();
        window.Show();

        return window;
    }

    /// <summary>The alpha the tint was built with - see <c>DiffLineColors.Tinted</c>.</summary>
    private static double Opacity(Avalonia.Media.IBrush? brush) =>
        brush is Avalonia.Media.ISolidColorBrush solid ? solid.Color.A / 255.0 : 0;
}
