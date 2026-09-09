using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Snapshots;

namespace Fubar.Studio.Core.Tests.Snapshots;

/// <summary>
/// What a snapshot file ends up containing. Two properties matter more than the rest: a redacted value
/// never reaches it, and recording the same answer twice produces the same bytes.
/// </summary>
public class SnapshotRecorderTests
{
    private static readonly Dictionary<string, string> NoHeaders = [];

    private static SnapshotRecorder.Recording Record(
        string body,
        ResolvedSnapshotPolicy? policy = null,
        IReadOnlyDictionary<string, string>? headers = null) =>
        SnapshotRecorder.Record(
            body, 200, headers ?? NoHeaders, "staging", policy ?? ResolvedSnapshotPolicy.Empty);

    /// <summary>A policy as if the request itself had written every rule. The recorder does not read
    /// provenance - only the Rules tab does - so these tests state the rules and nothing else.</summary>
    private static ResolvedSnapshotPolicy Policy(
        SnapshotRule[]? redact = null,
        SnapshotRule[]? normalize = null,
        string[]? headers = null) =>
        new(
            [.. (redact ?? []).Select(From)],
            [.. (normalize ?? []).Select(From)],
            new Resolved<IReadOnlyList<string>>(headers ?? [], ComparisonScope.Request, "Request"));

    private static ResolvedSnapshotRule From(SnapshotRule rule) =>
        new(rule, ComparisonScope.Request, "Request");

    // ---- Redaction: a security test, not a formatting one ----------------------------------------

    [Fact]
    public void A_redacted_value_never_reaches_the_file()
    {
        var recording = Record(
            """{"user":"ada","token":"eyJhbGciOiJIUzI1NiJ9.secret"}""",
            Policy(redact: [new SnapshotRule("$.token", "<redacted>")]));

        var written = recording.Snapshot.BodyForComparison();

        Assert.DoesNotContain("secret", written, StringComparison.Ordinal);
        Assert.Contains("<redacted>", written, StringComparison.Ordinal);
        Assert.Equal(1, recording.RedactionsApplied);
    }

    [Fact]
    public void Redaction_reaches_any_depth_with_a_recursive_path()
    {
        var recording = Record(
            """{"a":{"b":{"token":"sh"}},"list":[{"token":"hh"}]}""",
            Policy(redact: [new SnapshotRule("$..token", "<redacted>")]));

        var written = recording.Snapshot.BodyForComparison();

        Assert.DoesNotContain("sh", written, StringComparison.Ordinal);
        Assert.DoesNotContain("hh", written, StringComparison.Ordinal);
        Assert.Equal(2, recording.RedactionsApplied);
    }

    /// <summary>
    /// The worst failure this could have: someone writes a redaction rule, the response comes back as
    /// text, the rule silently does nothing and the secret is committed. It is reported instead.
    /// </summary>
    [Fact]
    public void A_redaction_rule_on_a_non_json_response_is_reported_not_silently_skipped()
    {
        var recording = Record("plain text, token=abc", Policy(redact: [new SnapshotRule("$.token", "<redacted>")]));

        Assert.Contains("$.token", recording.UnsupportedPaths);
    }

    [Fact]
    public void A_path_that_cannot_be_parsed_is_reported_rather_than_ignored()
    {
        var recording = Record("""{"a":1}""", Policy(redact: [new SnapshotRule("not-a-path", "<redacted>")]));

        Assert.Contains("not-a-path", recording.UnsupportedPaths);
    }

    // ---- Normalisation ---------------------------------------------------------------------------

    [Fact]
    public void Normalising_replaces_a_volatile_value_with_a_stable_one()
    {
        var first = Record(
            """{"generatedAt":"2026-01-01T00:00:00Z","total":10}""",
            Policy(normalize: [new SnapshotRule("$.generatedAt", "<timestamp>")]));

        var second = Record(
            """{"generatedAt":"2026-09-09T11:22:33Z","total":10}""",
            Policy(normalize: [new SnapshotRule("$.generatedAt", "<timestamp>")]));

        Assert.Equal(first.Snapshot.BodyForComparison(), second.Snapshot.BodyForComparison());
    }

    [Fact]
    public void Every_element_of_an_array_can_be_normalised()
    {
        var recording = Record(
            """{"items":[{"id":"a","n":1},{"id":"b","n":2}]}""",
            Policy(normalize: [new SnapshotRule("$.items[*].id", "<id>")]));

        Assert.Equal(2, recording.NormalisationsApplied);
        Assert.DoesNotContain("\"a\"", recording.Snapshot.BodyForComparison(), StringComparison.Ordinal);
    }

    // ---- Stability -------------------------------------------------------------------------------

    /// <summary>Without this every re-record is an unreviewable diff, and the file stops being worth
    /// committing.</summary>
    [Fact]
    public void Object_keys_are_sorted_so_the_same_answer_writes_the_same_file()
    {
        var one = Record("""{"b":2,"a":1,"c":{"z":1,"y":2}}""");
        var two = Record("""{"a":1,"c":{"y":2,"z":1},"b":2}""");

        Assert.Equal(one.Snapshot.BodyForComparison(), two.Snapshot.BodyForComparison());
    }

    /// <summary>Array order is DATA. Sorting it would hide a difference that matters.</summary>
    [Fact]
    public void Array_order_is_left_alone()
    {
        var recording = Record("""{"xs":[3,1,2]}""");

        Assert.Contains("3", recording.Snapshot.BodyForComparison(), StringComparison.Ordinal);
        Assert.Equal(
            """{"xs":[3,1,2]}""".Replace(" ", "", StringComparison.Ordinal),
            recording.Snapshot.BodyForComparison().Replace(" ", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal));
    }

    // ---- Headers and shape -----------------------------------------------------------------------

    [Fact]
    public void Only_the_headers_the_policy_named_are_kept()
    {
        var recording = Record(
            """{"a":1}""",
            Policy(headers: ["Content-Type"]),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "application/json",
                ["Date"] = "Tue, 09 Sep 2026 11:00:00 GMT",
                ["Set-Cookie"] = "session=abc",
            });

        Assert.Equal(["Content-Type"], recording.Snapshot.Headers.Keys);
    }

    [Fact]
    public void A_json_body_is_stored_parsed_and_text_is_stored_as_text()
    {
        Assert.Equal("json", Record("""{"a":1}""").Snapshot.BodyFormat);
        Assert.Equal("text", Record("not json").Snapshot.BodyFormat);
        Assert.Equal("not json", Record("not json").Snapshot.BodyText);
    }

    [Fact]
    public void A_shared_snapshot_has_no_environment()
    {
        var recording = SnapshotRecorder.Record(
            """{"a":1}""", 200, NoHeaders, environment: null, ResolvedSnapshotPolicy.Empty);

        Assert.Equal(SnapshotScope.Shared, recording.Snapshot.Scope);
        Assert.Null(recording.Snapshot.Environment);
    }
}
