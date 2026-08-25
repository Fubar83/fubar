namespace Fubar.Studio.Core.Testing;

/// <summary>The outcome of evaluating one <see cref="Models.Assertion"/> against a response.</summary>
public sealed record AssertionResult(bool Passed, string Description, string? Actual);

/// <summary>The outcome of applying one <see cref="Models.CaptureRule"/>: the variable written (or the
/// reason it couldn't be).</summary>
public sealed record CaptureResult(bool Ok, string VariableName, string? Value, string Scope, string? Error);
