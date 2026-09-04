using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Diagnostics;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One line in the Status &amp; Log strip.</summary>
/// <param name="Timestamp">When it happened, kept as a real instant rather than pre-formatted text so
/// the file sink can write UTC while the strip shows local time.</param>
public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message)
{
    public string Display => $"{Timestamp.LocalDateTime:HH:mm:ss}  {Message}";

    public bool IsWarning => Level == LogLevel.Warning;

    public bool IsError => Level == LogLevel.Error;
}

/// <summary>
/// Backs the collapsible Status &amp; Log strip.
///
/// <para>Everything that goes wrong in this application reports here: discarded edits, auth failures,
/// capture failures, history write failures, import warnings and import FAILURES. The strip is
/// collapsed by default and <c>Ctrl+`</c> was the only way to open it, so all of that was effectively
/// invisible - which made every other quality improvement invisible too. Hence
/// <see cref="AttentionCount"/> (a badge the shell shows whatever the strip is doing) and
/// <see cref="RaiseRequested"/>, which asks the shell to open the strip the first time something
/// actually fails.</para>
/// </summary>
public partial class StatusLogViewModel : ViewModelBase
{
    /// <summary>Entries kept on screen. Bounded, because a long session with a chatty importer would
    /// otherwise grow without limit; the file sink is the unbounded record.</summary>
    private const int MaxEntries = 2000;

    private readonly ILogSink? _sink;
    private readonly IClipboardService? _clipboard;

    /// <summary>
    /// One constructor with optional dependencies rather than two overloads: this type IS resolved from
    /// DI, and with two public constructors the container picks the greediest resolvable one - which
    /// works until a registration changes and it silently starts picking the other.
    /// </summary>
    public StatusLogViewModel(ILogSink? sink = null, IClipboardService? clipboard = null)
    {
        _sink = sink;
        _clipboard = clipboard;
    }

    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <summary>Raised the first time a Warning or Error arrives while the strip is closed. The shell
    /// opens it; a failure reported into a panel nobody can see is not reported.</summary>
    public event Action? RaiseRequested;

    /// <summary>Whether the strip is currently on screen. Set by the shell, and read here so the badge
    /// clears when the user actually looks at it.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Unread warnings and errors, for the shell's badge.</summary>
    [ObservableProperty]
    public partial int AttentionCount { get; set; }

    /// <summary>Hide everything below the selected level. Info is the default, i.e. show everything.</summary>
    [ObservableProperty]
    public partial LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>Where the entries are also being written, or a note that they are not.</summary>
    public string LogFileLocation => _sink?.Location ?? "not being written to a file";

    public static IReadOnlyList<LogLevel> LevelOptions { get; } = Enum.GetValues<LogLevel>();

    public IEnumerable<LogEntry> VisibleEntries => Entries.Where(e => e.Level >= MinimumLevel);

    public void Log(string message) => Log(message, LogLevel.Info);

    public void Log(string message, LogLevel level)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);

        Entries.Insert(0, entry);
        if (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        _sink?.Write(entry.Timestamp, level, message);
        OnPropertyChanged(nameof(VisibleEntries));

        if (level == LogLevel.Info)
        {
            return;
        }

        if (IsVisible)
        {
            return;
        }

        AttentionCount++;
        RaiseRequested?.Invoke();
    }

    /// <summary>Convenience for the commonest two, so call sites read as what they mean.</summary>
    public void LogWarning(string message) => Log(message, LogLevel.Warning);

    public void LogError(string message) => Log(message, LogLevel.Error);

    partial void OnIsVisibleChanged(bool value)
    {
        if (value)
        {
            AttentionCount = 0;
        }
    }

    partial void OnMinimumLevelChanged(LogLevel value) => OnPropertyChanged(nameof(VisibleEntries));

    /// <summary>
    /// Copies what is on screen, oldest first - reading order for someone pasting it into an issue,
    /// which is the opposite of the newest-first order the strip shows.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task CopyAllAsync()
    {
        if (_clipboard is null)
        {
            return;
        }

        var text = string.Join(
            System.Environment.NewLine,
            VisibleEntries
                .Reverse()
                .Select(e => $"{e.Timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z  {e.Level.ToString().ToUpperInvariant(),-7}  {e.Message}"));

        await _clipboard.SetTextAsync(text);
    }

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        AttentionCount = 0;
        OnPropertyChanged(nameof(VisibleEntries));
    }
}
