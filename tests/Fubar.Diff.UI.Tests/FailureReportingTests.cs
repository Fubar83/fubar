using Avalonia.Headless.XUnit;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// What the tab does with a failure nobody phrased for it.
///
/// <para>Both paths here are awaited by an <c>async void</c> - a click handler, a property setter, a
/// fire-and-forget task at startup - so an exception that escapes is either a dead process or a window
/// that opens empty and says nothing at all. This repository has shipped two process-killers of
/// exactly that shape (see CLAUDE.md), which is why the catch is wide and the report is the same
/// banner every other failure uses.</para>
///
/// <para>The readers phrase what they can: a missing file, a folder, a file too large, an IO or
/// permission error all arrive as <see cref="TextFileReadException"/> and are shown as-is. These are
/// the ones that arrive as anything else.</para>
/// </summary>
public class FailureReportingTests
{
    /// <summary>A disk where one named path throws something the readers never wrap.</summary>
    private sealed class AwkwardDisk(string throwsOn, Exception failure) : ITextFileReader, ITextFileWriter
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            string.Equals(path, throwsOn, StringComparison.Ordinal)
                ? throw failure
                : Task.FromResult(new TextDocument(path, ["a", "b"], TextFormat.Default));

        public Task WriteAsync(string path, IReadOnlyList<string> lines, TextFormat format, CancellationToken ct = default) =>
            Task.CompletedTask;
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

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickFilesAsync(string title) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    /// <summary>Compares normally, and throws when asked to re-run under new options.</summary>
    private sealed class RecomparesBadly(IFileComparisonService inner) : IFileComparisonService
    {
        public Task<FileComparison> CompareFilesAsync(
            string leftPath, string rightPath, ComparisonOptions options, CancellationToken ct = default) =>
            inner.CompareFilesAsync(leftPath, rightPath, options, ct);

        public Task<FileComparison> RecompareAsync(
            FileComparison comparison, ComparisonOptions options, CancellationToken ct = default) =>
            throw new InvalidOperationException("the engine gave up");

        public FileComparison Recompare(FileComparison comparison, ComparisonOptions options) =>
            throw new InvalidOperationException("the engine gave up");

        public Task<FileComparison> CompareTextAsync(
            string leftText, string rightText, ComparisonOptions options,
            string leftLabel = "left", string rightLabel = "right", CancellationToken ct = default) =>
            inner.CompareTextAsync(leftText, rightText, options, leftLabel, rightLabel, ct);

        public Task<FileComparison> CompareDocumentsAsync(
            TextDocument left, TextDocument right, ComparisonOptions options, CancellationToken ct = default) =>
            inner.CompareDocumentsAsync(left, right, options, ct);

        public JsonDisplay FormatJsonForDisplay(
            FileComparison comparison, bool prettyLeft, bool prettyRight, JsonFormatOptions format) =>
            inner.FormatJsonForDisplay(comparison, prettyLeft, prettyRight, format);
    }

    private static FileComparisonService RealService(ITextFileReader disk) =>
        new(disk,
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

    private static ComparisonViewModel Tab(IFileComparisonService comparisons, ITextFileWriter writer) =>
        new(comparisons,
            new MergeService(writer),
            new NoPicker(),
            new NoWatcher(),
            new NoClipboard(),
            writer,
            new ThemeManagerViewModel());

    [AvaloniaFact]
    public async Task A_path_the_platform_rejects_is_reported_rather_than_thrown()
    {
        // `new FileInfo(path)` throws ArgumentException on a path holding an invalid character -
        // before any of the reader's own try/catch runs, so it never becomes a TextFileReadException.
        // From the command line that landed in an unobserved task: FubarDiff "a|b.txt" c.txt opened an
        // empty window and said nothing.
        var disk = new AwkwardDisk("a|b.txt", new ArgumentException("Illegal characters in path."));
        var tab = Tab(RealService(disk), disk);

        await tab.InitializeAsync("a|b.txt", "right.txt");

        Assert.NotNull(tab.ErrorMessage);
        Assert.Contains("Illegal characters", tab.ErrorMessage!, StringComparison.Ordinal);
        Assert.Equal("Comparison failed.", tab.StatusMessage);
    }

    [AvaloniaFact]
    public async Task A_phrased_read_failure_is_still_shown_as_the_domain_phrased_it()
    {
        // The wide catch must not swallow the narrow one: these messages are written for a user and
        // are shown as-is, with no "Could not compare these files" wrapped around them.
        var disk = new AwkwardDisk("gone.txt", new TextFileReadException("gone.txt", "the file does not exist."));
        var tab = Tab(RealService(disk), disk);

        await tab.InitializeAsync("gone.txt", "right.txt");

        Assert.Equal("Could not read 'gone.txt': the file does not exist.", tab.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task An_option_that_cannot_be_applied_reports_instead_of_killing_the_process()
    {
        // RecompareAsync caught only OperationCanceledException, and its caller is
        // `private async void Recompare()`. Anything else the engine threw while re-diffing documents
        // already in memory went straight out to the dispatcher.
        var disk = new AwkwardDisk("nothing", new InvalidOperationException());
        var tab = Tab(new RecomparesBadly(RealService(disk)), disk);

        await tab.InitializeAsync("left.txt", "right.txt");
        Assert.Null(tab.ErrorMessage);

        await tab.RecompareAsync();

        Assert.NotNull(tab.ErrorMessage);
        Assert.Contains("the engine gave up", tab.ErrorMessage!, StringComparison.Ordinal);
    }
}
