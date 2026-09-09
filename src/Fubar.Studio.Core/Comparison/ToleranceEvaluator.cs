using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fubar.Studio.Core.Json;

namespace Fubar.Studio.Core.Comparison;

/// <param name="Outcome">What is left to report once every forgiven difference is out.</param>
/// <param name="Tolerated">How many were forgiven. Shown, not swallowed: a step that passed only
/// because six fields were within tolerance is a different fact from one that matched.</param>
/// <param name="Warnings">Rules that could not be applied at all. A tolerance that silently does
/// nothing is worse than no tolerance, because it reads as a check.</param>
public sealed record ToleratedOutcome(
    ComparisonOutcome Outcome,
    int Tolerated,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Applies tolerances to what a comparison found.
/// </summary>
/// <remarks>
/// <para>After the comparer rather than inside it, which is what keeps <see cref="IResponseComparer"/>
/// free of them: the engine's job is to say what differs, and this says which of those differences
/// were allowed. The spec's evaluation order (redact -> normalise -> tolerance -> ignore) puts
/// tolerance before ignore; the engine has already marked ignored differences by the time this runs,
/// and the two orders can only disagree about a field that is both ignored AND tolerated, which is
/// not reported either way.</para>
/// <para>Tolerances need paths, so they only apply to a SEMANTIC comparison. A textual one has no
/// fields to name, and pretending otherwise would forgive whole hunks by accident - so the rules are
/// reported as unapplied instead.</para>
/// </remarks>
public static class ToleranceEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static ToleratedOutcome Apply(
        ComparisonOutcome outcome,
        IReadOnlyList<ResolvedTolerance> tolerances,
        string? left = null,
        string? right = null)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(tolerances);

        if (tolerances.Count == 0 || outcome.Same)
        {
            return new ToleratedOutcome(outcome, 0, []);
        }

        var warnings = new List<string>();

        foreach (var resolved in tolerances.Where(t => t.Tolerance.Kind == ToleranceKind.None))
        {
            warnings.Add(
                $"Tolerance for \"{resolved.Tolerance.Path}\" states no allowance, or several, and was not applied");
        }

        if (!outcome.IsSemantic)
        {
            warnings.Add(
                "The responses were compared as text, so no tolerance applied - a tolerance names a field, "
                + "and there are no fields in a text comparison.");

            return new ToleratedOutcome(outcome, 0, warnings);
        }

        var usable = tolerances.Where(t => t.Tolerance.Kind != ToleranceKind.None).ToList();
        if (usable.Count == 0)
        {
            return new ToleratedOutcome(outcome, 0, warnings);
        }

        var arrays = new ArrayLengths(left, right);
        var kept = new List<ResponseDifference>(outcome.Differences.Count);

        foreach (var difference in outcome.Differences)
        {
            if (!IsTolerated(difference, usable, arrays))
            {
                kept.Add(difference);
            }
        }

        var tolerated = outcome.Differences.Count - kept.Count;
        if (tolerated == 0)
        {
            return new ToleratedOutcome(outcome, 0, warnings);
        }

        return new ToleratedOutcome(
            outcome with { DifferenceCount = kept.Count, Differences = kept },
            tolerated,
            warnings);
    }

    private static bool IsTolerated(
        ResponseDifference difference,
        IReadOnlyList<ResolvedTolerance> tolerances,
        ArrayLengths arrays)
    {
        // Closest level last, so the last matching rule is the one that level meant to apply.
        for (var i = tolerances.Count - 1; i >= 0; i--)
        {
            var tolerance = tolerances[i].Tolerance;

            if (tolerance.Kind == ToleranceKind.LengthWithinPercent)
            {
                if (ForgivesLength(tolerance, difference, arrays))
                {
                    return true;
                }

                continue;
            }

            if (JsonPathMatcher.Matches(tolerance.Path, difference.Path)
                && ForgivesValue(tolerance, difference.Left, difference.Right))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// An array whose length moved: the difference is reported at the ELEMENT (<c>$.orders[7]</c>),
    /// so the rule names the array and this asks whether the element's parent is that array.
    /// </summary>
    /// <remarks>
    /// Only elements that appeared or went away. An element whose CONTENT changed is not a length
    /// change, and forgiving it would turn "this list may be a little longer" into "nothing inside
    /// this list is checked".
    /// </remarks>
    private static bool ForgivesLength(Tolerance tolerance, ResponseDifference difference, ArrayLengths arrays)
    {
        if (difference.Kind is not (ResponseDifferenceKind.Added or ResponseDifferenceKind.Removed))
        {
            return false;
        }

        if (JsonPathMatcher.ParentOf(difference.Path) is not { } parent
            || !JsonPathMatcher.Matches(tolerance.Path, parent))
        {
            return false;
        }

        // Without both bodies there is no length to measure, and guessing would forgive a list that
        // emptied itself.
        if (!arrays.TryGet(parent, out var leftLength, out var rightLength))
        {
            return false;
        }

        var larger = Math.Max(leftLength, rightLength);

        return larger == 0
            || (Math.Abs(leftLength - rightLength) * 100.0 / larger) <= tolerance.LengthWithinPercent!.Value;
    }

    private static bool ForgivesValue(Tolerance tolerance, string? left, string? right) => tolerance.Kind switch
    {
        ToleranceKind.Numeric =>
            TryNumber(left, out var l) && TryNumber(right, out var r)
            && Math.Abs(l - r) <= tolerance.Numeric!.Value,

        ToleranceKind.WithinSeconds =>
            TryTime(left, out var lt) && TryTime(right, out var rt)
            && Math.Abs((lt - rt).TotalSeconds) <= tolerance.WithinSeconds!.Value,

        // BOTH sides, so a generated id is forgiven and a missing one is not.
        ToleranceKind.Matches =>
            left is not null && right is not null && MatchesBoth(tolerance.Matches!, left, right),

        ToleranceKind.OneOf =>
            left is not null && right is not null
            && tolerance.OneOf!.Contains(left, StringComparer.Ordinal)
            && tolerance.OneOf!.Contains(right, StringComparer.Ordinal),

        _ => false,
    };

    private static bool MatchesBoth(string pattern, string left, string right)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, RegexTimeout);
            return regex.IsMatch(left) && regex.IsMatch(right);
        }
        catch (ArgumentException)
        {
            // An unparseable expression forgives nothing - the difference is reported, which is the
            // safe direction for a rule nobody can read.
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool TryNumber(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryTime(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
            out value);

    /// <summary>Array lengths on both sides, parsed once and only if something asks.</summary>
    private sealed class ArrayLengths(string? left, string? right)
    {
        private JsonNode? _left;
        private JsonNode? _right;
        private bool _parsed;

        public bool TryGet(string path, out int leftLength, out int rightLength)
        {
            leftLength = 0;
            rightLength = 0;

            if (!_parsed)
            {
                _parsed = true;
                _left = Parse(left);
                _right = Parse(right);
            }

            return _left is not null
                && _right is not null
                && Length(_left, path, out leftLength)
                && Length(_right, path, out rightLength);
        }

        private static JsonNode? Parse(string? text)
        {
            try
            {
                return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>Walks a CONCRETE path - the one a difference reported, so it has no wildcards.</summary>
        private static bool Length(JsonNode root, string path, out int length)
        {
            length = 0;
            var node = root;

            foreach (var (name, index) in Steps(path))
            {
                node = index >= 0
                    ? (node as JsonArray)?[index]
                    : (node as JsonObject)?[name!];

                if (node is null)
                {
                    return false;
                }
            }

            if (node is not JsonArray array)
            {
                return false;
            }

            length = array.Count;
            return true;
        }

        private static IEnumerable<(string? Name, int Index)> Steps(string path)
        {
            var i = 1;
            while (i < path.Length)
            {
                if (path[i] == '.')
                {
                    i++;
                    var start = i;
                    while (i < path.Length && path[i] is not ('.' or '['))
                    {
                        i++;
                    }

                    yield return (path[start..i], -1);
                    continue;
                }

                var close = path.IndexOf(']', i);
                if (close < 0)
                {
                    yield break;
                }

                var inner = path[(i + 1)..close];
                i = close + 1;

                yield return int.TryParse(inner, out var index)
                    ? (null, index)
                    : (inner.Trim('\''), -1);
            }
        }
    }
}
