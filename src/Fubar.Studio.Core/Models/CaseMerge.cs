using System.Text;

namespace Fubar.Studio.Core.Models;

/// <summary>
/// Folds a case onto its endpoint, producing the request that will actually be sent.
/// </summary>
/// <remarks>
/// <para>Pure and in Core, so what a case does to an endpoint is testable without a workspace on
/// disk - the same reason <c>ComparisonSettingsResolver</c> and <c>AuthApplier</c> live here.</para>
/// <para>The result is a <see cref="RequestModel"/> because everything downstream - the executor, the
/// variable resolver, the assertion runner - already speaks that. A case is a way of writing one
/// down, not a second kind of thing to send.</para>
/// </remarks>
public static class CaseMerge
{
    /// <summary>The case name a run uses when the endpoint names no default and has no cases.</summary>
    public const string ImplicitCaseName = "default";

    /// <summary>
    /// The endpoint as this case calls it. A null case returns the endpoint unchanged, which is what
    /// "run the endpoint itself" means.
    /// </summary>
    public static RequestModel Apply(RequestModel endpoint, EndpointCase? endpointCase)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (endpointCase is null)
        {
            return endpoint;
        }

        return new RequestModel
        {
            // The ENDPOINT's id, deliberately: history is per endpoint, and a case is a way of calling
            // it rather than a different thing to call.
            Id = endpoint.Id,
            Name = endpoint.Name,
            Kind = endpoint.Kind,
            Method = endpoint.Method,
            Url = FillPathParams(endpoint.Url, endpointCase.PathParams),
            QueryParams = Merge(endpoint.QueryParams, endpointCase.QueryParams),
            Headers = Merge(endpoint.Headers, endpointCase.Headers),
            Body = endpointCase.Body ?? endpoint.Body,
            Auth = endpoint.Auth,
            AuthProfileId = endpoint.AuthProfileId,
            TimeoutSeconds = endpoint.TimeoutSeconds,
            Captures = endpointCase.Captures.Count > 0 ? endpointCase.Captures : endpoint.Captures,
            Assertions = endpointCase.Assertions.Count > 0 ? endpointCase.Assertions : endpoint.Assertions,
            SuppressedInheritedHeaderKeys = endpoint.SuppressedInheritedHeaderKeys,

            // Comparison, snapshot and tolerance rules are NOT folded here. They resolve down the
            // whole chain - global, folders, endpoint, case, batch overlay - and doing half of that
            // here would give the case level two different meanings depending on who asked.
            Comparison = endpoint.Comparison,
            Snapshot = endpoint.Snapshot,
            Tolerances = endpoint.Tolerances,
            Settings = endpoint.Settings,
        };
    }

    /// <summary>
    /// Replaces <c>{param}</c> placeholders with the case's values.
    /// </summary>
    /// <remarks>
    /// A placeholder with no value is LEFT AS IT IS rather than emptied. An unresolved
    /// <c>/orders/{orderId}</c> in the URL bar says what is missing; <c>/orders/</c> is a different
    /// request that will get a plausible-looking answer from the wrong resource.
    /// </remarks>
    public static string FillPathParams(string url, IReadOnlyDictionary<string, string> pathParams)
    {
        ArgumentNullException.ThrowIfNull(pathParams);

        if (string.IsNullOrEmpty(url) || pathParams.Count == 0 || !url.Contains('{', StringComparison.Ordinal))
        {
            return url;
        }

        var result = new StringBuilder(url.Length);
        var i = 0;

        while (i < url.Length)
        {
            // {{variable}} belongs to the environment and is left for the variable resolver. Two
            // syntaxes, two owners - see EndpointCase.PathParams.
            if (url[i] == '{' && i + 1 < url.Length && url[i + 1] == '{')
            {
                var end = url.IndexOf("}}", i, StringComparison.Ordinal);
                var stop = end < 0 ? url.Length : end + 2;
                result.Append(url, i, stop - i);
                i = stop;
                continue;
            }

            if (url[i] == '{')
            {
                var close = url.IndexOf('}', i);
                if (close > i)
                {
                    var name = url[(i + 1)..close];
                    result.Append(pathParams.TryGetValue(name, out var value) ? value : url[i..(close + 1)]);
                    i = close + 1;
                    continue;
                }
            }

            result.Append(url[i]);
            i++;
        }

        return result.ToString();
    }

    /// <summary>The names of every <c>{param}</c> in a URL, in the order they appear - what an editor
    /// offers a case to fill in.</summary>
    public static IReadOnlyList<string> PathParamNames(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return [];
        }

        var names = new List<string>();
        var i = 0;

        while (i < url.Length)
        {
            if (url[i] != '{')
            {
                i++;
                continue;
            }

            if (i + 1 < url.Length && url[i + 1] == '{')
            {
                var end = url.IndexOf("}}", i, StringComparison.Ordinal);
                i = end < 0 ? url.Length : end + 2;
                continue;
            }

            var close = url.IndexOf('}', i);
            if (close < 0)
            {
                break;
            }

            var name = url[(i + 1)..close];
            if (name.Length > 0 && !names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }

            i = close + 1;
        }

        return names;
    }

    /// <summary>Union by key, the case winning - the same rule folder headers already follow.
    /// A duplicated key in either list keeps its own duplicates rather than collapsing them: a
    /// repeated query parameter is legal and sometimes meant.</summary>
    private static List<KeyValueItem> Merge(List<KeyValueItem> endpoint, List<KeyValueItem> overrides)
    {
        if (overrides.Count == 0)
        {
            return endpoint;
        }

        var overridden = new HashSet<string>(
            overrides.Select(o => o.Key), StringComparer.OrdinalIgnoreCase);

        return [.. endpoint.Where(e => !overridden.Contains(e.Key)), .. overrides];
    }
}
