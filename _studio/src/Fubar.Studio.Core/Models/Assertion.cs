namespace Fubar.Studio.Core.Models;

/// <summary>The comparison a single <see cref="Assertion"/> applies to the value it reads.</summary>
public enum AssertionOperator
{
    Equals,
    NotEquals,
    Contains,
    Exists,
    NotExists,
    LessThan,
    GreaterThan,
}

/// <summary>
/// A declarative response check. Reads a value from the response (<see cref="Source"/> + optional
/// <see cref="Target"/> JSONPath/header name), compares it to <see cref="Expected"/> with
/// <see cref="Operator"/>, and passes or fails. Kept intentionally scriptless - no JS engine - so it
/// reuses the app's existing JSONPath support and stays trivially serializable.
/// </summary>
public sealed class Assertion
{
    public bool Enabled { get; set; } = true;

    public ResponseField Source { get; set; } = ResponseField.StatusCode;

    /// <summary>JSONPath (for <see cref="ResponseField.JsonBody"/>) or header name (for
    /// <see cref="ResponseField.Header"/>). Ignored for status/time.</summary>
    public string Target { get; set; } = "";

    public AssertionOperator Operator { get; set; } = AssertionOperator.Equals;

    /// <summary>The expected value (compared as text, or as a number for the numeric operators). Unused
    /// for <see cref="AssertionOperator.Exists"/> / <see cref="AssertionOperator.NotExists"/>.</summary>
    public string Expected { get; set; } = "";
}
