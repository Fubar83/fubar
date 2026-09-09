using System.Text.Json.Nodes;

namespace Fubar.Studio.Core.Snapshots;

/// <summary>Who a snapshot is for.</summary>
public enum SnapshotScope
{
    /// <summary>One environment's answer. Wins for that environment.</summary>
    Environment,

    /// <summary>Every environment without a file of its own.</summary>
    Shared,
}

/// <summary>
/// A recorded response, kept to compare later runs against.
/// </summary>
/// <remarks>
/// <para>
/// The body is stored PARSED when the response is JSON, so the committed file is a readable diff
/// rather than one enormous escaped string - the whole point of keeping these in git is that a change
/// to one shows up in review as the fields that changed.
/// </para>
/// <para>
/// Written normalised and redacted (see <see cref="SnapshotPolicy"/>), never raw: a token that reaches
/// the file has leaked into whatever the tracked history keeps, and a real timestamp in the file makes
/// every future re-record a diff nobody can read.
/// </para>
/// </remarks>
public sealed class ResponseSnapshot
{
    /// <summary>Which case of the endpoint this was recorded from, or null in the requests format.
    /// The directory already says it; the field is here so the file still says what it is after
    /// someone moves it, which is what <see cref="Environment"/> is for too.</summary>
    public string? Case { get; set; }

    /// <summary>The environment this was recorded from, or null for a shared snapshot. The file says
    /// what it is, so nothing has to infer scope from a name a merge may have given it.</summary>
    public string? Environment { get; set; }

    /// <summary>Derived from <see cref="Environment"/>, and NOT written to the file: a stored copy
    /// could contradict the field it is derived from, and then two readers of the same snapshot would
    /// disagree about who it is for.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public SnapshotScope Scope => Environment is null ? SnapshotScope.Shared : SnapshotScope.Environment;

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? RecordedBy { get; set; }

    public int Status { get; set; }

    /// <summary>Only the headers the policy asked to keep. Storing all of them makes every snapshot
    /// churn on <c>Date</c> and <c>Set-Cookie</c>.</summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>"json" or "text".</summary>
    public string BodyFormat { get; set; } = "text";

    /// <summary>The parsed body, when it was JSON.</summary>
    public JsonNode? Body { get; set; }

    /// <summary>The body as text, when it was not JSON.</summary>
    public string? BodyText { get; set; }

    /// <summary>
    /// The stamp of the request this was recorded from, so a snapshot can say it is STALE - recorded
    /// before the endpoint or case was last edited. A green run against a snapshot taken from a
    /// since-changed request is a lie, and this is what lets the tree and the run verdict say so.
    /// </summary>
    public string? RequestFingerprint { get; set; }

    /// <summary>The body as the comparer wants it: the JSON re-rendered, or the text as recorded.</summary>
    public string BodyForComparison() =>
        Body is not null ? Body.ToJsonString(SnapshotJson.Options) : BodyText ?? string.Empty;
}
