using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>
/// One line of a settings page: a plain-language <see cref="HeaderedContentControl.Header"/>, a muted
/// <see cref="Description"/> under it, and the control itself (<c>Content</c> - a switch, a combo, a
/// number box) on the right.
///
/// The description is the point. A settings window whose explanations all live in tooltips reads as a
/// wall of terse labels, and the one thing a user needs in order to answer "do I want this?" is the
/// thing they have to hover to find - if they suspect it is there at all. Written out, each row
/// answers its own question, and the tooltip goes back to being for detail nobody needs the first
/// time.
/// </summary>
public class SettingRow : HeaderedContentControl
{
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    /// <summary>
    /// One short sentence saying what turning this on does, in the user's words rather than the
    /// codebase's. Hidden when empty, for a row whose header is genuinely self-explanatory.
    /// </summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// What the settings search box currently holds.
    ///
    /// <para>Attached and INHERITED, so a settings window sets it once at the top and every row below
    /// filters itself. The alternative - a binding on each of two dozen rows - is the kind of thing
    /// that works until somebody adds the twenty-fifth row and forgets, leaving one setting that can
    /// never be found by searching for it.</para>
    /// </summary>
    public static readonly AttachedProperty<string?> FilterProperty =
        AvaloniaProperty.RegisterAttached<SettingRow, StyledElement, string?>(
            "Filter", defaultValue: null, inherits: true);

    public static string? GetFilter(StyledElement element) => element.GetValue(FilterProperty);

    public static void SetFilter(StyledElement element, string? value) => element.SetValue(FilterProperty, value);

    private static readonly DirectProperty<SettingRow, bool> MatchesFilterProperty =
        AvaloniaProperty.RegisterDirect<SettingRow, bool>(nameof(MatchesFilter), o => o.MatchesFilter);

    private bool _matchesFilter = true;

    /// <summary>
    /// Whether this row survives the current search. True when nothing is being searched for.
    ///
    /// <para>Matched against the header AND the description, because the description is where the
    /// words a user actually knows live: nobody searches for "NormalizeUnicode", they search for
    /// "invisible" or "encoding", which is what the sentence under the header says.</para>
    /// </summary>
    public bool MatchesFilter
    {
        get => _matchesFilter;
        private set => SetAndRaise(MatchesFilterProperty, ref _matchesFilter, value);
    }

    static SettingRow()
    {
        // Header is inherited from HeaderedContentControl, so all three are watched the same way -
        // a row whose header changes must not keep a stale verdict.
        FilterProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.Recompute());
        DescriptionProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.Recompute());
        HeaderProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.Recompute());
    }

    private void Recompute()
    {
        var filter = GetFilter(this)?.Trim();

        if (string.IsNullOrEmpty(filter))
        {
            MatchesFilter = true;

            return;
        }

        MatchesFilter =
            Contains(Header as string, filter) || Contains(Description, filter);

        static bool Contains(string? text, string filter) =>
            text is not null && text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    protected override Type StyleKeyOverride => typeof(SettingRow);
}
