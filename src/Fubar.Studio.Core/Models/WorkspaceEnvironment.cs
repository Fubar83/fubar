namespace Fubar.Studio.Core.Models;

/// <summary>
/// A named environment variable set stored at <c>environments/*.json</c> under a workspace root
/// (e.g. "Staging", "Production"). <c>{{key}}</c> tokens resolve strictly against whichever
/// environment is active (RequestEditorPane.md §1.3 - "Environment-Only Variables") via
/// <c>IVariableResolver</c>. Named <c>WorkspaceEnvironment</c> rather than <c>Environment</c> to
/// avoid colliding with <see cref="System.Environment"/>.
/// </summary>
public sealed class WorkspaceEnvironment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public required string Name { get; set; }

    public List<AppVariable> Variables { get; set; } = [];

    /// <summary>
    /// How connections for this environment are made - client certificate, extra CAs, proxy. Null (the
    /// common case) means the machine defaults, which is what the app has always done.
    /// </summary>
    public TransportSettings? Transport { get; set; }
}
