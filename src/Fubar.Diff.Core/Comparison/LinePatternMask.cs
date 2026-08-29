using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// Blanks out the parts of a line the user has said not to care about, before anything compares it.
///
/// The case this exists for: a file that regenerates a timestamp, a build number or a GUID on every
/// run, so every comparison of two otherwise-identical outputs is buried under differences nobody can
/// act on. A rule like <c>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}</c> makes those lines compare equal
/// while leaving every other difference on that line intact - which is what separates this from
/// ignoring the whole line, and why it is a substitution rather than a filter.
///
/// It produces comparison KEYS and nothing else. The panes always show the user their own text,
/// timestamp included; what changes is only whether the row counts as a difference.
/// </summary>
public sealed class LinePatternMask
{
    /// <summary>
    /// What a match is replaced with. A character that cannot occur in real source text, rather than
    /// an empty string: blanking a match to nothing would make <c>ab</c> and <c>a</c> compare equal
    /// under the rule <c>b</c>, which is a difference the user did not ask to hide. A marker keeps the
    /// SHAPE of the line, so only the masked text stops mattering.
    /// </summary>
    private const string Marker = "\u0001";

    /// <summary>
    /// How long any single line may spend being matched. A user-supplied pattern can backtrack
    /// catastrophically, and a diff tool that hangs on a regex someone typed is worse than one that
    /// declines the rule - so the pattern that cannot keep up is simply skipped for that line.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Regex[] _patterns;

    private LinePatternMask(Regex[] patterns) => _patterns = patterns;

    /// <summary>
    /// Compiles the rules once, or returns null when there is nothing usable to apply - so the caller's
    /// fast path is a null check rather than a loop over an empty array per line.
    ///
    /// An unparseable pattern is dropped rather than thrown: these come from a settings file a user can
    /// hand-edit, and refusing to compare anything because one rule has a stray bracket is not an
    /// acceptable answer. <paramref name="rejected"/> reports which, so the UI can say so.
    /// </summary>
    public static LinePatternMask? Create(IReadOnlyList<string> patterns, out IReadOnlyList<string> rejected)
    {
        var compiled = new List<Regex>(patterns.Count);
        var bad = new List<string>();

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (TryCompile(pattern) is { } regex)
            {
                compiled.Add(regex);
            }
            else
            {
                bad.Add(pattern);
            }
        }

        rejected = bad;

        return compiled.Count == 0 ? null : new LinePatternMask([.. compiled]);
    }

    /// <summary>Convenience for callers with nothing to report a rejection to.</summary>
    public static LinePatternMask? Create(IReadOnlyList<string> patterns) => Create(patterns, out _);

    /// <summary>
    /// Prefers the non-backtracking engine, which is linear in the length of the input and therefore
    /// cannot be made to hang by a pattern like <c>(a+)+$</c>. It does not support backreferences or
    /// lookaround, so a pattern using those falls back to the ordinary engine with a timeout - slower
    /// to fail, but still bounded.
    /// </summary>
    private static Regex? TryCompile(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
        }
        catch (NotSupportedException)
        {
            // Backreferences or lookaround: legitimate, just not linear-time.
        }
        catch (ArgumentException)
        {
            return null;
        }

        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The line with every match replaced by the marker, or the line itself when nothing matched -
    /// which is the common case, and worth not allocating for.
    /// </summary>
    public string Apply(string line)
    {
        var masked = line;

        foreach (var pattern in _patterns)
        {
            try
            {
                masked = pattern.Replace(masked, Marker);
            }
            catch (RegexMatchTimeoutException)
            {
                // This rule is too slow for this line. Leaving the line unmasked reports a difference
                // the user asked to hide, which is a far better failure than freezing the window.
            }
        }

        return masked;
    }
}
