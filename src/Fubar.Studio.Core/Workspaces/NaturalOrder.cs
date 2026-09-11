namespace Fubar.Studio.Core.Workspaces;

/// <summary>
/// Sorts names the way a person reading a numbered list expects: <c>request-2</c> before
/// <c>request-10</c>.
/// </summary>
/// <remarks>
/// <para>Ordinary string ordering compares <c>1</c> against <c>2</c> character by character and puts
/// <c>request-10</c> second, so a batch of eleven items runs 1, 10, 11, 2, 3 - an order nobody wrote
/// and which reads as a bug in the runner rather than in the sort.</para>
/// <para>Digits are compared as NUMBERS wherever they appear, not just at the end, so
/// <c>v2/step-3</c> sorts the way both of its numbers say. Everything else is compared
/// case-insensitively first - a list holding <c>Step-2</c> and <c>step-10</c> is one list to the
/// person who wrote it.</para>
/// <para>A total order, and a stable one: names that differ only in leading zeros or in case
/// (<c>step-01</c> against <c>step-1</c>) compare equal on the natural pass, so the raw string decides
/// between them. Without that last tiebreak two files could each claim to come first, and the order
/// would depend on the order the directory happened to be read in.</para>
/// </remarks>
public sealed class NaturalOrder : IComparer<string>
{
    public static NaturalOrder Instance { get; } = new();

    private NaturalOrder()
    {
    }

    /// <summary>The names in the order they should run.</summary>
    public static IEnumerable<T> Sort<T>(IEnumerable<T> items, Func<T, string> name)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(name);

        return items.OrderBy(name, Instance);
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var i = 0;
        var j = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var byNumber = CompareNumbers(x, ref i, y, ref j);
                if (byNumber != 0)
                {
                    return byNumber;
                }

                continue;
            }

            var byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        // One ran out: the shorter is the prefix of the other and comes first. Equal here means the two
        // differ only in case or in leading zeros, so the raw string breaks the tie rather than leaving
        // the order down to whatever the file system returned first.
        var byLength = (x.Length - i).CompareTo(y.Length - j);
        return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// Compares the runs of digits starting at each position, leaving both indices just past them.
    /// </summary>
    /// <remarks>
    /// By LENGTH of the significant part, then digit by digit - never by parsing. A file may be named
    /// with more digits than any integer type holds, and a sort that threw on someone's file name
    /// would take the whole tree down with it.
    /// </remarks>
    private static int CompareNumbers(string x, ref int i, string y, ref int j)
    {
        var startX = i;
        var startY = j;

        while (i < x.Length && char.IsAsciiDigit(x[i]))
        {
            i++;
        }

        while (j < y.Length && char.IsAsciiDigit(y[j]))
        {
            j++;
        }

        // Leading zeros change the length without changing the value, so they are skipped before the
        // lengths are compared at all.
        var digitsX = Significant(x, startX, i);
        var digitsY = Significant(y, startY, j);

        if (digitsX.Length != digitsY.Length)
        {
            return digitsX.Length.CompareTo(digitsY.Length);
        }

        return digitsX.SequenceCompareTo(digitsY);
    }

    private static ReadOnlySpan<char> Significant(string text, int start, int end)
    {
        while (start < end - 1 && text[start] == '0')
        {
            start++;
        }

        return text.AsSpan(start, end - start);
    }
}
