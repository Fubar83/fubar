using Fubar.Diff.Core.Code;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Infrastructure.Code;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// The structural C# comparison, end to end: real source text in, a list of what happened to each
/// member out.
///
/// Written against the parser and the differ TOGETHER rather than against either alone, because the
/// interesting failures live between them. The differ's matching is only as good as the signatures
/// the parser hands it, and the parser's token subtraction is only correct if the differ actually
/// stops reporting an ancestor when a descendant changes - neither is provable from one side.
/// </summary>
public class CodeStructureTests
{
    private static readonly RoslynCodeStructureParser Parser = new();

    private static IReadOnlyList<CodeChange> Compare(string left, string right)
    {
        Assert.True(Parser.TryParse(left, SourceLanguage.CSharp, out var leftRoot), "left did not parse");
        Assert.True(Parser.TryParse(right, SourceLanguage.CSharp, out var rightRoot), "right did not parse");

        return CodeStructureDiffer.Compare(leftRoot!, rightRoot!);
    }

    private static CodeChange Single(IReadOnlyList<CodeChange> changes)
    {
        Assert.Single(changes);

        return changes[0];
    }

    private const string OneClass = """
        namespace Reporting;

        public class Report
        {
            public string Title { get; set; }

            public int Total()
            {
                return 0;
            }

            public void Print()
            {
                Console.WriteLine(Title);
            }
        }
        """;

    // ---- Nothing changed ------------------------------------------------------------------------

    [Fact]
    public void Identical_files_produce_no_changes()
    {
        Assert.Empty(Compare(OneClass, OneClass));
    }

    // ---- The headline: cosmetic vs functional ---------------------------------------------------

    [Fact]
    public void Reindenting_a_method_is_cosmetic_rather_than_a_change()
    {
        // The answer nothing else gives. To a line differ this is a block of red beside a block of
        // green, indistinguishable from the method having been rewritten.
        var right = OneClass.Replace(
            """
                public int Total()
                {
                    return 0;
                }
            """,
            """
                public int Total() {
                        return 0;
                    }
            """);

        var change = Single(Compare(OneClass, right));

        Assert.Equal(CodeChangeKind.Cosmetic, change.Kind);
        Assert.Equal("Total", change.DisplayName);
        Assert.False(change.IsFunctional);
    }

    [Fact]
    public void Editing_a_comment_is_cosmetic()
    {
        var left = OneClass.Replace("public int Total()", "// counts things\n    public int Total()");
        var right = OneClass.Replace("public int Total()", "// counts all the things\n    public int Total()");

        var change = Single(Compare(left, right));

        Assert.Equal(CodeChangeKind.Cosmetic, change.Kind);
    }

    [Fact]
    public void Changing_a_body_is_functional()
    {
        var right = OneClass.Replace("return 0;", "return 1;");

        var change = Single(Compare(OneClass, right));

        Assert.Equal(CodeChangeKind.Modified, change.Kind);
        Assert.True(change.IsFunctional);
        Assert.Equal("Reporting.Report.Total()", change.Path);
    }

    [Fact]
    public void A_changed_method_does_not_report_its_class_and_namespace_as_changed_too()
    {
        // What the parser's token subtraction buys. Without it every ancestor of every edit is a
        // change, and the tree says "the file changed, the class changed, the method changed" where
        // only the last of those is information.
        var right = OneClass.Replace("return 0;", "return 1;");

        var change = Single(Compare(OneClass, right));

        Assert.Equal(CodeMemberKind.Method, change.MemberKind);
    }

    // ---- Moves ----------------------------------------------------------------------------------

    [Fact]
    public void A_method_moved_to_the_end_is_reported_as_moved_and_nothing_else()
    {
        var right = """
            namespace Reporting;

            public class Report
            {
                public string Title { get; set; }

                public void Print()
                {
                    Console.WriteLine(Title);
                }

                public int Total()
                {
                    return 0;
                }
            }
            """;

        var change = Single(Compare(OneClass, right));

        Assert.Equal(CodeChangeKind.Moved, change.Kind);
        Assert.True(change.IsMoved);
        Assert.False(change.IsFunctional);
    }

    [Fact]
    public void Inserting_a_method_at_the_top_does_not_mark_everything_below_it_as_moved()
    {
        // The reason move detection runs a longest-increasing-subsequence rather than comparing
        // indices: without it, one insertion marks every member after it, which is both wrong and
        // exactly the noise a structural view exists to remove.
        var right = OneClass.Replace(
            "    public string Title { get; set; }",
            """
                public int Version => 2;

                public string Title { get; set; }
            """);

        var change = Single(Compare(OneClass, right));

        Assert.Equal(CodeChangeKind.Added, change.Kind);
        Assert.Equal("Version", change.DisplayName);
    }

    [Fact]
    public void A_method_that_moved_AND_changed_reports_as_changed_with_the_move_beside_it()
    {
        var right = """
            namespace Reporting;

            public class Report
            {
                public string Title { get; set; }

                public void Print()
                {
                    Console.WriteLine(Title);
                }

                public int Total()
                {
                    return 42;
                }
            }
            """;

        var change = Single(Compare(OneClass, right));

        Assert.Equal(CodeChangeKind.Modified, change.Kind);
        Assert.True(change.IsMoved);
        Assert.Equal("changed and moved", change.Description);
    }

    // ---- Renames --------------------------------------------------------------------------------

    [Fact]
    public void A_renamed_method_with_an_identical_body_is_a_rename_rather_than_a_pair_of_unrelated_changes()
    {
        var right = OneClass.Replace("public void Print()", "public void Render()");

        var change = Single(Compare(OneClass, right));

        Assert.Equal(CodeChangeKind.Renamed, change.Kind);
        Assert.Equal("Print → Render", change.DisplayName);
        Assert.True(change.IsFunctional);
    }

    [Fact]
    public void A_rename_is_not_claimed_when_the_body_is_shared_with_another_member()
    {
        // Same rule MoveDetector holds itself to: a mark that says "this is the same thing" is worse
        // than nothing when it is wrong, and two members with the same one-line body say nothing about
        // which of them became which.
        var left = """
            public class Flags
            {
                public bool A() => true;
                public bool B() => true;
            }
            """;

        var right = """
            public class Flags
            {
                public bool C() => true;
                public bool D() => true;
            }
            """;

        var changes = Compare(left, right);

        Assert.Equal(4, changes.Count);
        Assert.All(changes, c => Assert.True(c.Kind is CodeChangeKind.Added or CodeChangeKind.Removed));
    }

    // ---- Signature changes ----------------------------------------------------------------------

    [Fact]
    public void Adding_a_parameter_is_a_change_to_the_method_rather_than_a_new_one()
    {
        var right = OneClass.Replace("public int Total()", "public int Total(int seed)");

        var change = Single(Compare(OneClass, right));

        Assert.Equal(CodeChangeKind.Modified, change.Kind);
        Assert.Equal("Total", change.DisplayName);
    }

    [Fact]
    public void A_field_promoted_to_a_property_is_one_change_not_two()
    {
        var left = """
            public class Report
            {
                public string Title;
            }
            """;

        var right = """
            public class Report
            {
                public string Title { get; set; }
            }
            """;

        var change = Single(Compare(left, right));

        Assert.Equal(CodeChangeKind.Modified, change.Kind);
        Assert.Equal("Title", change.DisplayName);
    }

    [Fact]
    public void Two_overloads_stay_two_members()
    {
        var left = """
            public class Report
            {
                public int Total() => 0;
                public int Total(int seed) => seed;
            }
            """;

        var right = left.Replace("public int Total(int seed) => seed;", "public int Total(int seed) => seed + 1;");

        var change = Single(Compare(left, right));

        Assert.Equal("Report.Total(int)", change.Path);
    }

    // ---- Adding and removing ---------------------------------------------------------------------

    [Fact]
    public void A_removed_class_is_one_removal_and_not_one_per_member()
    {
        // Listing every method of a deleted class buries the fact that it is the class that went.
        var left = OneClass;
        var right = "namespace Reporting;";

        var change = Single(Compare(left, right));

        Assert.Equal(CodeChangeKind.Removed, change.Kind);
        Assert.Equal(CodeMemberKind.Class, change.MemberKind);
    }

    [Fact]
    public void A_using_is_a_member_like_any_other()
    {
        var left = "using System;\n\npublic class A { }";
        var right = "using System;\nusing System.Linq;\n\npublic class A { }";

        var change = Single(Compare(left, right));

        Assert.Equal(CodeChangeKind.Added, change.Kind);
        Assert.Equal(CodeMemberKind.Import, change.MemberKind);
        Assert.Equal("System.Linq", change.DisplayName);
    }

    [Fact]
    public void Reordering_usings_is_a_move_and_not_a_rewrite()
    {
        var left = "using System.Linq;\nusing System;\n\npublic class A { }";
        var right = "using System;\nusing System.Linq;\n\npublic class A { }";

        var changes = Compare(left, right);

        Assert.All(changes, c => Assert.Equal(CodeChangeKind.Moved, c.Kind));
        Assert.NotEmpty(changes);
    }

    // ---- The summary -----------------------------------------------------------------------------

    [Fact]
    public void A_reformatted_file_says_so_in_one_sentence()
    {
        var right = OneClass
            .Replace("    public int Total()\n    {\n        return 0;\n    }", "    public int Total()\n    {\n            return 0;\n    }")
            .Replace("        Console.WriteLine(Title);", "        Console.WriteLine( Title );");

        var summary = CodeStructureSummary.Of(Compare(OneClass, right));

        Assert.True(summary.NoFunctionalChange);
        Assert.StartsWith("No functional changes", summary.Caption());
    }

    [Fact]
    public void A_real_change_is_never_called_cosmetic()
    {
        var right = OneClass.Replace("return 0;", "return 1;");

        var summary = CodeStructureSummary.Of(Compare(OneClass, right));

        Assert.False(summary.NoFunctionalChange);
        Assert.Equal(1, summary.Functional);
        Assert.Equal("1 changed", summary.Caption());
    }

    [Fact]
    public void Identical_files_are_not_described_as_having_no_functional_changes()
    {
        // Technically true and completely misleading: it reads as though a difference was found and
        // dismissed, when there was none.
        var summary = CodeStructureSummary.Of(Compare(OneClass, OneClass));

        Assert.False(summary.NoFunctionalChange);
        Assert.Equal(string.Empty, summary.Caption());
    }

    // ---- Robustness ------------------------------------------------------------------------------

    [Fact]
    public void A_file_that_does_not_compile_still_parses()
    {
        // Mid-edit, mid-merge, mid-conflict - which is when a diff is wanted most. Roslyn's parser
        // recovers rather than throwing, and this pins that we rely on it rather than on valid input.
        const string broken = """
            public class Report
            {
                public int Total()
                {
                    return
            """;

        Assert.True(Parser.TryParse(broken, SourceLanguage.CSharp, out var root));
        Assert.NotNull(root);
        Assert.Single(root!.Children);
    }

    [Fact]
    public void A_file_with_no_declarations_at_all_is_reported_as_unparsed()
    {
        // Otherwise two different files both produce an empty tree, compare equal, and the summary
        // says "no functional changes" about a pair that differs - the one answer this must never
        // give wrongly.
        Assert.False(Parser.TryParse("Console.WriteLine(1);", SourceLanguage.CSharp, out _));
        Assert.False(Parser.TryParse("   ", SourceLanguage.CSharp, out _));
    }

    [Fact]
    public void Only_CSharp_is_claimed()
    {
        Assert.True(Parser.CanParse(SourceLanguage.CSharp));
        Assert.False(Parser.CanParse(SourceLanguage.TypeScript));
        Assert.False(Parser.TryParse(OneClass, SourceLanguage.Java, out _));
    }

    [Fact]
    public void Line_endings_alone_are_not_reported_as_every_member_being_touched()
    {
        // TextFormatDifference already says this once about the whole file. Saying it again once per
        // member would bury every real answer underneath it.
        var right = OneClass.Replace("\n", "\r\n");

        Assert.Empty(Compare(OneClass, right));
    }

    [Fact]
    public void A_span_points_at_the_member_it_describes()
    {
        // What lets a click in the structure tree scroll the text view to the right place.
        var right = OneClass.Replace("return 0;", "return 1;");
        var change = Single(Compare(OneClass, right));

        Assert.True(change.Right!.Span.IsKnown);
        Assert.Equal(7, change.Right.Span.StartLine);
    }
}
