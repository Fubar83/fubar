namespace Fubar.Studio.Core.Models;

/// <summary>
/// One <c>cases/&lt;name&gt;.json</c> inside an endpoint directory: a particular way of calling the
/// endpoint.
/// </summary>
/// <remarks>
/// <para>The split is between the OPERATION and the INVOCATION. "GET {{baseUrl}}/orders/{orderId},
/// bearer auth, Accept: application/json" is the endpoint and it is true every time. "orderId
/// A-0000, and it should come back 404" is one case, and there are usually several.</para>
/// <para>Everything here is an override: an absent member means the endpoint's own value stands.
/// That is why <see cref="Body"/> is nullable while <c>RequestModel.Body</c> is not - "no body" and
/// "the endpoint's body" have to be different statements, and a non-null default would make every
/// case silently erase one.</para>
/// </remarks>
public sealed class EndpointCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public required string Name { get; set; }

    /// <summary>What this case is for, in a sentence - shown beside it wherever cases are listed.
    /// A case named "not-found" still owes the reader why.</summary>
    public string? Description { get; set; }

    /// <summary>Values for the <c>{param}</c> placeholders in the endpoint's URL.</summary>
    /// <remarks>
    /// A separate syntax from <c>{{variable}}</c> on purpose: one is the endpoint's shape and the
    /// other is the environment's values. Conflated, there is no way to say which of the two a
    /// missing value should have come from.
    /// </remarks>
    public Dictionary<string, string> PathParams { get; set; } = [];

    /// <summary>Added to the endpoint's, this level winning per key.</summary>
    public List<KeyValueItem> QueryParams { get; set; } = [];

    /// <summary>Added to the endpoint's, this level winning per key.</summary>
    public List<KeyValueItem> Headers { get; set; } = [];

    /// <summary>Null inherits the endpoint's body. A body of type <c>none</c> is a decision to send
    /// nothing, which is not the same statement.</summary>
    public RequestBody? Body { get; set; }

    /// <summary>Checks for THIS call. Stating any replaces the endpoint's, because a case usually
    /// exists to expect something else - a "not-found" case asserting 404 cannot also carry the
    /// endpoint's "status equals 200", and merging the two lists would guarantee one of them fails.</summary>
    public List<Assertion> Assertions { get; set; } = [];

    /// <summary>Captures for THIS call, replacing the endpoint's when stated - same rule, same
    /// reason as <see cref="Assertions"/>.</summary>
    public List<CaptureRule> Captures { get; set; } = [];

    /// <summary>The innermost comparison level. Null inherits the endpoint's.</summary>
    public ComparisonSettings? Comparison { get; set; }

    /// <summary>The innermost tolerance level. Null inherits the endpoint's.</summary>
    public List<Comparison.Tolerance>? Tolerances { get; set; }
}
