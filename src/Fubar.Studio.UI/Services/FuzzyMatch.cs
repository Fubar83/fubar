using System;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Subsequence matching with a score, for the command palette.
///
/// <para>Subsequence rather than substring because that is what people expect from a palette: "nr"
/// should find "New Request", and "ordcr" should find "Orders / create". Pure and static so the
/// ranking can be tested without a window - the ranking is the whole feature, since a palette that
/// finds the right entry and puts it fourth is a palette nobody uses twice.</para>
/// </summary>
public static class FuzzyMatch
{
    /// <summary>
    /// How well <paramref name="candidate"/> matches <paramref name="query"/>: higher is better, and
    /// null means it does not match at all.
    ///
    /// <para>An empty query matches everything with a flat score, so the palette opens showing the
    /// full list in its natural order rather than an arbitrary one.</para>
    /// </summary>
    public static int? Score(string? candidate, string? query)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var score = 0;
        var candidateIndex = 0;
        var previousMatch = -1;

        foreach (var wanted in query)
        {
            if (char.IsWhiteSpace(wanted))
            {
                continue;
            }

            var found = -1;
            for (var i = candidateIndex; i < candidate.Length; i++)
            {
                if (char.ToLowerInvariant(candidate[i]) == char.ToLowerInvariant(wanted))
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
            {
                return null;
            }

            // Consecutive characters score far above scattered ones, so "orders" beats a candidate
            // that merely contains those six letters spread across a sentence.
            score += previousMatch >= 0 && found == previousMatch + 1 ? 12 : 1;

            // A match at a word boundary is what the user was aiming at - the "R" of "Request" rather
            // than the "r" in "Environment".
            if (found == 0 || candidate[found - 1] is ' ' or '/' or '-' or '_' or '.')
            {
                score += 8;
            }

            // Earlier is better, gently: enough to break ties, not enough to beat a run of consecutive
            // characters further along.
            score += Math.Max(0, 4 - found / 8);

            previousMatch = found;
            candidateIndex = found + 1;
        }

        return score;
    }

    /// <summary>True when the candidate matches at all.</summary>
    public static bool Matches(string? candidate, string? query) => Score(candidate, query) is not null;
}
