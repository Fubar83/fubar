using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>
/// The headers a request actually puts on the wire: what its folders hand down, then its own.
/// </summary>
/// <remarks>
/// <para>Pure and in Core for the same reason <see cref="Auth.EffectiveAuthResolver"/> is - and
/// because the answer must not depend on WHO is sending. It did: the request editor layered the
/// inheritance chain over the request's own headers before every Send, and the runner sent the file
/// exactly as it stood. So a folder's <c>Accept</c> or <c>X-Api-Key</c> went out from the editor and
/// was missing from every collection run, every snapshot, and both sides of an environment
/// comparison - where a missing header is worse than anywhere else, because two systems answering a
/// differently-shaped request disagree for a reason that has nothing to do with the systems.</para>
/// <para>Auth is deliberately NOT folded in here. The execution pipeline acquires the credential and
/// injects it itself (<c>AuthRequestMerge</c>); a copy added here would be the stale one, and it
/// would also make <c>AuthRequestMerge</c> - which refuses to overwrite a header the request already
/// carries enabled - skip the real thing.</para>
/// </remarks>
public static class EffectiveHeaders
{
    /// <summary>
    /// Inherited headers first, root-most folder first, then the request's own - the order
    /// <see cref="IInheritanceResolver.GetInheritanceChainAsync"/> produces and the order the Headers
    /// tab lists.
    /// </summary>
    /// <remarks>
    /// <para>Layered, NOT deduplicated by key. A repeated header is legal and sometimes meant, and
    /// <c>HttpRequestExecutor</c> adds them in this order, so a closer folder's value and then the
    /// request's own arrive last - which is what "closest wins" amounts to on the wire for the
    /// headers that take a single value. Collapsing them here would silently drop the second
    /// <c>Accept</c> of a request that meant to send both.</para>
    /// <para>Disabled at either level means not sent: a folder header whose box is unchecked is not
    /// something to inherit, and a key in <paramref name="suppressedKeys"/> is a request saying it
    /// does not want what its folder offers.</para>
    /// </remarks>
    public static List<KeyValueItem> Resolve(
        IEnumerable<InheritedHeader> inherited,
        IEnumerable<KeyValueItem> direct,
        IEnumerable<string> suppressedKeys)
    {
        ArgumentNullException.ThrowIfNull(inherited);
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(suppressedKeys);

        // Header names are case-insensitive, so a suppression written as "accept" has to still
        // suppress "Accept" - the folder and the request are edited in different windows, and
        // matching them ordinally would leave a toggled-off header going out anyway.
        var suppressed = new HashSet<string>(suppressedKeys, StringComparer.OrdinalIgnoreCase);

        var headers = new List<KeyValueItem>();

        foreach (var header in inherited)
        {
            if (Sendable(header.Item) && !suppressed.Contains(header.Item.Key))
            {
                headers.Add(Copy(header.Item));
            }
        }

        foreach (var item in direct)
        {
            if (Sendable(item))
            {
                headers.Add(Copy(item));
            }
        }

        return headers;
    }

    /// <summary>
    /// The request as it will be sent from this point in the tree - a shallow clone carrying the
    /// resolved headers.
    /// </summary>
    /// <remarks>
    /// A clone rather than a mutation: the caller's model is what history and the editor still hold,
    /// and folding an ancestor's headers into it would persist them into the request's own file the
    /// next time it was saved.
    /// </remarks>
    public static RequestModel Apply(RequestModel request, InheritanceChain? chain)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (chain is null || chain.Headers.Count == 0)
        {
            return request;
        }

        return new RequestModel
        {
            Id = request.Id,
            Name = request.Name,
            Kind = request.Kind,
            Method = request.Method,
            Url = request.Url,
            QueryParams = request.QueryParams,
            Headers = Resolve(chain.Headers, request.Headers, request.SuppressedInheritedHeaderKeys),
            Body = request.Body,
            Auth = request.Auth,
            AuthProfileId = request.AuthProfileId,
            TimeoutSeconds = request.TimeoutSeconds,
            Captures = request.Captures,
            Assertions = request.Assertions,
            SuppressedInheritedHeaderKeys = request.SuppressedInheritedHeaderKeys,
            Comparison = request.Comparison,
            Snapshot = request.Snapshot,
            Tolerances = request.Tolerances,
            LocalVariables = request.LocalVariables,
            Settings = request.Settings,
        };
    }

    private static bool Sendable(KeyValueItem item) =>
        item.Enabled && !string.IsNullOrWhiteSpace(item.Key);

    private static KeyValueItem Copy(KeyValueItem item) =>
        new() { Key = item.Key, Value = item.Value, Enabled = true, Description = item.Description };
}
