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

    private static TextDocument DocumentFor(FileComparison comparison, DiffSide side) =>
        side == DiffSide.Left ? comparison.Left : comparison.Right;
}
