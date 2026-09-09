using Fubar.Studio.Core.Comparison;

namespace Fubar.Studio.Core.Models;

/// <summary>What judges a batch's responses.</summary>
public enum BatchOracleKind
{
    /// <summary>Assertions only. What a plain collection run has always done.</summary>
    None,

    /// <summary>Compare against the recorded snapshot for each step.</summary>
    Snapshot,

    /// <summary>Send twice and compare the two environments' answers.</summary>
    Environment,
}

/// <param name="Environment">The second environment, for <see cref="BatchOracleKind.Environment"/>.
/// Ignored by the others.</param>
public sealed record BatchOracle(BatchOracleKind Kind, string? Environment = null);

/// <summary>
/// One step of a batch: which endpoint, and which of its cases.
/// </summary>
/// <param name="Endpoint">A path relative to <c>collections/</c> - <c>orders/get-order</c>. A batch
/// survives a rename only if the file moves with it, exactly like every other cross-file reference in
/// this format.</param>
/// <param name="Case">One case name, or null to run every case the endpoint has.</param>
public sealed record BatchStep(string Endpoint, string? Case = null);

/// <summary>
/// The occasion's own rules, applied after the containment chain resolves.
/// </summary>
/// <remarks>
/// A batch is NOT a sixth level of the hierarchy. It cuts across the tree - the same endpoint appears
/// in a smoke batch and in a nightly one - so folding it into the chain would make an endpoint's
/// effective settings depend on which list happened to name it. An overlay is applied last and cannot
/// introduce a setting the chain does not already know about.
/// </remarks>
public sealed class BatchOverlay
{
    public ComparisonSettings? Comparison { get; set; }

    public List<Tolerance>? Tolerances { get; set; }

    public bool IsEmpty => Comparison is null && Tolerances is null;
}

/// <summary>How a batch runs. The subset of <c>RunOptions</c> that is a property of the batch rather
/// than of the person starting it.</summary>
public sealed class BatchOptions
{
    public bool StopOnFailure { get; set; }

    public int DelayMs { get; set; }
}

/// <summary>
/// One <c>batches/&lt;name&gt;.json</c>: a named list of calls to make together, with what should judge
/// them.
/// </summary>
/// <remarks>
/// <para>A batch is an OCCASION - "the smoke test", "the nightly drift check" - and that is why it is
/// stored beside the collection rather than inside it. The tree says what exists; a batch says what to
/// do on a particular occasion, and the two are different lists that change for different reasons.</para>
/// <para>Batches do not nest. A batch of batches is a scheduler, and that is a different tool.</para>
/// </remarks>
public sealed class Batch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>In the order they will be sent. The order is the batch's, not the tree's: a batch that
    /// starts with a login and then calls three endpoints is stating a dependency, and sorting it
    /// would break exactly the batches worth having.</summary>
    public List<BatchStep> Steps { get; set; } = [];

    public BatchOracle? Oracle { get; set; }

    /// <summary>One name for most oracles, two for an environment comparison. Empty means "whatever
    /// the caller chose", which is what makes a batch usable from the UI's environment selector as
    /// well as from CI.</summary>
    public List<string> Environments { get; set; } = [];

    public BatchOptions? Options { get; set; }

    public BatchOverlay? Overlay { get; set; }
}
