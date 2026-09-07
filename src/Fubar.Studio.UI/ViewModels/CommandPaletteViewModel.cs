using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// One thing the palette can do.
/// </summary>
/// <param name="Title">What the user reads and types against.</param>
/// <param name="Category">Where it came from - "Command", or the workspace a request belongs to.</param>
/// <param name="Gesture">The keyboard shortcut, shown beside the entry so the palette teaches the
/// shortcuts as it is used. Null when there is none.</param>
/// <param name="Invoke">What running it does.</param>
public sealed record PaletteEntry(string Title, string Category, string? Gesture, Func<Task> Invoke);

/// <summary>
/// The command palette.
///
/// <para>Worth more in this application than in most: there is no menu bar, so a palette is
/// simultaneously the shortcut surface and the ONLY discovery surface. Everything the app can do was
/// previously reachable only from a right-click menu, a toolbar flyout, or a keyboard gesture written
/// down nowhere. Each entry shows its gesture, so using the palette teaches the shortcut that would
/// have avoided it.</para>
/// </summary>
public partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly List<PaletteEntry> _entries;

    public CommandPaletteViewModel(IEnumerable<PaletteEntry> entries)
    {
        _entries = [.. entries];
        Results = [.. _entries];
        Selected = Results.FirstOrDefault();
    }

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    public ObservableCollection<PaletteEntry> Results { get; }

    [ObservableProperty]
    public partial PaletteEntry? Selected { get; set; }

    /// <summary>Raised when an entry has been chosen, so the host can close the window.</summary>
    public event Action? Accepted;

    partial void OnQueryChanged(string value)
    {
        var ranked = _entries
            .Select(entry => (Entry: entry, Score: FuzzyMatch.Score(entry.Title, value)))
            .Where(x => x.Score is not null)
            // Descending by score, then by title so the order is stable rather than dependent on the
            // order the entries happened to be built in.
            .OrderByDescending(x => x.Score!.Value)
            .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Entry)
            .ToList();

        Results.Clear();
        foreach (var entry in ranked)
        {
            Results.Add(entry);
        }

        // Keeps the selection on something real. Without this, typing until nothing matches and then
        // pressing Return would run whatever was selected before - which is the one thing a palette
        // must never do.
        Selected = Results.FirstOrDefault();
    }

    [RelayCommand]
    private void MoveDown() => Move(1);

    [RelayCommand]
    private void MoveUp() => Move(-1);

    private void Move(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        // Wraps, because a list navigated by keyboard that stops dead at the end feels broken.
        Selected = Results[(index + delta + Results.Count) % Results.Count];
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (Selected is not { } entry)
        {
            return;
        }

        // Closed BEFORE the action runs: several entries open a dialog of their own, and a palette
        // still on screen behind a modal is a window the user cannot get rid of.
        Accepted?.Invoke();
        await entry.Invoke();
    }
}
