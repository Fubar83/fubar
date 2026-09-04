using Fubar.Studio.Core.Diagnostics;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The strip is collapsed by default and Ctrl+` used to be the only way into it, so everything that
/// went wrong - auth failures, capture failures, import FAILURES - reported somewhere nobody could
/// see. These pin the two things that fixed: it raises itself, and it counts what was missed.
/// </summary>
public class StatusLogTests
{
    private static StatusLogViewModel NewLog(ILogSink? sink = null) => new(sink, clipboard: null);

    [Fact]
    public void An_error_asks_the_shell_to_raise_the_strip()
    {
        var log = NewLog();
        var raised = 0;
        log.RaiseRequested += () => raised++;

        log.LogError("Postman import failed: not a collection.");

        Assert.Equal(1, raised);
    }

    [Fact]
    public void An_ordinary_message_does_not_interrupt()
    {
        var log = NewLog();
        var raised = 0;
        log.RaiseRequested += () => raised++;

        log.Log("Fubar shell ready.");

        Assert.Equal(0, raised);
        Assert.Equal(0, log.AttentionCount);
    }

    [Fact]
    public void Unread_warnings_are_counted_while_the_strip_is_closed()
    {
        var log = NewLog();

        log.LogWarning("one");
        log.LogWarning("two");

        Assert.Equal(2, log.AttentionCount);
    }

    /// <summary>The badge clears when the user actually looks, not when something merely tried to
    /// raise it - otherwise a warning arriving during a raise would be counted as read.</summary>
    [Fact]
    public void Opening_the_strip_clears_the_badge()
    {
        var log = NewLog();
        log.LogWarning("something");

        log.IsVisible = true;

        Assert.Equal(0, log.AttentionCount);
    }

    [Fact]
    public void Nothing_is_counted_while_the_strip_is_open()
    {
        var log = NewLog();
        log.IsVisible = true;

        log.LogWarning("visible already");

        Assert.Equal(0, log.AttentionCount);
    }

    [Fact]
    public void The_filter_hides_entries_below_the_chosen_level()
    {
        var log = NewLog();
        log.Log("chatter");
        log.LogWarning("look at this");
        log.LogError("this failed");

        log.MinimumLevel = LogLevel.Warning;

        Assert.Equal(2, log.VisibleEntries.Count());
        Assert.DoesNotContain(log.VisibleEntries, e => e.Message == "chatter");
    }

    [Fact]
    public void Entries_reach_the_sink_so_they_outlive_the_session()
    {
        var sink = new RecordingSink();
        var log = NewLog(sink);

        log.LogError("gone by morning otherwise");

        var (level, message) = Assert.Single(sink.Written);
        Assert.Equal(LogLevel.Error, level);
        Assert.Equal("gone by morning otherwise", message);
    }

    private sealed class RecordingSink : ILogSink
    {
        public List<(LogLevel Level, string Message)> Written { get; } = [];

        public string? Location => "memory";

        public void Write(DateTimeOffset timestamp, LogLevel level, string message) =>
            Written.Add((level, message));
    }
}
