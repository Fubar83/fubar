using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>Severity of a <see cref="Banner"/> - drives its colour and (optional) default icon.</summary>
public enum BannerSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// An inline message strip - info/success/warning/error - with an optional leading <see cref="Icon"/>
/// glyph, a wrapping <see cref="Message"/>, and an optional trailing <see cref="Action"/> slot (e.g. a
/// "Retry"/"Dismiss" button). Colour comes from the palette's status tokens via <see cref="Severity"/>.
/// Use it for validation errors, "unsaved changes" notices, empty-with-reason states, etc.
/// </summary>
public class Banner : TemplatedControl
{
    public static readonly StyledProperty<BannerSeverity> SeverityProperty =
        AvaloniaProperty.Register<Banner, BannerSeverity>(nameof(Severity), BannerSeverity.Info);

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<Banner, string?>(nameof(Message));

    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<Banner, string?>(nameof(Icon));

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<Banner, object?>(nameof(Action));

    public BannerSeverity Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Optional leading glyph. If empty, no icon is shown.</summary>
    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Optional trailing content, usually an action button.</summary>
    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Banner);
}
