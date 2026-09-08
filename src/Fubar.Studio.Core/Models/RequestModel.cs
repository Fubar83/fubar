using System.Text.Json.Nodes;

namespace Fubar.Studio.Core.Models;

/// <summary>
/// A single <c>request.json</c> document under a workspace's <c>collections/</c> tree. Matches
/// the (placeholder) schema at https://fubarhttp.dev/schemas/v1/request.schema.json, extended
/// with <see cref="Kind"/> and <see cref="Settings"/> so non-HTTP protocols can extend the format
/// without touching the HTTP-specific fields.
/// </summary>
public sealed class RequestModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public required string Name { get; set; }

    public RequestKind Kind { get; set; } = RequestKind.Http;

    public string Method { get; set; } = "GET";

    public string Url { get; set; } = "";

    public List<KeyValueItem> QueryParams { get; set; } = [];

    public List<KeyValueItem> Headers { get; set; } = [];

    public RequestBody Body { get; set; } = new();

    public AuthConfig Auth { get; set; } = new();

    /// <summary>Id of the <see cref="AuthProfile"/> to use when <see cref="AuthConfig.Type"/> is
    /// <see cref="AuthType.Profile"/>.</summary>
    public string? AuthProfileId { get; set; }

    /// <summary>Per-request send timeout in seconds. Null (or ≤ 0) uses the executor's default.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Post-response rules that extract a value (JSONPath / header / status) into a session or
    /// environment variable after a successful send - e.g. capturing a login token into <c>{{token}}</c>.</summary>
    public List<CaptureRule> Captures { get; set; } = [];

    /// <summary>Declarative checks run against the response after a send (status code, response time,
    /// JSONPath value, header presence) - the lightweight "tests" surfaced in the Response pane.</summary>
    public List<Assertion> Assertions { get; set; } = [];

    /// <summary>
    /// Keys of inherited headers (from a folder or the resolved auth profile) this request has
    /// toggled off - RequestEditorPane.md §5, "Toggleable Override: Developers can uncheck an
    /// inherited header to temporarily suppress it from being transmitted".
    /// </summary>
    public List<string> SuppressedInheritedHeaderKeys { get; set; } = [];

    /// <summary>
    /// SUPERSEDED by <see cref="Comparison"/>'s <c>IgnoredPaths</c>. Deserialised only so a pre-floor
    /// file still loads; <c>LegacyRequestMigration</c> folds it into <see cref="Comparison"/> once, on
    /// open, and clears it. Nothing reads it - read <see cref="Comparison"/>.
    /// </summary>
    public List<string> ResponseDiffIgnorePaths { get; set; } = [];

    /// <summary>
    /// This request's comparison overrides - the innermost level of the hierarchy, beating the folder
    /// and global levels for whichever individual settings it sets. Null means it overrides nothing.
    /// </summary>
    public ComparisonSettings? Comparison { get; set; }

    /// <summary>What to redact and normalise on the way into a snapshot, and which headers to keep.
    /// Inherited exactly like <see cref="Comparison"/>; null means this level says nothing.</summary>
    public Snapshots.SnapshotPolicy? Snapshot { get; set; }

    /// <summary>
    /// RETIRED: variables resolve from the active environment, the session store and the workspace
    /// manifest. Deserialised only so a pre-floor file still loads; <c>LegacyRequestMigration</c>
    /// drops it once, on open.
    /// </summary>
    public List<KeyValueItem> LocalVariables { get; set; } = [];

    /// <summary>
    /// Protocol-owned data bag (e.g. GraphQL query/variables/operationName, WebSocket
    /// subprotocols). Each <c>IProtocolProvider</c> owns the shape of its own entries here.
    /// </summary>
    public JsonObject? Settings { get; set; }
}
