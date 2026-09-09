using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Snapshots;

/// <summary>Serialisation for snapshot files: stable, so a re-record diffs only where it changed.</summary>
public static class SnapshotJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // Relaxed escaping, unlike the rest of this app's JSON. The default encoder escapes < > & to
        // <-style sequences, which turns "<redacted>" into "<redacted>" and any HTML in
        // a response into noise - in a file whose entire purpose is to be read in a diff. Safe here
        // because a snapshot is written to disk for review, never embedded in a page or a script.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>One rule: a JSON path, and what to put there instead.</summary>
/// <param name="Path">A path as <c>JsonPathPattern</c> understands it - <c>$.a.b</c>, <c>$..token</c>,
/// <c>$.items[*].id</c>.</param>
/// <param name="As">The replacement, e.g. <c>&lt;timestamp&gt;</c>.</param>
public sealed record SnapshotRule(string Path, string As);

/// <summary>
/// What to do to a response on its way into a snapshot.
/// </summary>
/// <remarks>
/// <para>
/// <b>Redact and normalise are different acts, and both happen on WRITE.</b> Redacting removes what
/// must not be committed - a token, an email. Normalising replaces what legitimately moves - a
/// timestamp, a generated id - with a stable placeholder, so the file does not churn.
/// </para>
/// <para>
/// Both are preferred to ignoring, which happens on READ and leaves the real value in the file: an
/// ignored field still lands in git, still changes on every re-record, and still has to be scrolled
/// past in review. Ignore only what cannot be predicted but must still be seen.
/// </para>
/// </remarks>
public sealed class SnapshotPolicy
{
    /// <summary>Replaced before the file is written, because they must never be committed at all.</summary>
    public InheritedRules? Redact { get; set; }

    /// <summary>Replaced before the file is written, because they change on every call.</summary>
    public InheritedRules? Normalize { get; set; }

    /// <summary>Response headers worth keeping. Absent means keep none.</summary>
    public List<string>? Headers { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        (Redact is null || Redact.IsEmpty)
        && (Normalize is null || Normalize.IsEmpty)
        && Headers is null;

    public SnapshotPolicy Clone() => new()
    {
        Redact = Redact?.Clone(),
        Normalize = Normalize?.Clone(),
        Headers = Headers is null ? null : [.. Headers],
    };
}

/// <summary>
/// One level's contribution to an inherited list of snapshot rules - the same add/remove shape
/// <see cref="InheritedPaths"/> uses, for the same reasons.
/// </summary>
public sealed class InheritedRules
{
    public List<SnapshotRule> Add { get; set; } = [];

    /// <summary>Paths to stop applying. By path, not by whole rule: a level saying "do not normalise
    /// $.id here" should not have to repeat what its ancestor was replacing it with.</summary>
    public List<string> Remove { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Add.Count == 0 && Remove.Count == 0;

    public InheritedRules Clone() => new() { Add = [.. Add], Remove = [.. Remove] };
}

/// <summary>
/// One snapshot rule in force, and the level that added it.
/// </summary>
/// <remarks>
/// Per rule rather than per policy, which is the point of add/remove: the Rules tab has to say
/// "inherited from folder: orders" beside a redaction the endpoint did not write, and its ✕ has to
/// know whether removing it means deleting a local addition or writing a removal at this level.
/// </remarks>
public readonly record struct ResolvedSnapshotRule(
    SnapshotRule Rule,
    Fubar.Studio.Core.Comparison.ComparisonScope Scope,
    string SourceName)
{
    public string Path => Rule.Path;

    public string As => Rule.As;
}

/// <summary>Every snapshot rule in force, with the level each came from.</summary>
public sealed record ResolvedSnapshotPolicy(
    IReadOnlyList<ResolvedSnapshotRule> Redact,
    IReadOnlyList<ResolvedSnapshotRule> Normalize,
    Fubar.Studio.Core.Comparison.Resolved<IReadOnlyList<string>> Headers)
{
    public static readonly ResolvedSnapshotPolicy Empty = new(
        [],
        [],
        new Fubar.Studio.Core.Comparison.Resolved<IReadOnlyList<string>>(
            [], Fubar.Studio.Core.Comparison.ComparisonScope.Default, "Default"));

    /// <summary>Just the rules, for the recorder, which has no use for where each came from.</summary>
    public IReadOnlyList<SnapshotRule> RedactRules => [.. Redact.Select(r => r.Rule)];

    /// <inheritdoc cref="RedactRules"/>
    public IReadOnlyList<SnapshotRule> NormalizeRules => [.. Normalize.Select(r => r.Rule)];
}
