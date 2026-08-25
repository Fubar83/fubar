using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>Read-only display wrapper for one <see cref="ExecutionSnapshot"/> row in the History tab
/// (RequestEditorPane.md §6).</summary>
public sealed class HistoryEntryViewModel(ExecutionSnapshot snapshot) : ViewModelBase
{
    public ExecutionSnapshot Snapshot { get; } = snapshot;

    public string TimestampText => Snapshot.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string StatusText => Snapshot.ErrorMessage is not null ? "Error" : $"{Snapshot.StatusCode} {Snapshot.ReasonPhrase}";

    public string DurationText => $"{Snapshot.ElapsedMilliseconds} ms";

    public string SizeText => $"{Snapshot.SizeBytes} B";

    /// <summary>
    /// Whether this entry can be diffed against the current response. False for an empty body, one
    /// over <c>HistoryBodyPolicy</c>'s cap, and every entry written before history stored bodies at
    /// all - so the button disables itself rather than opening an empty comparison.
    /// </summary>
    public bool CanCompare => !string.IsNullOrEmpty(Snapshot.ResponseBody);

    /// <summary>Names this side of the comparison in the diff window's header.</summary>
    public string CompareLabel => $"{StatusText} · {TimestampText}";
}
