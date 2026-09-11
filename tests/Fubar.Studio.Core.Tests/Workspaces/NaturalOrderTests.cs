using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Core.Tests.Workspaces;

/// <summary>
/// Sorting names the way a person reading a numbered list expects.
///
/// <para>Ordinary string ordering runs a batch of eleven items 1, 10, 11, 2, 3 - an order nobody wrote,
/// which reads as a bug in the runner rather than in the sort.</para>
/// </summary>
public class NaturalOrderTests
{
    private static string[] Sorted(params string[] names) =>
        [.. names.OrderBy(n => n, NaturalOrder.Instance)];

    // ---- The case this exists for ----------------------------------------------------------------

    [Fact]
    public void Ten_comes_after_two()
    {
        Assert.Equal(
            ["request-1", "request-2", "request-10"],
            Sorted("request-10", "request-1", "request-2"));
    }

    [Fact]
    public void A_long_run_stays_in_numeric_order()
    {
        Assert.Equal(
            ["step-1", "step-2", "step-3", "step-10", "step-11", "step-20", "step-100"],
            Sorted("step-100", "step-11", "step-2", "step-20", "step-1", "step-10", "step-3"));
    }

    // ---- Where the numbers are -------------------------------------------------------------------

    /// <summary>Numbers count wherever they appear, not only at the end.</summary>
    [Fact]
    public void A_number_in_the_middle_counts_too()
    {
        Assert.Equal(
            ["v2/step-3", "v2/step-10", "v10/step-1"],
            Sorted("v10/step-1", "v2/step-10", "v2/step-3"));
    }

    [Fact]
    public void Several_numbers_are_compared_in_turn()
    {
        Assert.Equal(
            ["a1b1", "a1b2", "a1b10", "a2b1"],
            Sorted("a2b1", "a1b10", "a1b1", "a1b2"));
    }

    // ---- Names with no numbers at all ------------------------------------------------------------

    [Fact]
    public void Plain_names_sort_alphabetically_and_ignore_case()
    {
        Assert.Equal(["apple", "Banana", "cherry"], Sorted("cherry", "apple", "Banana"));
    }

    /// <summary>A list holding "Step-2" and "step-10" is one list to the person who wrote it.</summary>
    [Fact]
    public void Case_does_not_split_a_numbered_run()
    {
        Assert.Equal(["Step-2", "step-10"], Sorted("step-10", "Step-2"));
    }

    [Fact]
    public void A_prefix_comes_before_what_extends_it()
    {
        Assert.Equal(["step", "step-1"], Sorted("step-1", "step"));
    }

    // ---- Ties --------------------------------------------------------------------------------

    /// <summary>
    /// Leading zeros do not change a number's value, so these are equal on the natural pass - and the
    /// raw string then decides, so the order never depends on which the directory returned first.
    /// </summary>
    [Fact]
    public void Leading_zeros_do_not_change_the_value_but_still_order_deterministically()
    {
        // Equal as NUMBERS - "step-01" does not sort after "step-2"...
        Assert.Equal(["step-01", "step-2"], Sorted("step-2", "step-01"));

        // ...but never equal as an ANSWER: the raw string breaks the tie, so the pair comes out the
        // same way round whichever way it went in.
        Assert.NotEqual(0, Compare("step-01", "step-1"));
        Assert.Equal(Sorted("step-01", "step-1"), Sorted("step-1", "step-01"));
    }

    [Fact]
    public void The_order_is_total_and_antisymmetric()
    {
        string[] names = ["step-1", "step-01", "Step-1", "step-2", "step", "step-10", "", "10", "2"];

        foreach (var a in names)
        {
            Assert.Equal(0, Compare(a, a));

            foreach (var b in names)
            {
                Assert.Equal(-Math.Sign(Compare(b, a)), Math.Sign(Compare(a, b)));
            }
        }
    }

    /// <summary>A file may be named with more digits than any integer type holds, and a sort that threw
    /// on somebody's file name would take the whole tree down with it.</summary>
    [Fact]
    public void A_number_too_big_for_any_integer_is_still_compared()
    {
        var huge = new string('9', 40);
        var bigger = "1" + new string('0', 40);

        Assert.True(Compare($"x{huge}", $"x{bigger}") < 0);
    }

    [Fact]
    public void Nulls_and_empties_are_handled()
    {
        Assert.Equal(0, Compare(null, null));
        Assert.True(Compare(null, "a") < 0);
        Assert.True(Compare("a", null) > 0);
        Assert.True(Compare("", "a") < 0);
    }

    // ---- The helper ------------------------------------------------------------------------------

    [Fact]
    public void Sort_orders_by_a_chosen_name()
    {
        var files = new[] { "/b/request-10.json", "/b/request-2.json" };

        Assert.Equal(
            ["/b/request-2.json", "/b/request-10.json"],
            NaturalOrder.Sort(files, Path.GetFileName));
    }

    private static int Compare(string? a, string? b) => NaturalOrder.Instance.Compare(a, b);
}
