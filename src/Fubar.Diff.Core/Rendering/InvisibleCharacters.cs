namespace Fubar.Diff.Core.Rendering;

/// <summary>
/// Characters that are invisible, or indistinguishable from an ordinary space, and therefore make a
/// diff look wrong: the two lines are plainly different to the differ and plainly identical to the
/// eye. Revealing them is the only way to close that gap.
///
/// The set is deliberately narrow. Curly quotes and en/em dashes are confusable with their ASCII
/// cousins but are VISIBLY different once you look, and they occur legitimately in prose all the time -
/// flagging them would cry wolf on ordinary text. Everything listed here is either zero-width or
/// renders exactly like U+0020, so seeing it is never noise.
///
/// The bidi controls are included for a second reason: a run of them can make source code read in one
/// order and compile in another ("Trojan Source"), which is precisely the kind of thing a diff should
/// be able to show you.
///
/// Written with \u escapes rather than the characters themselves on purpose - a file full of literal
/// zero-width characters is unreadable, unreviewable, and liable to be silently mangled by any tool
/// that touches it on the way past.
/// </summary>
public static class InvisibleCharacters
{
    /// <summary>
    /// A short visible stand-in for <paramref name="c"/>, or null when it needs no revealing.
    ///
    /// Returns a marker rather than a bool so the caller has something to draw: these characters are
    /// zero-width or blank, so highlighting them in place would paint a region with nothing in it.
    /// </summary>
    public static string? MarkerFor(char c) => c switch
    {
        // Zero-width. U+FEFF is a byte order mark appearing INSIDE the text rather than at the start,
        // where the reader would have consumed it as a preamble.
        '\u200B' => "ZWSP",
        '\u200C' => "ZWNJ",
        '\u200D' => "ZWJ",
        '\uFEFF' => "BOM",
        '\u00AD' => "SHY",

        // Bidirectional controls - invisible, and able to reorder how a line reads.
        '\u200E' => "LRM",
        '\u200F' => "RLM",
        '\u202A' => "LRE",
        '\u202B' => "RLE",
        '\u202C' => "PDF",
        '\u202D' => "LRO",
        '\u202E' => "RLO",
        '\u2066' => "LRI",
        '\u2067' => "RLI",
        '\u2068' => "FSI",
        '\u2069' => "PDI",

        // Spaces that are not U+0020. NBSP is by far the most common - pasted from a browser or a
        // word processor, it breaks parsers while looking exactly like the space beside it.
        '\u00A0' => "NBSP",
        '\u2007' => "FIGSP",
        '\u202F' => "NNBSP",
        '\u205F' => "MMSP",
        '\u3000' => "IDSP",
        >= '\u2000' and <= '\u200A' => "SP",

        _ => null,
    };

    /// <summary>Whether a line contains anything worth revealing - a cheap pre-check before scanning.</summary>
    public static bool ContainsAny(string text)
    {
        foreach (var c in text)
        {
            // Everything in the set is non-ASCII, so the overwhelmingly common all-ASCII line costs
            // one comparison per character and no switch dispatch at all.
            if (c > '\u007F' && MarkerFor(c) is not null)
            {
                return true;
            }
        }

        return false;
    }
}
