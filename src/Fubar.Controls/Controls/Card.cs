using System;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>
/// A bordered surface panel with an optional header. <c>Header</c> (any content, usually a string or a
/// <see cref="SectionHeader"/>) renders in a top strip separated from the body by a <see cref="Divider"/>;
/// when it is null the strip collapses and only the body <c>Content</c> shows.
/// </summary>
public class Card : HeaderedContentControl
{
    protected override Type StyleKeyOverride => typeof(Card);
}
