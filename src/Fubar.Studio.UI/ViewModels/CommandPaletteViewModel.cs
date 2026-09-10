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

/// <summary>One stretch of a title, and whether the query matched it.</summary>
/// <remarks>
/// The title is split rather than marked up because Avalonia has no bindable inline collection: the
/// row draws these as a strip of <c>TextBlock</c>s, and the matched ones carry a style class.
/// </remarks>
public sealed record PaletteRun(string Text, bool IsMatch);

/// <summary>
/// One entry as the palette is currently showing it: the entry itself, plus its title cut into matched
/// and unmatched runs for the query that found it.
/// </summary>
/// <remarks>
/// <para>A row rather than the bare entry, because what is drawn depends on the QUERY and an entry
/// does not: the same "New Request" is highlighted differently under "nr" and under "req". Building
/// the runs while filtering also means the highlight is the matcher's own answer rather than a second
/// search done by the view - the characters in bold are the ones the score was earned on.</para>
/// <para><see cref="Title"/>, <see cref="Category"/> and <see cref="Gesture"/> pass straight through,
/// so a row reads like the entry it stands for.</para>
/// </remarks>
public sealed record PaletteRow(PaletteEntry Entry, IReadOnlyList<PaletteRun> Runs)
{
    public string Title => Entry.Title;

    public string Category => Entry.Category;

    public string? Gesture => Entry.Gesture;

    /// <summary>Cuts <paramref name="title"/> at <paramref name="positions"/>, merging neighbours so a
    /// run of matched characters is one bold stretch rather than four adjacent ones.</summary>
    public static PaletteRow For(PaletteEntry entry, IReadOnlyList<int> positions)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(positions);

        var title = entry.Title;
        if (positions.Count == 0 || title.Length == 0)
        {
            return new PaletteRow(entry, [new PaletteRun(title, false)]);
        }

        var matched = new HashSet<int>(positions);
        var runs = new List<PaletteRun>();
        var start = 0;

        for (var i = 1; i <= title.Length; i++)
        {
            if (i < title.Length && matched.Contains(i) == matched.Contains(start))
            {
                continue;
            }

            runs.Add(new PaletteRun(title[start..i], matched.Contains(start)));
            start = i;
        }

        return new PaletteRow(entry, runs);
    }
}

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
        Results = [.. _entries.Select(e => PaletteRow.For(e, []))];
        Selected = Results.FirstOrDefault();
    }

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    public ObservableCollection<PaletteRow> Results { get; }

    [ObservableProperty]
    public partial PaletteRow? Selected { get; set; }

    /// <summary>Raised when an entry has been chosen, so the host can close the window.</summary>
    public event Action? Accepted;

    partial void OnQueryChanged(string value)
    {
        var ranked = _entries
            .Select(entry => (Entry: entry, Match: FuzzyMatch.Match(entry.Title, value)))
            .Where(x => x.Match is not null)
            // Descending by score, then by title so the order is stable rather than dependent on the
            // order the entries happened to be built in.
            .OrderByDescending(x => x.Match!.Value.Score)
            .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => PaletteRow.For(x.Entry, x.Match!.Value.Positions))
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
        if (Selected is not { Entry: { } entry })
        {
            return;
        }

        // Closed BEFORE the action runs: several entries open a dialog of their own, and a palette
        // still on screen behind a modal is a window the user cannot get rid of.
        Accepted?.Invoke();
        await entry.Invoke();
    }
}
