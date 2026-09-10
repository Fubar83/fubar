using System;
using System.Collections.Generic;

namespace Fubar.Studio.UI.Services;

/// <summary>Where a query's characters landed in a candidate, and how well.</summary>
/// <param name="Score">Higher is better. Only comparable between candidates for the SAME query.</param>
/// <param name="Positions">Indexes into the candidate, ascending - what the palette draws in bold.</param>
public readonly record struct FuzzyMatchResult(int Score, IReadOnlyList<int> Positions);

/// <summary>
/// Subsequence matching with a score and the positions it matched at, for the command palette.
///
/// <para>Subsequence rather than substring because that is what people expect from a palette: "nr"
/// finds "New Request", "ordcr" finds "Orders / create", and "hj" finds "Henrik Johansson" by its
/// initials. Pure and static so the ranking can be tested without a window - the ranking is the whole
/// feature, since a palette that finds the right entry and puts it fourth is a palette nobody uses
/// twice.</para>
///
/// <para><b>Best alignment, not the first one.</b> This used to walk the candidate greedily, taking
/// the earliest position for each query character, which finds A match whenever one exists but
/// routinely the wrong one: "posh" against "Copy as cURL (PowerShell)" took the <c>p</c> of "Copy",
/// and then scored the result as the scattered thing it had just chosen to make. Every alignment is
/// considered now and the best-scoring one wins, which is also what makes the highlight honest - the
/// characters drawn in bold are the ones the score was earned on.</para>
/// </summary>
public static class FuzzyMatch
{
    // Per matched character. Everything else is expressed relative to this.
    private const int MatchBonus = 16;

    /// <summary>Adjacent to the previous match. The strongest signal there is: a run of characters the
    /// user typed as one word.</summary>
    private const int ConsecutiveBonus = 18;

    /// <summary>The first letter of a word - which is what an initials query like "hj" is made of, and
    /// what someone typing "r" for "Request" is aiming at rather than the r in "Environment".</summary>
    private const int WordStartBonus = 14;

    /// <summary>Skipping over characters costs, so a tight match beats a sprawling one. Capped, or a
    /// long title would be unmatchable on its last word.</summary>
    private const int GapPenalty = 2;
    private const int MaxGapPenalty = 12;

    /// <summary>
    /// How well <paramref name="candidate"/> matches <paramref name="query"/>, and where.
    /// Null means it does not match at all.
    ///
    /// <para>An empty query matches everything with a flat score and no positions, so the palette opens
    /// showing the full list in its natural order rather than an arbitrary one.</para>
    /// </summary>
    public static FuzzyMatchResult? Match(string? candidate, string? query)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return null;
        }

        var wanted = Compact(query);
        if (wanted.Count == 0)
        {
            return new FuzzyMatchResult(0, []);
        }

        if (wanted.Count > candidate.Length)
        {
            return null;
        }

        // best[q, c] - the best score for matching wanted[q..] within candidate[c..], with `previous`
        // saying whether candidate[c-1] was itself matched (which is what makes a match at c
        // consecutive). Two tables rather than one, because the same (q, c) is worth different amounts
        // depending on that, and collapsing them loses exactly the run-of-characters case the score
        // exists to reward.
        var width = candidate.Length + 1;
        var after = new int?[2, (wanted.Count + 1) * width];
        var chosen = new int[2, (wanted.Count + 1) * width];

        var score = Best(candidate, wanted, 0, 0, false, after, chosen, width);
        if (score is null)
        {
            return null;
        }

        // Walk the choices back out to say WHERE it matched.
        var positions = new List<int>(wanted.Count);
        var previousMatched = false;
        for (int q = 0, c = 0; q < wanted.Count; q++)
        {
            var at = chosen[previousMatched ? 1 : 0, (q * width) + c];
            positions.Add(at);
            previousMatched = true;
            c = at + 1;
        }

        return new FuzzyMatchResult(score.Value, positions);
    }

    /// <summary>
    /// The best score for the rest of the query, and the position the next character was matched at.
    /// </summary>
    /// <remarks>
    /// Recursion with a memo rather than a loop: what makes this readable is that every alignment is
    /// "match here, then solve the rest", and the memo is what keeps it linear enough - palette titles
    /// are short and there are tens of them, so this runs on every keystroke without being felt.
    /// </remarks>
    private static int? Best(
        string candidate,
        List<char> wanted,
        int q,
        int c,
        bool previousMatched,
        int?[,] memo,
        int[,] chosen,
        int width)
    {
        if (q == wanted.Count)
        {
            return 0;
        }

        var slot = previousMatched ? 1 : 0;
        var key = (q * width) + c;

        if (memo[slot, key] is { } cached)
        {
            return cached == int.MinValue ? null : cached;
        }

        int? best = null;
        var bestAt = -1;

        // Not enough candidate left to place every remaining query character.
        for (var i = c; i <= candidate.Length - (wanted.Count - q); i++)
        {
            if (char.ToLowerInvariant(candidate[i]) != wanted[q])
            {
                continue;
            }

            var rest = Best(candidate, wanted, q + 1, i + 1, true, memo, chosen, width);
            if (rest is null)
            {
                continue;
            }

            var here = MatchBonus + rest.Value;

            if (previousMatched && i == c)
            {
                here += ConsecutiveBonus;
            }
            else if (IsWordStart(candidate, i))
            {
                here += WordStartBonus;
            }

            here -= Math.Min(MaxGapPenalty, (i - c) * GapPenalty);

            if (best is null || here > best)
            {
                best = here;
                bestAt = i;
            }
        }

        memo[slot, key] = best ?? int.MinValue;
        chosen[slot, key] = bestAt;

        return best;
    }

    /// <summary>
    /// The first letter of a word.
    /// </summary>
    /// <remarks>
    /// Separators and camelCase both count, so "New Request", "get-order", "orders/create" and
    /// "BaseUrl" all give up their initials to a two-letter query.
    /// </remarks>
    private static bool IsWordStart(string candidate, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var previous = candidate[index - 1];

        return previous is ' ' or '/' or '\\' or '-' or '_' or '.' or '(' or '[' or ':' or '#' or '@' or ','
            || (char.IsLower(previous) && char.IsUpper(candidate[index]));
    }

    /// <summary>The query as lower-case characters, with whitespace dropped: someone typing "new req"
    /// means the same as "newreq", and a trailing space must not stop everything matching.</summary>
    private static List<char> Compact(string? query)
    {
        var result = new List<char>();
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (var c in query)
        {
            if (!char.IsWhiteSpace(c))
            {
                result.Add(char.ToLowerInvariant(c));
            }
        }

        return result;
    }

    /// <summary>How well it matches, without asking where. Null means it does not.</summary>
    public static int? Score(string? candidate, string? query) => Match(candidate, query)?.Score;

    /// <summary>True when the candidate matches at all.</summary>
    public static bool Matches(string? candidate, string? query) => Match(candidate, query) is not null;
}
