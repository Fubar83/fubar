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
    /// Background tint for a WHOLE line in the MAIN panes, or null when the line needs no tint.
    /// Never called with <see cref="DiffEmphasis.Emphasized"/> - the close-up panes skip this renderer
    /// entirely (see <c>ChangeLineBackgroundRenderer.Draw</c>) in favour of <see cref="SpanBackground"/>
    /// alone, so there is nothing here for that level to mean.
    ///
    /// Modified is deliberately null: a modified line's own words already get a stronger,
    /// precisely-located tint from <see cref="SpanBackground"/>, and washing the entire row underneath
    /// that competed with it rather than helping - "here is what changed" reads better than "here is a
    /// row, somewhere in which something changed". Inserted/Deleted rows have no such span to defer
    /// to in the main panes - the whole row IS the difference - so they keep a full-line tint there.
    /// </summary>
    public static IBrush? LineBackground(StyledElement host, ChangeKind kind, DiffEmphasis emphasis = DiffEmphasis.Normal) => kind switch
    {
        ChangeKind.Inserted => Tinted(host, "MethodPostBrush", emphasis == DiffEmphasis.Faded ? 0.07 : 0.18),
        ChangeKind.Deleted => Tinted(host, "MethodDeleteBrush", emphasis == DiffEmphasis.Faded ? 0.07 : 0.18),
        ChangeKind.Modified => null,
        ChangeKind.Filler => Tinted(host, "BgHover", 0.35),
        _ => null,
    };

    /// <summary>
    /// The band behind a row that differs only at ignored paths.
    ///
    /// Barely there on purpose, and neutral rather than a change colour: it says "something differs
    /// here and you asked not to be told", which must not compete for attention with the differences
    /// that were not ignored. Any stronger and adding a rule would not visibly quieten the diff,
    /// which is the whole point of adding one.
    /// </summary>
    public static IBrush? IgnoredBackground(StyledElement host) => Tinted(host, "TextSecondary", 0.07);

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
    /// The tint for the characters that actually changed within a modified line - now the PRIMARY
    /// signal for a modified row, since <see cref="LineBackground"/> no longer washes the row itself.
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
