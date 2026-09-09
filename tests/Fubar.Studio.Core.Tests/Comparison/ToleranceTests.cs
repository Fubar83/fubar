using Fubar.Studio.Core.Comparison;

namespace Fubar.Studio.Core.Tests.Comparison;

/// <summary>
/// What a tolerance forgives, and - more important - what it does not.
///
/// <para>A tolerance exists to keep checking a field while allowing the part that legitimately moves.
/// Every test here is really the same assertion twice: the drift is forgiven, and the regression a
/// lazier rule would have swallowed with it is still reported.</para>
/// </summary>
public class ToleranceTests
{
    private static ComparisonOutcome Semantic(params ResponseDifference[] differences) =>
        new(differences.Length, true, differences);

    private static ResponseDifference Changed(string path, string left, string right) =>
        new(path, left, right, ResponseDifferenceKind.Changed);

    private static IReadOnlyList<ResolvedTolerance> Rules(params Tolerance[] tolerances) =>
        ToleranceResolver.Resolve([new ToleranceLayer(tolerances, ComparisonScope.Request, "Request")]);

    // ---- Numeric ---------------------------------------------------------------------------------

    [Fact]
    public void A_number_within_the_allowance_is_forgiven()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(Changed("$.total", "10.00", "10.005")),
            Rules(new Tolerance { Path = "$.total", Numeric = 0.01 }));

        Assert.True(result.Outcome.Same);
        Assert.Equal(1, result.Tolerated);
    }

    [Fact]
    public void A_number_outside_the_allowance_is_still_reported()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(Changed("$.total", "10.00", "11.00")),
            Rules(new Tolerance { Path = "$.total", Numeric = 0.01 }));

        Assert.False(result.Outcome.Same);
        Assert.Equal(0, result.Tolerated);
    }

    /// <summary>The rule names one field, so it says nothing about any other.</summary>
    [Fact]
    public void A_tolerance_forgives_only_the_field_it_names()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(
                Changed("$.total", "10.00", "10.005"),
                Changed("$.currency", "EUR", "USD")),
            Rules(new Tolerance { Path = "$.total", Numeric = 0.01 }));

        Assert.Equal(1, result.Tolerated);
        Assert.Equal("$.currency", Assert.Single(result.Outcome.Differences).Path);
    }

    /// <summary>A field that stopped being a number is a regression, not a rounding difference.</summary>
    [Fact]
    public void A_numeric_tolerance_does_not_forgive_a_value_that_is_no_longer_a_number()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(Changed("$.total", "10.00", "null")),
            Rules(new Tolerance { Path = "$.total", Numeric = 100 }));

        Assert.False(result.Outcome.Same);
    }

    // ---- Time ------------------------------------------------------------------------------------

    [Fact]
    public void A_timestamp_within_the_window_is_forgiven()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(Changed("$.generatedAt", "2026-09-08T19:00:00Z", "2026-09-08T19:04:00Z")),
            Rules(new Tolerance { Path = "$.generatedAt", WithinSeconds = 300 }));

        Assert.True(result.Outcome.Same);
    }

    [Fact]
    public void A_timestamp_outside_the_window_is_reported()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(Changed("$.generatedAt", "2026-09-08T19:00:00Z", "2026-09-09T19:00:00Z")),
            Rules(new Tolerance { Path = "$.generatedAt", WithinSeconds = 300 }));

        Assert.False(result.Outcome.Same);
    }

    // ---- Shape -----------------------------------------------------------------------------------

    /// <summary>BOTH sides have to match, which is what makes this a check rather than an ignore: a
    /// generated id is forgiven, an id that turned into an error message is not.</summary>
    [Fact]
    public void A_pattern_forgives_two_values_that_both_match_and_nothing_else()
    {
        var rule = Rules(new Tolerance { Path = "$.requestId", Matches = "^[0-9a-f]{4}$" });

        Assert.True(ToleranceEvaluator
            .Apply(Semantic(Changed("$.requestId", "a1b2", "c3d4")), rule).Outcome.Same);

        Assert.False(ToleranceEvaluator
            .Apply(Semantic(Changed("$.requestId", "a1b2", "missing")), rule).Outcome.Same);
    }

    [Fact]
    public void One_of_forgives_a_move_between_listed_values_only()
    {
        var rule = Rules(new Tolerance { Path = "$.status", OneOf = ["paid", "shipped"] });

        Assert.True(ToleranceEvaluator
            .Apply(Semantic(Changed("$.status", "paid", "shipped")), rule).Outcome.Same);

        Assert.False(ToleranceEvaluator
            .Apply(Semantic(Changed("$.status", "paid", "refunded")), rule).Outcome.Same);
    }

    /// <summary>An expression nobody can parse forgives nothing - the safe direction for a broken
    /// rule, since the alternative is a check that silently stopped checking.</summary>
    [Fact]
    public void An_unparseable_pattern_forgives_nothing()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(Changed("$.requestId", "a", "b")),
            Rules(new Tolerance { Path = "$.requestId", Matches = "[" }));

        Assert.False(result.Outcome.Same);
    }

    // ---- Paths -----------------------------------------------------------------------------------

    [Fact]
    public void A_wildcard_path_covers_every_element()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(
                Changed("$.orders[0].total", "1.00", "1.005"),
                Changed("$.orders[7].total", "9.00", "9.004")),
            Rules(new Tolerance { Path = "$.orders[*].total", Numeric = 0.01 }));

        Assert.True(result.Outcome.Same);
        Assert.Equal(2, result.Tolerated);
    }

    [Fact]
    public void A_descendant_path_covers_any_depth()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(Changed("$.a.b.c.elapsedMs", "10", "480")),
            Rules(new Tolerance { Path = "$..elapsedMs", Numeric = 500 }));

        Assert.True(result.Outcome.Same);
    }

    // ---- Array length ----------------------------------------------------------------------------

    /// <summary>The difference is reported at the ELEMENT, so the rule names the array and the lengths
    /// come from the two bodies - which is why this kind needs them and the others do not.</summary>
    [Fact]
    public void An_array_that_grew_within_the_percentage_is_forgiven()
    {
        var left = """{"orders":[1,2,3,4,5,6,7,8,9,10]}""";
        var right = """{"orders":[1,2,3,4,5,6,7,8,9,10,11]}""";

        var result = ToleranceEvaluator.Apply(
            Semantic(new ResponseDifference("$.orders[10]", null, "11", ResponseDifferenceKind.Added)),
            Rules(new Tolerance { Path = "$.orders", LengthWithinPercent = 10 }),
            left,
            right);

        Assert.True(result.Outcome.Same);
    }

    [Fact]
    public void An_array_that_grew_beyond_the_percentage_is_reported()
    {
        var left = """{"orders":[1,2]}""";
        var right = """{"orders":[1,2,3,4]}""";

        var result = ToleranceEvaluator.Apply(
            Semantic(
                new ResponseDifference("$.orders[2]", null, "3", ResponseDifferenceKind.Added),
                new ResponseDifference("$.orders[3]", null, "4", ResponseDifferenceKind.Added)),
            Rules(new Tolerance { Path = "$.orders", LengthWithinPercent = 10 }),
            left,
            right);

        Assert.False(result.Outcome.Same);
    }

    /// <summary>"This list may be a little longer" must not become "nothing in this list is checked".</summary>
    [Fact]
    public void A_length_tolerance_does_not_forgive_a_changed_element()
    {
        var left = """{"orders":[{"total":1}]}""";
        var right = """{"orders":[{"total":99}]}""";

        var result = ToleranceEvaluator.Apply(
            Semantic(Changed("$.orders[0].total", "1", "99")),
            Rules(new Tolerance { Path = "$.orders", LengthWithinPercent = 50 }),
            left,
            right);

        Assert.False(result.Outcome.Same);
    }

    // ---- Rules that cannot be applied ------------------------------------------------------------

    /// <summary>A text comparison has no fields to name. Saying so beats forgiving a whole hunk by
    /// accident, and beats a rule that silently does nothing while reading as a check.</summary>
    [Fact]
    public void Tolerances_do_not_apply_to_a_text_comparison_and_say_so()
    {
        var result = ToleranceEvaluator.Apply(
            new ComparisonOutcome(1, false, []),
            Rules(new Tolerance { Path = "$.total", Numeric = 0.01 }));

        Assert.False(result.Outcome.Same);
        Assert.Equal(0, result.Tolerated);
        Assert.Contains(result.Warnings, w => w.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_rule_stating_no_allowance_is_reported_rather_than_guessed_at()
    {
        var result = ToleranceEvaluator.Apply(
            Semantic(Changed("$.total", "1", "2")),
            Rules(new Tolerance { Path = "$.total" }));

        Assert.Contains(result.Warnings, w => w.Contains("$.total", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rule_stating_two_allowances_is_reported_rather_than_guessed_at()
    {
        var tolerance = new Tolerance { Path = "$.total", Numeric = 1, OneOf = ["a"] };

        Assert.Equal(ToleranceKind.None, tolerance.Kind);
    }

    // ---- Resolution ------------------------------------------------------------------------------

    /// <summary>Per path, closest wins - so a folder's generous default can be tightened on one
    /// endpoint without restating every other rule.</summary>
    [Fact]
    public void The_closest_level_wins_for_the_same_path()
    {
        var resolved = ToleranceResolver.Resolve([
            new ToleranceLayer([new Tolerance { Path = "$.total", Numeric = 100 }], ComparisonScope.Folder, "Folder: orders"),
            new ToleranceLayer([new Tolerance { Path = "$.total", Numeric = 0.01 }], ComparisonScope.Request, "Request"),
        ]);

        var only = Assert.Single(resolved);
        Assert.Equal(0.01, only.Tolerance.Numeric);
        Assert.Equal(ComparisonScope.Request, only.Scope);
    }

    /// <summary>Every other path keeps inheriting, which is what makes the hierarchy worth having.</summary>
    [Fact]
    public void Overriding_one_path_leaves_the_others_inherited()
    {
        var resolved = ToleranceResolver.Resolve([
            new ToleranceLayer(
                [new Tolerance { Path = "$.total", Numeric = 100 }, new Tolerance { Path = "$..id", Matches = "^x" }],
                ComparisonScope.Folder,
                "Folder: orders"),
            new ToleranceLayer([new Tolerance { Path = "$.total", Numeric = 1 }], ComparisonScope.Request, "Request"),
        ]);

        Assert.Equal(2, resolved.Count);
        Assert.Equal("Folder: orders", resolved.Single(r => r.Tolerance.Path == "$..id").SourceName);
    }
}
