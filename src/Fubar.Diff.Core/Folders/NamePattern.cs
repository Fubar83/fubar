using System;
using System.Collections.Generic;

namespace Fubar.Diff.Core.Folders;

/// <summary>
/// Matches an entry NAME against a shell-style pattern: <c>*</c> for any run of characters, <c>?</c>
/// for exactly one, anything else literal.
///
/// Deliberately not a regular expression and deliberately not a path glob. Exclusions here are things
/// like <c>bin</c>, <c>*.dll</c> and <c>.git</c> - names, typed quickly, by people who are not thinking
/// about escaping. A regex would make <c>.</c> mean "any character", so <c>.git</c> would quietly also
/// exclude <c>agit</c>; a path glob would invite <c>**/</c> syntax this does not implement. The
/// separate <c>LinePatternMask</c> is where real regular expressions belong, and it is opt-in.
/// </summary>
public static class NamePattern
{
    /// <summary>Whether any of the patterns matches the name.</summary>
    public static bool MatchesAny(string name, IReadOnlyList<string> patterns, bool ignoreCase)
    {
        foreach (var pattern in patterns)
        {
            if (Matches(name, pattern, ignoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether one pattern matches the name.
    ///
    /// Iterative with a backtrack point rather than recursive: the only construct needing backtracking
    /// is <c>*</c>, and remembering the last one is enough to match it in linear time. A recursive
    /// version is shorter and takes exponential time on a name full of stars, which is a poor trade for
    /// input a user can type.
    /// </summary>
    public static bool Matches(string name, string pattern, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var n = 0;
        var p = 0;
        var starAt = -1;
        var matchAt = 0;

        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || Same(name[n], pattern[p], comparison)))
            {
                n++;
                p++;
                continue;
            }

            if (p < pattern.Length && pattern[p] == '*')
            {
                // Remember where to resume if this star turns out to need to swallow more.
                starAt = p++;
                matchAt = n;
                continue;
            }

            if (starAt >= 0)
            {
                p = starAt + 1;
                n = ++matchAt;
                continue;
            }

            return false;
        }

        // Trailing stars can match nothing at all.
        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool Same(char a, char b, StringComparison comparison) =>
        comparison == StringComparison.Ordinal
            ? a == b
            : char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
