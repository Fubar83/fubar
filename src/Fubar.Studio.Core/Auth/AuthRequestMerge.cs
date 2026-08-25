using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// Injects an <see cref="AppliedAuth"/> into a request just before it's sent. Returns a shallow clone with
/// the auth headers/query params added - only for keys the request doesn't already carry as an enabled
/// entry, so an explicit user header/param always wins. The clone is used for execution only; the caller
/// keeps the original (auth-free) request for history, so resolved tokens never get persisted.
/// </summary>
public static class AuthRequestMerge
{
    public static RequestModel Inject(RequestModel request, AppliedAuth applied)
    {
        if (applied.IsEmpty)
        {
            return request;
        }

        var headers = new List<KeyValueItem>(request.Headers);
        foreach (var h in applied.Headers)
        {
            if (!headers.Any(x => x.Enabled && string.Equals(x.Key, h.Key, StringComparison.OrdinalIgnoreCase)))
            {
                headers.Add(new KeyValueItem { Key = h.Key, Value = h.Value, Enabled = true });
            }
        }

        var query = new List<KeyValueItem>(request.QueryParams);
        foreach (var q in applied.QueryParams)
        {
            if (!query.Any(x => x.Enabled && string.Equals(x.Key, q.Key, StringComparison.OrdinalIgnoreCase)))
            {
                query.Add(new KeyValueItem { Key = q.Key, Value = q.Value, Enabled = true });
            }
        }

        return new RequestModel
        {
            Id = request.Id,
            Name = request.Name,
            Kind = request.Kind,
            Method = request.Method,
            Url = request.Url,
            QueryParams = query,
            Headers = headers,
            Body = request.Body,
            Auth = request.Auth,
            AuthProfileId = request.AuthProfileId,
            TimeoutSeconds = request.TimeoutSeconds,
            Captures = request.Captures,
            Assertions = request.Assertions,
            SuppressedInheritedHeaderKeys = request.SuppressedInheritedHeaderKeys,
            LocalVariables = request.LocalVariables,
            Settings = request.Settings,
        };
    }
}
