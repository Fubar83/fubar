using System.Diagnostics;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Looking a property up by name, on objects of every size.
///
/// This is a lookup with two implementations behind one method - a scan for the small objects JSON
/// usually holds, an index for the large ones it sometimes does - so the rules that matter are that
/// they agree, and that the second one exists at all. Every caller of Find is inside a loop over the
/// other document's properties, which made a scan quadratic: a 120,000-property document spent 45
/// seconds in ArrayKeyScanner alone, looking for arrays that were not there.
/// </summary>
public class JsonAstObjectLookupTests
{
    private static JsonAstObject Object(params string[] names) => new(
        [.. names.Select((name, i) => new JsonAstProperty(
            name,
            new JsonAstScalar(JsonAstKind.Number, i.ToString(), null, new SourceSpan(i + 1, 1, i + 1, 2)),
            new SourceSpan(i + 1, 1, i + 1, 2)))],
        new SourceSpan(1, 1, names.Length + 1, 2));

    [Theory]
    [InlineData(3)]
    [InlineData(200)]
    public void A_property_is_found_whichever_side_of_the_index_threshold_it_is(int size)
    {
        var subject = Object([.. Enumerable.Range(0, size).Select(i => $"key{i}")]);

        Assert.Equal("key0", subject.Find("key0")?.Name);
        Assert.Equal($"key{size - 1}", subject.Find($"key{size - 1}")?.Name);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(200)]
    public void A_name_that_is_not_there_is_null(int size)
    {
        var subject = Object([.. Enumerable.Range(0, size).Select(i => $"key{i}")]);

        Assert.Null(subject.Find("absent"));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(200)]
    public void A_duplicate_name_resolves_to_the_first_one(int size)
    {
        // Duplicate names are legal JSON and ill-defined; the documented answer is "the first", and
        // the index has to keep that promise rather than whichever the dictionary happened to store.
        var names = new List<string> { "dup" };
        names.AddRange(Enumerable.Range(0, size).Select(i => $"key{i}"));
        names.Add("dup");

        var subject = Object([.. names]);
        var found = subject.Find("dup");

        Assert.Same(subject.Properties[0], found);
    }

    [Fact]
    public void Lookups_on_a_large_object_do_not_stay_linear()
    {
        // The regression this file exists for. A scan over 100,000 properties, once per property, is
        // ten billion comparisons; an index is a hundred thousand. The budget is enormous on purpose -
        // what it catches is the index being lost, not a machine having a slow afternoon.
        var subject = Object([.. Enumerable.Range(0, 100_000).Select(i => $"key{i}")]);

        var stopwatch = Stopwatch.StartNew();
        foreach (var property in subject.Properties)
        {
            Assert.NotNull(subject.Find(property.Name));
        }

        Assert.True(
            stopwatch.ElapsedMilliseconds < 5_000,
            $"100,000 lookups took {stopwatch.ElapsedMilliseconds} ms, which means they are scanning again");
    }
}
