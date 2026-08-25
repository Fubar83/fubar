using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Rendering;

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
    /// <summary>Background tint for a whole line, or null when the line needs no tint.</summary>
    public static IBrush? LineBackground(StyledElement host, ChangeKind kind) => kind switch
    {
        ChangeKind.Inserted => Tinted(host, "MethodPostBrush", 0.18),
        ChangeKind.Deleted => Tinted(host, "MethodDeleteBrush", 0.18),
        ChangeKind.Modified => Tinted(host, "MethodPutBrush", 0.16),
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
    /// Stronger tint for the characters that actually changed within a modified line. It sits ON TOP
    /// of the line tint, so it must be noticeably denser or it simply disappears into it.
    /// </summary>
    public static IBrush? SpanBackground(StyledElement host, ChangeKind kind) => kind switch
    {
        ChangeKind.Inserted => Tinted(host, "MethodPostBrush", 0.55),
        ChangeKind.Deleted => Tinted(host, "MethodDeleteBrush", 0.55),
        _ => null,
    };

    /// <summary>
    /// Extra wash over the hunk the user is currently on, painted on top of its change tint.
    ///
    /// Kept low: it stacks with a tint that is already there, and the point is to make one difference
    /// findable among many, not to obscure the text inside it. The accent bar and outline below do
    /// most of the work - colour alone is too weak a signal to locate a block by.
    /// </summary>
    public static IBrush? CurrentHunkWash(StyledElement host) => Tinted(host, "PostmanOrangeBrush", 0.10);

    /// <summary>The bar down the edge of the current hunk, and the hairline boxing it in.</summary>
    public static IBrush? CurrentHunkAccent(StyledElement host) => Tinted(host, "PostmanOrangeBrush", 0.95);

    public static IBrush? CurrentHunkOutline(StyledElement host) => Tinted(host, "PostmanOrangeBrush", 0.50);

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
