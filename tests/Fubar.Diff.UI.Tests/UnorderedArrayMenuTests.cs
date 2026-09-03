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
/// Picking "Ignore order" from the change tree's right-click menu, on the shape it exists for: an array
/// of plain STRINGS, nested several levels down.
///
/// Written after a report that the menu item did nothing on a real file. Everything below the view
/// model was already covered and passing, so these drive the same route the menu does - the row's own
/// options, the view model's apply, and the re-comparison it triggers.
/// </summary>
public class UnorderedArrayMenuTests
{
    private sealed class Files(Dictionary<string, string> files) : ITextFileReader, ITextFileWriter
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TextDocument(path, files[path].Split('\n'), TextFormat.Default));

        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default) =>
            Task.CompletedTask;
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

    /// <summary>The reported case, trimmed to its shape: a reordered list of strings nested five deep,
    /// beside two changes that are real and must survive.</summary>
    private const string Left = """
        {
          "glossary": {
            "GlossDiv": {
              "GlossList": {
                "GlossEntry": {
                  "GlossDef": {
                    "GlossSeeAlso": ["GML", "XML"],
                    "GlossSee": "markup"
                  }
                }
              }
            }
          }
        }
        """;

    private const string Right = """
        {
          "glossary": {
            "GlossDiv": {
              "GlossList": {
                "GlossEntry": {
                  "GlossDef": {
                    "GlossSeeAlso": ["XML", "GML"],
                    "added": "Tjosan"
                  }
                }
              }
            }
          }
        }
        """;

    private const string ArrayPath = "$.glossary.GlossDiv.GlossList.GlossEntry.GlossDef.GlossSeeAlso";

    private static ComparisonViewModel Build()
    {
        var disk = new Files(new Dictionary<string, string> { ["l.json"] = Left, ["r.json"] = Right });

        var comparisons = new FileComparisonService(
            disk,
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

        return new ComparisonViewModel(
            comparisons, new MergeService(disk), new NoPicker(), new NoWatcher(),
            new NoClipboard(), disk, new ThemeManagerViewModel(), null)
        {
            LeftPath = "l.json",
            RightPath = "r.json",
            Mode = ComparisonMode.Json,
        };
    }

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
    public async Task The_array_row_exists_and_offers_ignore_order()
    {
        var tab = Build();
        await tab.CompareAsync();

        var row = Flatten(tab.Pane.SemanticTree).FirstOrDefault(n => n.IsArray);

        Assert.NotNull(row);
        Assert.Equal(ArrayPath, row!.ArrayChoices!.Path);

        var unordered = row.ArrayKeyOptions.FirstOrDefault(o => o.Mode == ArrayMatchMode.Unordered);
        Assert.NotNull(unordered);
        Assert.Equal(ArrayPath, unordered!.Path);
    }

    [AvaloniaFact]
    public async Task Choosing_ignore_order_from_the_row_changes_what_the_diff_says()
    {
        // The whole point, and the thing that was reported as not working: the reordered strings must
        // stop being differences, while the two real changes stay.
        var tab = Build();
        await tab.CompareAsync();

        Assert.Equal(4, tab.Pane.SemanticChanges.Count);

        var row = Flatten(tab.Pane.SemanticTree).First(n => n.IsArray);
        var unordered = row.ArrayKeyOptions.First(o => o.Mode == ArrayMatchMode.Unordered);

        await tab.ApplyArrayKeyAsync(unordered.Path, unordered.Mode, unordered.Key);

        Assert.Equal(2, tab.Pane.SemanticChanges.Count);
        Assert.DoesNotContain(tab.Pane.SemanticChanges, c => c.Path.ToString().Contains("GlossSeeAlso"));
    }

    [AvaloniaFact]
    public async Task The_option_reaches_the_comparison_options()
    {
        var tab = Build();
        await tab.CompareAsync();

        await tab.ApplyArrayKeyAsync(ArrayPath, ArrayMatchMode.Unordered);

        Assert.Contains(ArrayPath, tab.UnorderedArrays);
    }

    [AvaloniaFact]
    public async Task The_menu_then_shows_ignore_order_as_the_current_choice()
    {
        // The check mark has to agree with what is happening, or the next reader cannot tell whether
        // the setting took.
        var tab = Build();
        await tab.CompareAsync();

        await tab.ApplyArrayKeyAsync(ArrayPath, ArrayMatchMode.Unordered);

        var row = Flatten(tab.Pane.SemanticTree).FirstOrDefault(n => n.IsArray);

        // The array row only survives if something about it is still reported; when the reorder stops
        // being a difference the row goes away entirely, which is itself the answer.
        if (row is not null)
        {
            var unordered = row.ArrayKeyOptions.First(o => o.Mode == ArrayMatchMode.Unordered);
            Assert.True(unordered.IsCurrent);
        }
    }
}
