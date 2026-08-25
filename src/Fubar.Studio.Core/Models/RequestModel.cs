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
    /// Obsolete: variables now resolve strictly from the active <see cref="WorkspaceEnvironment"/>
    /// (RequestEditorPane.md §1.3 - "Environment-Only Variables"; the request-local Variables tab
    /// is removed). Kept only so older <c>request.json</c> files still deserialize; no longer read
    /// by <c>IVariableResolver</c> or the request builder UI.
    /// </summary>
    public List<KeyValueItem> LocalVariables { get; set; } = [];

    /// <summary>
    /// Protocol-owned data bag (e.g. GraphQL query/variables/operationName, WebSocket
    /// subprotocols). Each <c>IProtocolProvider</c> owns the shape of its own entries here.
    /// </summary>
    public JsonObject? Settings { get; set; }
}
