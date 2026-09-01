using Fubar.Diff.Core.Languages;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Extension mapping, and the pair rule. Nothing clever - which is the point: guessing a language from
/// content would be wrong on exactly the short files where being wrong is most visible.
/// </summary>
public class LanguageDetectorTests
{
    [Theory]
    [InlineData("Program.cs", SourceLanguage.CSharp)]
    [InlineData("script.csx", SourceLanguage.CSharp)]
    [InlineData("app.js", SourceLanguage.JavaScript)]
    [InlineData("app.mjs", SourceLanguage.JavaScript)]
    [InlineData("Component.jsx", SourceLanguage.JavaScript)]
    [InlineData("model.ts", SourceLanguage.TypeScript)]
    [InlineData("Component.tsx", SourceLanguage.TypeScript)]
    [InlineData("notes.txt", SourceLanguage.None)]
    [InlineData("data.json", SourceLanguage.None)]
    [InlineData("Makefile", SourceLanguage.None)]
    public void An_extension_decides_the_language(string path, SourceLanguage expected) =>
        Assert.Equal(expected, LanguageDetector.FromPath(path));

    [Fact]
    public void A_full_path_works_the_same_as_a_bare_name() =>
        Assert.Equal(SourceLanguage.CSharp, LanguageDetector.FromPath(@"C:\src\project\Program.cs"));

    [Fact]
    public void Extensions_are_matched_regardless_of_case() =>
        Assert.Equal(SourceLanguage.CSharp, LanguageDetector.FromPath("PROGRAM.CS"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_path_means_no_language(string? path) =>
        Assert.Equal(SourceLanguage.None, LanguageDetector.FromPath(path));

    [Fact]
    public void An_in_memory_label_is_not_a_language()
    {
        // API Studio compares two strings and puts a LABEL where a path would go. It must not be
        // mistaken for a file whose name happens to end in something.
        Assert.Equal(SourceLanguage.None, LanguageDetector.FromPath("response"));
    }

    [Fact]
    public void The_left_side_decides_when_both_are_known() =>
        Assert.Equal(SourceLanguage.JavaScript, LanguageDetector.ForPair("a.js", "b.ts"));

    [Fact]
    public void The_known_side_decides_when_only_one_is()
    {
        // Comparing a .cs against a .txt copy of it: scanning both with C# rules beats scanning
        // neither.
        Assert.Equal(SourceLanguage.CSharp, LanguageDetector.ForPair("a.txt", "b.cs"));
        Assert.Equal(SourceLanguage.CSharp, LanguageDetector.ForPair("a.cs", "b.txt"));
    }

    [Fact]
    public void Neither_side_known_means_no_language() =>
        Assert.Equal(SourceLanguage.None, LanguageDetector.ForPair("a.txt", "b.log"));
}
