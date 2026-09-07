namespace Fubar.Studio.Core.Testing;

/// <summary>The outcome of evaluating one <see cref="Models.Assertion"/> against a response.</summary>
public sealed record AssertionResult(bool Passed, string Description, string? Actual);

/// <summary>The outcome of applying one <see cref="Models.CaptureRule"/>: the variable written (or the
/// reason it couldn't be).</summary>
public sealed record CaptureResult(bool Ok, string VariableName, string? Value, string Scope, string? Error)
{
    /// <summary>
    /// A note about a capture that WORKED but may not have been what the user wanted - today, one that
    /// persists something credential-shaped to a tracked file. Separate from <see cref="Error"/>
    /// precisely because the capture succeeded: it is a warning to read, not a failure to fix, and
    /// folding the two together would make a legitimate choice look broken.
    /// </summary>
    public string? Warning { get; init; }
}
