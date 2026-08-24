using Fubar.Diff.Core.Models;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// One row in the side-by-side grid. A thin, immutable projection of <see cref="DiffLine"/> that
/// pre-computes exactly what the view binds to, so the XAML needs no converters and rows stay cheap
/// enough to virtualise.
///
/// The state is exposed as one boolean per style class rather than a class-name string because
/// Avalonia binds style classes as <c>Classes.name="{Binding Flag}"</c> - <c>Classes</c> itself is not
/// a bindable property.
/// </summary>
public sealed class DiffRowViewModel
{
    public DiffRowViewModel(DiffLine line)
    {
        Line = line;
        LeftNumber = line.LeftNumber?.ToString() ?? string.Empty;
        RightNumber = line.RightNumber?.ToString() ?? string.Empty;
        LeftText = line.LeftText ?? string.Empty;
        RightText = line.RightText ?? string.Empty;

        // A row is styled per SIDE, not once: a Modified row is tinted on both sides, but a Deleted
        // row is tinted on the left and shows an inert filler on the right.
        IsModified = line.Kind == ChangeKind.Modified;
        LeftDeleted = line.Kind == ChangeKind.Deleted;
        RightInserted = line.Kind == ChangeKind.Inserted;
        LeftFiller = line.Kind == ChangeKind.Inserted;
        RightFiller = line.Kind == ChangeKind.Deleted;
    }

    public DiffLine Line { get; }

    public string LeftNumber { get; }
    public string RightNumber { get; }
    public string LeftText { get; }
    public string RightText { get; }

    /// <summary>Both sides have content and it differs - tint both.</summary>
    public bool IsModified { get; }

    /// <summary>Left-only content: a deletion.</summary>
    public bool LeftDeleted { get; }

    /// <summary>Right-only content: an insertion.</summary>
    public bool RightInserted { get; }

    /// <summary>No left line at all - the placeholder opposite an insertion.</summary>
    public bool LeftFiller { get; }

    /// <summary>No right line at all - the placeholder opposite a deletion.</summary>
    public bool RightFiller { get; }
}
