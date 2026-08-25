namespace Fubar.Studio.Core.Models;

/// <summary>Where a <see cref="CaptureRule"/> / <see cref="Assertion"/> reads its value from.</summary>
public enum ResponseField
{
    /// <summary>A JSONPath expression evaluated against the (JSON) response body.</summary>
    JsonBody,

    /// <summary>A named response header.</summary>
    Header,

    /// <summary>The numeric HTTP status code.</summary>
    StatusCode,

    /// <summary>The elapsed response time, in milliseconds.</summary>
    ResponseTimeMs,
}

/// <summary>Whether a captured value is stored in the in-memory session store (not persisted) or written
/// to the active environment (persisted to its file).</summary>
public enum CaptureScope
{
    Session,
    Environment,
}

/// <summary>
/// A post-response extraction rule: pull a value out of the response (a JSONPath match, a header, or the
/// status/time) and store it in a variable so later requests can reference <c>{{name}}</c>. The headline
/// use case is capturing an auth token from a login response.
/// </summary>
public sealed class CaptureRule
{
    public bool Enabled { get; set; } = true;

    /// <summary>The variable name to write (without braces).</summary>
    public string VariableName { get; set; } = "";

    public ResponseField Source { get; set; } = ResponseField.JsonBody;

    /// <summary>JSONPath (for <see cref="ResponseField.JsonBody"/>) or header name (for
    /// <see cref="ResponseField.Header"/>). Ignored for status/time.</summary>
    public string Expression { get; set; } = "";

    public CaptureScope Scope { get; set; } = CaptureScope.Session;
}
