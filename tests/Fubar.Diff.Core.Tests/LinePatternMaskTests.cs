using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Masking the text a user has said not to care about.
///
/// Two things carry real risk here and get most of the attention: these are USER-SUPPLIED regular
/// expressions, so one of them will eventually be malformed and one of them will eventually be
/// pathological, and neither may take the application down or hang it.
/// </summary>
public class LinePatternMaskTests
{
    private static string Mask(string line, params string[] patterns) =>
        LinePatternMask.Create(patterns)!.Apply(line);

    [Fact]
    public void A_matching_run_stops_counting_as_a_difference()
    {
        // The case this exists for: the same log line, generated twice.
        const string pattern = @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}";

        Assert.Equal(
            Mask("2024-01-01T09:00:00 starting up", pattern),
            Mask("2025-06-30T17:45:12 starting up", pattern));
    }

    [Fact]
    public void A_real_change_elsewhere_on_the_same_line_still_counts()
    {
        // Why this masks the MATCH rather than dropping the line: ignoring the whole line would hide
        // the message change too, which is the thing the reader actually wanted.
        const string pattern = @"\d{4}-\d{2}-\d{2}";

        Assert.NotEqual(
            Mask("2024-01-01 starting up", pattern),
            Mask("2025-06-30 shutting down", pattern));
    }

    [Fact]
    public void Masking_does_not_make_a_shorter_line_equal_to_a_longer_one()
    {
        // A match is replaced by a marker, not by nothing. Blanking to empty would make "ab" and "a"
        // compare equal under the rule "b" - a difference the user never asked to hide.
        Assert.NotEqual(Mask("ab", "b"), Mask("a", "b"));
    }

    [Fact]
    public void Several_rules_all_apply()
    {
        var masked = Mask("build 412 at 2024-01-01", @"\d{4}-\d{2}-\d{2}", @"build \d+");

        Assert.Equal(masked, Mask("build 9 at 2030-12-25", @"\d{4}-\d{2}-\d{2}", @"build \d+"));
    }

    [Fact]
    public void A_line_nothing_matches_comes_back_as_itself()
    {
        Assert.Equal("untouched", Mask("untouched", @"\d+"));
    }

    [Fact]
    public void No_patterns_means_no_mask()
    {
        // The caller's fast path is a null check rather than a loop over an empty array per line.
        Assert.Null(LinePatternMask.Create([]));
        Assert.Null(LinePatternMask.Create(["", "   "]));
    }

    [Fact]
    public void A_malformed_pattern_is_rejected_rather_than_thrown()
    {
        // These come from a settings file a user can hand-edit. Refusing to compare anything because
        // one rule has a stray bracket is not an acceptable answer.
        var mask = LinePatternMask.Create(["([unclosed", @"\d+"], out var rejected);

        Assert.NotNull(mask);
        Assert.Equal(["([unclosed"], rejected);
        Assert.Equal(Mask("a1", @"\d+"), mask!.Apply("a1"));
    }

    [Fact]
    public void A_set_of_only_malformed_patterns_masks_nothing()
    {
        Assert.Null(LinePatternMask.Create(["([unclosed", "*bad"], out var rejected));
        Assert.Equal(2, rejected.Count);
    }

    [Fact]
    public void A_pattern_that_could_backtrack_catastrophically_still_returns()
    {
        // (a+)+$ against a long non-matching run is the textbook exponential case. The non-backtracking
        // engine makes it linear; if it ever falls back, the timeout bounds it. Either way this must
        // finish, because a diff tool that hangs on a regex someone typed is unusable.
        var mask = LinePatternMask.Create(["(a+)+$"]);

        Assert.NotNull(mask);

        var result = mask!.Apply(new string('a', 2000) + "!");

        Assert.NotNull(result);
    }

    [Fact]
    public void A_pattern_using_lookaround_still_works()
    {
        // Lookaround is not supported by the linear-time engine, so this exercises the fallback path.
        var mask = LinePatternMask.Create([@"(?<=v)\d+"]);

        Assert.NotNull(mask);
        Assert.Equal(mask!.Apply("v1 release"), mask.Apply("v2 release"));
    }

    [Fact]
    public void A_backreference_pattern_still_works()
    {
        var mask = LinePatternMask.Create([@"(\w)\1"]);

        Assert.NotNull(mask);
        Assert.Equal("a", mask!.Apply("abb").Replace("", ""));
    }

    [Fact]
    public void Matching_is_not_affected_by_the_machine_locale()
    {
        // The same rule the normalizer follows: a diff must not change its answer because of where it
        // is running.
        var mask = LinePatternMask.Create(["[A-Z]+"]);

        Assert.Equal(mask!.Apply("ABC"), mask.Apply("XYZ"));
    }
}
