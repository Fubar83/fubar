using System;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Application.Merge;

/// <inheritdoc />
public sealed class MergeService : IMergeService
{
    private readonly ITextFileWriter _writer;

    public MergeService(ITextFileWriter writer) => _writer = writer;

    public async Task<string> SaveAsync(
        FileComparison comparison,
        MergeState state,
        DiffSide baseSide,
        string? targetPath = null,
        CancellationToken cancellationToken = default)
    {
        var baseDocument = DocumentFor(comparison, baseSide);
        var path = targetPath ?? baseDocument.Path;

        if (string.IsNullOrWhiteSpace(path))
        {
            // Nothing was loaded from disk, so there is no obvious destination. The UI should have
            // prompted for one; failing loudly beats writing to a surprising location.
            throw new TextFileWriteException("(no path)", "no destination file was given.");
        }

        var lines = MergedDocument.Build(comparison.Result, state, baseSide);

        // The BASE document's format is preserved, not the target path's - saving "mine" keeps mine's
        // encoding and line endings even when the two files disagree about them.
        await _writer.WriteAsync(path, lines, baseDocument.Format, cancellationToken).ConfigureAwait(false);

        return path;
    }

    public string Preview(FileComparison comparison, MergeState state, DiffSide baseSide) =>
        MergedDocument.ToText(
            MergedDocument.Build(comparison.Result, state, baseSide),
            DocumentFor(comparison, baseSide).Format);

    public async Task<string> SaveThreeWayAsync(
        ThreeWayComparison comparison,
        ThreeWayMergeState state,
        MergeSide destination,
        string? targetPath = null,
        CancellationToken cancellationToken = default)
    {
        var document = comparison.DocumentFor(destination);
        var path = targetPath ?? document.Path;

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new TextFileWriteException("(no path)", "no destination file was given.");
        }

        // Unresolved conflicts are NOT refused here. The domain already has a defined answer for them
        // (keep the ancestor - see ThreeWayMergedDocument), and a service that threw instead would make
        // "save what I have so far" impossible in the middle of a long merge. Warning the user is the
        // UI's job, and it has ThreeWayMergeState.UnresolvedConflicts to do it with.
        var lines = ThreeWayMergedDocument.Build(comparison.Result, state);

        await _writer.WriteAsync(path, lines, document.Format, cancellationToken).ConfigureAwait(false);

        return path;
    }

    public string PreviewThreeWay(ThreeWayComparison comparison, ThreeWayMergeState state, MergeSide destination) =>
        MergedDocument.ToText(
            ThreeWayMergedDocument.Build(comparison.Result, state),
            comparison.DocumentFor(destination).Format);

    private static TextDocument DocumentFor(FileComparison comparison, DiffSide side) =>
        side == DiffSide.Left ? comparison.Left : comparison.Right;
}
