using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>Three-way state a <see cref="ValidityIcon"/> can display.</summary>
public enum ValidityState
{
    /// <summary>Nothing to report yet (neutral, muted dot).</summary>
    Unknown,

    /// <summary>Valid / passing (green check).</summary>
    Valid,

    /// <summary>Invalid / failing (red cross).</summary>
    Invalid
}

/// <summary>
/// A small glyph that reflects a validation outcome - e.g. next to a JSON body it shows a green check
/// when the text parses and a red cross when it doesn't. Driven entirely by <see cref="State"/>; the
/// glyph and colour for each state are supplied by the control theme via property selectors.
/// </summary>
public class ValidityIcon : TemplatedControl
{
    public static readonly StyledProperty<ValidityState> StateProperty =
        AvaloniaProperty.Register<ValidityIcon, ValidityState>(nameof(State), ValidityState.Unknown);

    public ValidityState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ValidityIcon);
}
