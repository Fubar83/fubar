using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.UI.Rendering;

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
    /// Stronger tint for the characters that actually changed within a modified line. It sits ON TOP
    /// of the line tint, so it must be noticeably denser or it simply disappears into it.
    /// </summary>
    public static IBrush? SpanBackground(StyledElement host, ChangeKind kind) => kind switch
    {
        ChangeKind.Inserted => Tinted(host, "MethodPostBrush", 0.42),
        ChangeKind.Deleted => Tinted(host, "MethodDeleteBrush", 0.42),
        _ => null,
    };

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
