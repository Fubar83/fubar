using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// Choosing how an array is matched, from the change tree's right-click menu.
///
/// Through the REAL comparison, because the thing worth checking is that picking a key actually
/// changes what the diff says - a menu that records a preference and leaves the answer alone would
/// look like it worked.
/// </summary>
public class ArrayMatchingTests
{
    private sealed class Files(Dictionary<string, string> files) : ITextFileReader, ITextFileWriter
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TextDocument(path, files[path].Split('\n'), TextFormat.Default));

        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class Prompt(string? answer) : IConfirmationService
    {
        public string? LastMessage { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) => Task.FromResult(false);

        public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices) => Task.FromResult(-1);

        public Task<string?> AskForTextAsync(string title, string message, string initial = "")
        {
            LastMessage = message;

            return Task.FromResult(answer);
        }
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickFilesAsync(string title) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class NoWatcher : IFileChangeWatcher
    {
        public event EventHandler? Changed;

        public void Watch(IReadOnlyList<string> paths) => _ = Changed;

        public void Stop() { }

        public void Dispose() { }
    }

    private sealed class NoClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;
    }

    private static ComparisonViewModel Build(string left, string right, IConfirmationService? prompt = null)
    {
        var disk = new Files(new Dictionary<string, string> { ["l.json"] = left, ["r.json"] = right });

        var comparisons = new FileComparisonService(
            disk,
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

        return new ComparisonViewModel(
            comparisons, new MergeService(disk), new NoPicker(), new NoWatcher(),
            new NoClipboard(), disk, new ThemeManagerViewModel(), prompt)
        {
            LeftPath = "l.json",
            RightPath = "r.json",
            Mode = ComparisonMode.Json,
        };
    }

    /// <summary>Two lists of the same elements in a different order, keyed on a field nothing detects.</summary>
    private const string Left = """{"items":[{"ref":"a","v":1},{"ref":"b","v":2}]}""";

    private const string Right = """{"items":[{"ref":"b","v":2},{"ref":"a","v":1}]}""";

    private static JsonChangeNodeViewModel? ArrayRow(ComparisonViewModel tab) =>
        Flatten(tab.Pane.SemanticTree).FirstOrDefault(n => n.IsArray);

    private static IEnumerable<JsonChangeNodeViewModel> Flatten(IReadOnlyList<JsonChangeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    [AvaloniaFact]
    public async Task An_array_row_offers_the_fields_its_elements_share()
    {
        var tab = Build(Left, Right);
        await tab.CompareAsync();

        var row = ArrayRow(tab);

        Assert.NotNull(row);
        Assert.Contains(row!.ArrayKeyOptions, o => o.Key == "ref");
        Assert.Contains(row.ArrayKeyOptions, o => o.Key == "v");

        // Comparing by position is always on the menu, because for some arrays order IS the content.
        Assert.Contains(row.ArrayKeyOptions, o => o.Key is null);
    }

    [AvaloniaFact]
    public async Task Choosing_a_key_changes_what_the_diff_SAYS()
    {
        // The whole point. Reordered elements read as changes until the elements have identity.
        var tab = Build(Left, Right);
        await tab.CompareAsync();

        Assert.True(tab.Pane.HasChanges);

        await tab.ApplyArrayKeyAsync("$.items", ArrayMatchMode.Key, "ref");

        Assert.False(tab.Pane.HasChanges);
    }

    [AvaloniaFact]
    public async Task The_menu_command_reaches_the_same_place()
    {
        // The tests above call the operation directly so they can await it. This one checks the wiring
        // the user actually goes through - the row's menu entry, the pane's command, the host's
        // handler - which is otherwise the untested half.
        var tab = Build(Left, Right);
        await tab.CompareAsync();

        var row = ArrayRow(tab)!;
        tab.Pane.ChooseArrayKeyCommand.Execute(row.ArrayKeyOptions.First(o => o.Key == "ref"));

        Assert.Contains(tab.ArrayKeyOverrides, o => o.Path == "$.items" && o.Key == "ref");
    }

    [AvaloniaFact]
    public async Task Choosing_position_puts_the_differences_back()
    {
        var tab = Build(Left, Right);
        await tab.CompareAsync();

        await tab.ApplyArrayKeyAsync("$.items", ArrayMatchMode.Key, "ref");
        Assert.False(tab.Pane.HasChanges);

        await tab.ApplyArrayKeyAsync("$.items", ArrayMatchMode.Position);

        Assert.True(tab.Pane.HasChanges);
    }

    [AvaloniaFact]
    public async Task The_choice_applies_to_that_array_alone()
    {
        // The reason it is per-array: one document can hold a list where order means nothing beside a
        // list where order is the entire content. Keyed on "ref", which auto-detection does not know,
        // so both arrays genuinely differ and both get a row to right-click.
        var tab = Build(
            """{"users":[{"ref":"a"},{"ref":"b"}],"steps":[{"ref":"x"},{"ref":"y"}]}""",
            """{"users":[{"ref":"b"},{"ref":"a"}],"steps":[{"ref":"y"},{"ref":"x"}]}""");

        await tab.CompareAsync();

        var steps = Flatten(tab.Pane.SemanticTree).First(n => n.Path == "$.steps");

        Assert.True(steps.IsArray);
        await tab.ApplyArrayKeyAsync(steps.ArrayChoices!.Path, ArrayMatchMode.Position);

        Assert.Contains("$.steps", tab.PositionalArrays);
        Assert.DoesNotContain("$.users", tab.PositionalArrays);
    }

    [AvaloniaFact]
    public async Task A_key_and_position_are_never_both_recorded_for_one_array()
    {
        // An array cannot be both, and a stale entry in the other list would make the menu's state
        // disagree with what the comparison is doing.
        var tab = Build(Left, Right);
        await tab.CompareAsync();

        await tab.ApplyArrayKeyAsync("$.items", ArrayMatchMode.Position);
        Assert.Contains("$.items", tab.PositionalArrays);

        await tab.ApplyArrayKeyAsync("$.items", ArrayMatchMode.Key, "ref");

        Assert.DoesNotContain("$.items", tab.PositionalArrays);
        Assert.Contains(tab.ArrayKeyOverrides, o => o.Path == "$.items" && o.Key == "ref");
    }

    [AvaloniaFact]
    public async Task A_field_the_menu_did_not_offer_can_be_typed()
    {
        // For identity nested deeper than the scanner looks, or carried by only some elements today.
        var prompt = new Prompt("meta.id");

        var tab = Build(
            """{"items":[{"meta":{"id":1},"v":"a"},{"meta":{"id":2},"v":"b"}]}""",
            """{"items":[{"meta":{"id":2},"v":"b"},{"meta":{"id":1},"v":"a"}]}""",
            prompt);

        await tab.CompareAsync();

        tab.Pane.RequestCustomArrayKeyCommand.Execute("$.items");
        await Task.Yield();

        Assert.Contains(tab.ArrayKeyOverrides, o => o.Path == "$.items" && o.Key == "meta.id");
        Assert.Contains("$.items", prompt.LastMessage!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Cancelling_that_prompt_records_nothing()
    {
        var tab = Build(Left, Right, new Prompt(null));
        await tab.CompareAsync();

        tab.Pane.RequestCustomArrayKeyCommand.Execute("$.items");
        await Task.Yield();

        Assert.Empty(tab.ArrayKeyOverrides);
        Assert.Empty(tab.PositionalArrays);
    }

    [AvaloniaFact]
    public async Task A_text_comparison_has_no_array_rows_at_all()
    {
        var tab = Build("hello", "world");
        tab.Mode = ComparisonMode.Text;

        await tab.CompareAsync();

        Assert.DoesNotContain(Flatten(tab.Pane.SemanticTree), n => n.IsArray);
    }
}
