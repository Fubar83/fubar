using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Diff.Core.Code;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// One row of the structure panel: a member, and what happened to it.
/// </summary>
public sealed class CodeChangeItemViewModel
{
    public CodeChangeItemViewModel(CodeChange change, int row, int depth)
    {
        Change = change;
        Row = row;
        Depth = depth;
    }

    public CodeChange Change { get; }

    /// <summary>The aligned row to scroll to, or -1 when the change could not be placed.</summary>
    public int Row { get; }

    /// <summary>
    /// How deeply nested the member is, used only for the indent. From
    /// <see cref="CodeChange.Depth"/> - never counted from the path, where a namespace's own dots
    /// would make a top-level <c>using System.Collections.Generic</c> look two levels deep.
    /// </summary>
    public int Depth { get; }

    /// <summary>Indent per level of nesting, which is what makes a flat list read as a tree.</summary>
    public Avalonia.Thickness Indent => new(Depth * 12, 0, 0, 0);

    public string Name => Change.DisplayName;

    /// <summary>What happened, in the user's words - "changed and moved".</summary>
    public string Description => Change.Description;

    /// <summary>
    /// What kind of thing it is - "method", "property". Lower case, because it is read as part of a
    /// sentence rather than as a label.
    /// </summary>
    public string Kind => Change.MemberKind switch
    {
        CodeMemberKind.Import => "using",
        CodeMemberKind.EnumMember => "enum member",
        var kind => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>The type or namespace this sits in, dimmed under the name. Empty at the top level.</summary>
    public string Container => Change.Container;

    public bool HasContainer => Container.Length > 0;

    // One bool per style class: Avalonia's Classes is not bindable, so a view model exposes a flag per
    // class rather than a class name - the same arrangement JsonChangeNodeViewModel makes.

    public bool IsAdded => Change.Kind == CodeChangeKind.Added;

    public bool IsRemoved => Change.Kind == CodeChangeKind.Removed;

    public bool IsModified => Change.Kind == CodeChangeKind.Modified;

    public bool IsRenamed => Change.Kind == CodeChangeKind.Renamed;

    /// <summary>
    /// True for the two kinds that changed nothing about what the file does, which are drawn faintly.
    ///
    /// The point of the panel is to make the functional changes findable, and a reformatted method is
    /// exactly what the reader is trying to see past. It is still listed - "this file was also run
    /// through a formatter" is worth knowing - but it must not compete for attention with the two
    /// lines that actually changed.
    /// </summary>
    public bool IsPresentational => !Change.IsFunctional;
}

/// <summary>
/// The structure panel: what changed in a source file, member by member, beside the text diff.
///
/// This is the half of the structural comparison the user actually meets. What it is FOR is the
/// question a line diff cannot answer - "does any of this matter?" - which today is answered by
/// reading every hunk. A file that was reformatted, had three methods reordered and one line of logic
/// changed produces hundreds of changed lines, and the panel says: one method changed, everything
/// else moved or was rewrapped.
///
/// Deliberately a list of CHANGES rather than an outline of the file, the same choice
/// <see cref="Controls.ViewModels.JsonChangeNodeViewModel"/> makes and for the same reason: an
/// outline of a large file with four differences in it buries the four. Nesting is shown as an indent
/// rather than as an expandable tree, because every row here is already something that changed - there
/// is nothing to collapse away, and a tree would ask the reader to open five nodes to find out.
/// </summary>
public sealed partial class CodeStructureViewModel : ViewModelBase
{
    /// <summary>Every reported change, in the right-hand file's own order.</summary>
    public ObservableCollection<CodeChangeItemViewModel> Items { get; } = [];

    /// <summary>The one-sentence answer - see <see cref="CodeStructureSummary.Caption"/>.</summary>
    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    /// <summary>
    /// True when the files differ but nothing about what they DO does. The panel says so loudly,
    /// because it is the most useful thing it can ever say.
    /// </summary>
    [ObservableProperty]
    public partial bool NoFunctionalChange { get; private set; }

    /// <summary>Why there is nothing to show, when that is worth saying. Null otherwise.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>True when there is a list to draw.</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// The row the user picked. Setting it asks the host to scroll there; it is cleared by
    /// <see cref="Show"/> so the next comparison does not open with a stale selection.
    /// </summary>
    [ObservableProperty]
    public partial CodeChangeItemViewModel? SelectedItem { get; set; }

    /// <summary>Raised with an aligned row index when the user picks a member.</summary>
    public event EventHandler<int>? JumpRequested;

    partial void OnSelectedItemChanged(CodeChangeItemViewModel? value)
    {
        if (value is { Row: >= 0 } item)
        {
            JumpRequested?.Invoke(this, item.Row);
        }
    }

    /// <summary>
    /// Fills the panel from a finished comparison.
    ///
    /// Takes the <see cref="DiffResult"/> as well as the changes because the rows are resolved HERE,
    /// once, rather than on every click: a click should not scan a million-row document while the
    /// mouse is down, and the alignment cannot change underneath a result that is already built.
    /// </summary>
    public void Show(IReadOnlyList<CodeChange> changes, CodeStructureSummary summary, DiffResult result, string? skipped)
    {
        SelectedItem = null;
        Items.Clear();

        foreach (var change in changes)
        {
            Items.Add(new CodeChangeItemViewModel(change, CodeChangeRows.RowFor(result, change), change.Depth));
        }

        Caption = summary.Caption();
        NoFunctionalChange = summary.NoFunctionalChange;

        // Only said when there is nothing else to show. A skip reason under a list of changes would be
        // describing something that evidently did happen.
        Message = Items.Count > 0 ? null : skipped;

        OnPropertyChanged(nameof(HasItems));
    }

    /// <summary>Empties the panel - a new comparison, or one this does not apply to.</summary>
    public void Clear() => Show([], CodeStructureSummary.None, DiffResult.Empty, null);
}
