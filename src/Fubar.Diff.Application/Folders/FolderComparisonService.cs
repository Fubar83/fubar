using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Folders;

namespace Fubar.Diff.Application.Folders;

/// <inheritdoc />
public sealed class FolderComparisonService : IFolderComparisonService
{
    private readonly IFolderScanner _scanner;

    public FolderComparisonService(IFolderScanner scanner) => _scanner = scanner;

    public Task<FolderComparison> CompareAsync(
        string leftRoot,
        string rightRoot,
        FolderComparisonOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        // Off the calling thread: this reads every byte of every paired file, which on two real
        // checkouts is seconds of work. On the UI thread that is a frozen window, including the cancel
        // button that would let the user escape it.
        Task.Run(
            () => FolderComparer.Compare(leftRoot, rightRoot, _scanner, options, progress, cancellationToken),
            cancellationToken);

    public Task<FolderComparison> CompareLinkedAsync(
        string root,
        FolderComparisonOptions options,
        IReadOnlyList<LinkRule> rules,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => LinkedFolderComparer.Compare(root, _scanner, options, rules, progress, cancellationToken),
            cancellationToken);
}
