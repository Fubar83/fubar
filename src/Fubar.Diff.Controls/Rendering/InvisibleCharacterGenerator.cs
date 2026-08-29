using AvaloniaEdit.Rendering;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// Draws a visible marker in place of an invisible character - <c>NBSP</c> where a non-breaking space
/// is, <c>ZWSP</c> where a zero-width one is, and so on for the rest of
/// <see cref="InvisibleCharacters"/>.
///
/// A generator rather than a colourizer, and that is the whole point: a colourizer can only tint a
/// region, and these characters have no region to tint - most are literally zero pixels wide, and the
/// rest are blank. Substituting something with width is the only way to show that anything is there.
///
/// Replaces exactly ONE document character with a wider run. That is safe because
/// <c>FormattedTextElement</c> is told its DOCUMENT length is 1: the visual line gets wider, while
/// every offset the diff renderers and the merge model use stays exactly where it was.
/// </summary>
internal sealed class InvisibleCharacterGenerator : VisualLineElementGenerator
{
    private bool _enabled;

    /// <summary>Turns revealing on or off. The caller must redraw the text view.</summary>
    public void SetEnabled(bool value) => _enabled = value;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!_enabled)
        {
            return -1;
        }

        var document = CurrentContext.Document;
        var end = CurrentContext.VisualLine.LastDocumentLine.EndOffset;

        for (var offset = startOffset; offset < end; offset++)
        {
            // The ASCII pre-check first: every character in the set is non-ASCII, so an ordinary line
            // of code costs one comparison each and never reaches the switch.
            var c = document.GetCharAt(offset);
            if (c > '\u007F' && InvisibleCharacters.MarkerFor(c) is not null)
            {
                return offset;
            }
        }

        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var c = CurrentContext.Document.GetCharAt(offset);

        return InvisibleCharacters.MarkerFor(c) is { } marker
            ? new FormattedTextElement(marker, 1)
            : null;
    }
}
