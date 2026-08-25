using System;
using Avalonia;
using Avalonia.Controls;

namespace Fubar.Controls;

/// <summary>
/// A small rounded "pill" label - method tags (GET/POST), counts, status words, etc. Holds any
/// content (usually a short string) and is coloured purely through <see cref="Avalonia.Controls.Primitives.TemplatedControl"/>'s
/// own <c>Background</c>/<c>Foreground</c>/<c>BorderBrush</c>, so a host tints it by binding those
/// (e.g. to a MethodGetBrush token) rather than needing a per-variant subclass.
/// </summary>
public class Badge : ContentControl
{
    protected override Type StyleKeyOverride => typeof(Badge);
}
