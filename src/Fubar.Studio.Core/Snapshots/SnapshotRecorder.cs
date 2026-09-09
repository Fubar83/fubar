using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fubar.Studio.Core.Snapshots;

/// <summary>
/// Turns a response into the file that will be committed: redacted, normalised, key-sorted.
/// </summary>
/// <remarks>
/// The order is fixed and matters. <b>Redact first</b>, so a secret is gone before any other rule can
/// copy it somewhere; then normalise; then sort. Sorting last means the output is stable whatever
/// order the service returned its properties in, which is what stops a re-record diffing everywhere.
/// </remarks>
public static class SnapshotRecorder
{
    public const string RedactedPlaceholder = "<redacted>";

    /// <summary>What a recording did, so the UI can show it before anything is written.</summary>
    /// <param name="RedactionsApplied">How many values were removed. Shown, because a policy that
    /// matched nothing is the case worth noticing before committing.</param>
    /// <param name="UnsupportedPaths">Rules whose path this could not parse. Reported rather than
    /// dropped: a redaction that silently did nothing is the worst possible failure here.</param>
    public sealed record Recording(
        ResponseSnapshot Snapshot,
        int RedactionsApplied,
        int NormalisationsApplied,
        IReadOnlyList<string> UnsupportedPaths);

    public static Recording Record(
        string body,
        int status,
        IReadOnlyDictionary<string, string> headers,
        string? environment,
        ResolvedSnapshotPolicy policy,
        string? endpointCase = null,
        string? requestFingerprint = null,
        string? recordedBy = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(policy);

        var snapshot = new ResponseSnapshot
        {
            Case = endpointCase,
            Environment = environment,
            Status = status,
            RequestFingerprint = requestFingerprint,
            RecordedBy = recordedBy,
            Headers = Keep(headers, policy.Headers),
        };

        var unsupported = new List<string>();
        var redactions = 0;
        var normalisations = 0;

        if (TryParseJson(body, out var node))
        {
            redactions = ApplyAll(node, policy.Redact, unsupported);
            normalisations = ApplyAll(node, policy.Normalize, unsupported);

            snapshot.BodyFormat = "json";
            snapshot.Body = Sort(node);
        }
        else
        {
            // Not JSON, so there is nothing to walk. Rules are reported as unsupported HERE rather
            // than silently skipped: a redaction rule on a response that turned out to be text is
            // exactly the case where someone believes a secret was removed and it was not.
            foreach (var rule in policy.Redact)
            {
                unsupported.Add(rule.Path);
            }

            snapshot.BodyFormat = "text";
            snapshot.BodyText = body;
        }

        return new Recording(snapshot, redactions, normalisations, unsupported);
    }

    private static int ApplyAll(JsonNode? node, IReadOnlyList<SnapshotRule> rules, List<string> unsupported)
    {
        var applied = 0;
        foreach (var rule in rules)
        {
            if (!JsonPathRewriter.IsSupported(rule.Path))
            {
                unsupported.Add(rule.Path);
                continue;
            }

            applied += JsonPathRewriter.Apply(node, rule.Path, rule.As);
        }

        return applied;
    }

    private static Dictionary<string, string> Keep(
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyList<string> wanted)
    {
        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in wanted)
        {
            if (headers.TryGetValue(name, out var value))
            {
                kept[name] = value;
            }
        }

        return kept;
    }

    private static bool TryParseJson(string body, out JsonNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            node = JsonNode.Parse(body);
            return node is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Object keys sorted ordinal; array order left alone.
    ///
    /// <para>Arrays keep their order because it is data - reordering one would hide a difference that
    /// matters. Object keys are not: JSON objects are unordered, the comparer already treats them so,
    /// and sorting is what makes two recordings of the same answer produce the same file.</para>
    /// </summary>
    private static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    sorted[property.Key] = Sort(property.Value?.DeepClone());
                }

                return sorted;

            case JsonArray array:
                var result = new JsonArray();
                foreach (var element in array)
                {
                    result.Add(Sort(element?.DeepClone()));
                }

                return result;

            default:
                return node?.DeepClone();
        }
    }
}
