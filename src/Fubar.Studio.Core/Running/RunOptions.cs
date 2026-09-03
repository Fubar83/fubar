namespace Fubar.Studio.Core.Running;

/// <summary>
/// How a run behaves. Every default here is the conservative one for a tool that sends real requests to
/// real systems.
/// </summary>
public sealed record RunOptions
{
    public static readonly RunOptions Default = new();

    /// <summary>
    /// Stop at the first request that FAILS or ERRORS rather than carrying on. Off by default, because
    /// the usual reason to run a collection is to find out what is broken and a run that stops at the
    /// first problem answers that one request at a time.
    ///
    /// <para>Worth turning on for a chain: when request 1 is a login whose capture feeds the other
    /// nineteen, continuing past its failure produces nineteen more failures that all say the same
    /// thing and bury the one that matters.</para>
    /// </summary>
    public bool StopOnFailure { get; init; }

    /// <summary>
    /// Pause between requests. Zero by default. Exists for rate-limited APIs, where a run that is
    /// faster than the limit turns a collection of passing requests into a collection of 429s and looks
    /// like the collection is broken.
    /// </summary>
    public int DelayMilliseconds { get; init; }

    /// <summary>
    /// Write each request's per-request history entry, as an ordinary send does. OFF by default, which
    /// is the opposite of the single-send path and deliberate: history is capped per request, so a
    /// collection run on a schedule would evict the manual sends people actually go back to look for,
    /// replacing a record of what someone did with a record of what a timer did. The run's own report
    /// holds the results; history is for the things a person sent by hand.
    /// </summary>
    public bool RecordHistory { get; init; }

    /// <summary>
    /// Requests whose names contain this text (case-insensitive) are the only ones run. Null or blank
    /// runs everything. Filtering happens when the plan is built, so the report's "N of M" counts refer
    /// to what was actually attempted rather than to what was in the folder.
    /// </summary>
    public string? NameFilter { get; init; }
}
