using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// How strongly a row's tint should read, relative to whatever the current hunk is.
///
/// <see cref="Faded"/> is for a row that is a real difference but NOT the one the user just navigated
/// to - still scannable, but visibly receding so the current one is not competing for attention with
/// every other change in the file. <see cref="Emphasized"/> is the opposite case: the close-up panes
/// (DiffDetailPane and its Json-mode counterpart) show nothing BUT the current difference, so there is
/// no "other" for it to stand out against - the tint gets to be the loudest thing on screen instead.
/// </summary>
internal enum DiffEmphasis
{
    Faded,
    Normal,
    Emphasized,
}

/// <summary>
/// Resolves the brushes used to tint diff lines and character spans.
///
/// Colours come from the shared <c>Fubar.Controls</c> palette rather than being hard-coded, so both
/// theme variants keep working and the app stays visually consistent with Fubar API Studio. They are
/// looked up per render pass rather than cached: the palette's ThemeDictionaries swap the underlying
/// values when the user switches Dark/Light, and a cached brush would keep painting the old theme.
/// </summary>
internal static class DiffLineColors
{
    /// <summary>
    /// How strong a full-line tint is: quiet enough to read text through at every level, and clearly
    /// stronger on the difference the user is actually on.
    ///
    /// EVERY changed row gets one of these - including modified rows, which had none at all until
    /// recently. The old bargain was that a modified line's own words carry a precise span tint, so
    /// washing the row underneath would compete with it; the trouble is that it left the commonest
    /// kind of change with no row-level mark, so scanning a file for "which lines changed" worked for
    /// insertions and deletions and simply did not for modifications. Both now: the row says WHERE at
    /// a glance, the span still says WHAT, and the gap between 0.12 and 0.55 is what keeps the span
    /// the louder of the two.
    /// </summary>
    private static double LineOpacity(DiffEmphasis emphasis) => emphasis == DiffEmphasis.Faded ? 0.12 : 0.28;

    /// <summary>
    /// Background tint for a WHOLE line in the MAIN panes, or null when the line needs no tint.
    /// Never called with <see cref="DiffEmphasis.Emphasized"/> - the close-up panes skip this renderer
    /// entirely (see <c>ChangeLineBackgroundRenderer.Draw</c>) in favour of <see cref="SpanBackground"/>
    /// alone, so there is nothing here for that level to mean.
    ///
    /// A MODIFIED row is tinted in the colour of the side it is on - the removal colour on the left,
    /// the addition colour on the right - which is why the caller resolves the kind from the row's own
    /// spans before asking (see <c>ChangeLineBackgroundRenderer.TintKind</c>): those spans are Deleted
    /// on the left and Inserted on the right already, so the row and the words inside it agree. The
    /// Modified case below is the fallback for a row that has no spans to say which side it is (a
    /// blank line paired with a written one, or a three-way base column), and is deliberately a third
    /// colour rather than a guess at one of the two.
    /// </summary>
    public static IBrush? LineBackground(StyledElement host, ChangeKind kind, DiffEmphasis emphasis = DiffEmphasis.Normal) => kind switch
    {
        ChangeKind.Inserted => Tinted(host, "MethodPostBrush", LineOpacity(emphasis)),
        ChangeKind.Deleted => Tinted(host, "MethodDeleteBrush", LineOpacity(emphasis)),
        ChangeKind.Modified => Tinted(host, "MethodPutBrush", LineOpacity(emphasis)),
        ChangeKind.Filler => Tinted(host, "BgHover", 0.35),
        _ => null,
    };

    /// <summary>
    /// The band behind a row that differs only in ways the options were told to ignore - an ignored
    /// path, a reordered list element, or a line the whitespace/case/comment rules equalised.
    ///
    /// Quiet on purpose, and NEUTRAL rather than a change colour: it says "something differs here and
    /// you asked not to be told", which must not compete with the differences that were not ignored.
    /// Any louder and adding a rule would stop visibly quietening the diff, which is the whole point of
    /// adding one.
    ///
    /// <para>Raised from 0.07, which was too close to invisible to do the job it exists for - a mark
    /// nobody notices is the same as no mark, and the reader is then back to being unable to tell
    /// agreement from a suppressed disagreement. It stays comfortably below an ordinary change row
    /// (<see cref="LineOpacity"/>, 0.12 faded / 0.28) and, being grey where those are red and green, it
    /// is told apart by hue rather than only by weight.</para>
    /// </summary>
    public static IBrush? IgnoredBackground(StyledElement host) => Tinted(host, "TextSecondary", 0.14);

    /// <summary>
    /// The same signal over the CHARACTERS of an ignored change rather than a whole row.
    ///
    /// Stronger than <see cref="IgnoredBackground"/> for the same reason <see cref="SpanBackground"/> is
    /// stronger than <see cref="LineBackground"/>: a span covers a few characters instead of the pane's
    /// full width, so the same opacity reads as far less. The gap between the two is what keeps a span
    /// and a row saying the same thing at the same apparent strength.
    /// </summary>
    public static IBrush? IgnoredSpanBackground(StyledElement host) => Tinted(host, "TextSecondary", 0.30);

    /// <summary>
    /// The band behind a row both sides of a three-way merge changed differently.
    ///
    /// Amber rather than one of the change colours, and checked BEFORE them: green and red already
    /// mean "added" and "removed", and every column of a conflict is one of those - so reusing them
    /// would leave the one thing that needs a decision looking exactly like the many that do not.
    /// Stronger than an ordinary change tint too, because a conflict is the only row in the window that
    /// will not resolve itself if ignored.
    ///
    /// Distinct from <see cref="CurrentHunkAccent"/>'s orange by SHAPE as much as hue: that is a bar
    /// down the edge and a hairline box, this is a full-width wash, so the two read as different things
    /// even where the colours are neighbours.
    /// </summary>
    public static IBrush? ConflictBackground(StyledElement host, DiffEmphasis emphasis = DiffEmphasis.Normal) =>
        Tinted(host, "PostmanAmberBrush", emphasis == DiffEmphasis.Faded ? 0.12 : 0.30);

    /// <summary>
    /// The band behind a row that is one half of a block that only MOVED.
    ///
    /// Blue rather than green or red, and painted INSTEAD of them rather than over them, because the
    /// whole message is "this is not something you need to read". Green on one side and red on the
    /// other says two things happened; one colour on both halves says one thing did, and says which
    /// two places it connects.
    ///
    /// Kept at the same weight as an ordinary change tint. A move is still a difference - it can break
    /// a file - so quietening it below the changes around it would be overstating the case.
    /// </summary>
    public static IBrush? MovedBackground(StyledElement host, DiffEmphasis emphasis = DiffEmphasis.Normal) =>
        Tinted(host, "PostmanBlueBrush", LineOpacity(emphasis));

    /// <summary>
    /// The tint for the characters that actually changed within a modified line - the precise half of
    /// the pair, sitting on top of the row tint <see cref="LineBackground"/> now paints underneath it.
    /// Kept well above <see cref="LineOpacity"/> at every level so the words stay the loudest thing on
    /// the row, which is the point: the row says where, this says what.
    /// At <see cref="DiffEmphasis.Emphasized"/> (the close-up panes) it is also the ONLY signal for a
    /// whole inserted/deleted row, once <see cref="LineBackground"/> stops drawing there - see
    /// <c>CharSpanColorizer</c>, which synthesizes a whole-row span for exactly that case.
    /// </summary>
    public static IBrush? SpanBackground(StyledElement host, ChangeKind kind, DiffEmphasis emphasis = DiffEmphasis.Normal) => kind switch
    {
        ChangeKind.Inserted => Tinted(host, "MethodPostBrush", OpacityFor(emphasis, faded: 0.30, normal: 0.55, emphasized: 0.82)),
        ChangeKind.Deleted => Tinted(host, "MethodDeleteBrush", OpacityFor(emphasis, faded: 0.30, normal: 0.55, emphasized: 0.82)),
        _ => null,
    };

    private static double OpacityFor(DiffEmphasis emphasis, double faded, double normal, double emphasized) => emphasis switch
    {
        DiffEmphasis.Faded => faded,
        DiffEmphasis.Emphasized => emphasized,
        _ => normal,
    };

    /// <summary>
    /// Extra wash over the hunk the user is currently on, painted on top of its change tint.
    ///
    /// Kept low: it stacks with a tint that is already there, and the point is to make one difference
    /// findable among many, not to obscure the text inside it - the accent bar and outline below do
    /// most of the work. Only used by the MAIN Json panes now - the Json close-up highlights via
    /// <see cref="CurrentSpanBackground"/> instead.
    /// </summary>
    public static IBrush? CurrentHunkWash(StyledElement host) => Tinted(host, "PostmanOrangeBrush", 0.10);

    /// <summary>The bar down the edge of the current hunk, and the hairline boxing it in.</summary>
    public static IBrush? CurrentHunkAccent(StyledElement host) => Tinted(host, "PostmanOrangeBrush", 0.95);

    public static IBrush? CurrentHunkOutline(StyledElement host) => Tinted(host, "PostmanOrangeBrush", 0.50);

    /// <summary>
    /// The Json close-up's own highlight - see <c>SpanTextColorizer</c>. Unlike
    /// <see cref="CurrentHunkWash"/>, this paints only the exact characters a change's
    /// <c>SourceSpan</c> covers rather than a full-width band across every line it touches, so it can
    /// run at a stronger opacity without turning the whole excerpt into a solid block of colour.
    /// </summary>
    public static IBrush? CurrentSpanBackground(StyledElement host) => Tinted(host, "PostmanOrangeBrush", 0.60);

    /// <summary>
    /// Looks up a palette token for the host's CURRENT theme variant and applies an opacity.
    ///
    /// Returns null when the token is missing, so renaming something in the design system degrades to
    /// "no tint" rather than throwing inside a render pass - which would take the window down.
    /// </summary>
    private static IBrush? Tinted(StyledElement host, string token, double opacity)
    {
        if (host is not IResourceHost resourceHost)
        {
            return null;
        }

        // Pass the variant explicitly rather than relying on the ambient lookup: these brushes are
        // resolved from a renderer, not from a styled property, so nothing else would apply it.
        if (!resourceHost.TryFindResource(token, host.ActualThemeVariant, out var resource)
            || resource is not ISolidColorBrush brush)
        {
            return null;
        }

        var color = brush.Color;
        return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B));
    }
}
