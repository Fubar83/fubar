using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// YAML through the whole pipeline, which is the point of it having been added the way it was: the
/// parser is new, and nothing else is. The differ, the ignore rules, the array identity keys and the
/// spans that highlight a change all belong to the JSON path and were not touched.
///
/// The case this exists for is the one YAML is used for most: a manifest whose keys are in a
/// different order in two branches, where a line diff reports the whole file and a structural one
/// reports the two things that actually changed.
/// </summary>
public class YamlComparisonTests
{
    private sealed class Files(Dictionary<string, string[]> files) : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TextDocument(path, files[path], TextFormat.Default));
    }

    private static async Task<FileComparison> CompareAsync(
        string left,
        string right,
        string leftName = "a.yaml",
        string rightName = "b.yaml",
        ComparisonOptions? options = null)
    {
        var service = new FileComparisonService(
            new Files(new()
            {
                [leftName] = left.Split('\n'),
                [rightName] = right.Split('\n'),
            }),
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser(), new YamlAstParser()));

        return await service.CompareFilesAsync(leftName, rightName, options ?? ComparisonOptions.Default);
    }

    private const string Deployment = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: widget
        spec:
          replicas: 2
          image: registry/widget:1.2.0
        """;

    private const string Reordered = """
        kind: Deployment
        spec:
          image: registry/widget:1.3.0
          replicas: 2
        metadata:
          name: widget
        apiVersion: apps/v1
        """;

    [Fact]
    public async Task A_reordered_manifest_reports_only_what_changed()
    {
        // A line diff of these two says almost every line moved. There is one difference in them.
        var comparison = await CompareAsync(Deployment, Reordered);

        Assert.True(comparison.IsSemantic);
        var change = Assert.Single(comparison.SemanticChanges);
        Assert.Equal("$.spec.image", change.Path.ToString());
    }

    [Fact]
    public async Task Identical_content_written_differently_is_no_difference_at_all()
    {
        var comparison = await CompareAsync(
            "name: widget\nport: 8080\n",
            "port: 8080\nname: widget\n");

        Assert.True(comparison.IsSemantic);
        Assert.Empty(comparison.SemanticChanges);
    }

    [Fact]
    public async Task A_quoted_number_is_a_difference()
    {
        // The change most likely to break something, and the one a naive YAML comparison misses.
        var comparison = await CompareAsync("port: 8080\n", "port: \"8080\"\n");

        Assert.Single(comparison.SemanticChanges);
    }

    [Fact]
    public async Task The_change_carries_a_span_into_the_text()
    {
        // Structural differences are shown ON the document, so a change with no location would be
        // found and then not displayable.
        var comparison = await CompareAsync(Deployment, Reordered);
        var change = Assert.Single(comparison.OriginalSemanticChanges);

        Assert.True(change.LeftSpan.IsKnown);
        Assert.True(change.RightSpan.IsKnown);
    }

    [Fact]
    public async Task An_ignored_path_works_the_same_as_it_does_for_json()
    {
        // Nothing about ignore rules is JSON-specific; this is the test that says so.
        var options = new ComparisonOptions
        {
            Json = new JsonComparisonOptions { IgnoredPaths = ["$.spec.image"] },
        };

        var comparison = await CompareAsync(Deployment, Reordered, options: options);

        Assert.All(comparison.SemanticChanges, change => Assert.True(change.IsIgnored));
        Assert.True(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task A_list_of_objects_is_matched_by_identity_rather_than_position()
    {
        // The array-key machinery, inherited whole. Two containers swapped in the manifest is not a
        // change to both of them.
        var comparison = await CompareAsync(
            """
            containers:
              - name: api
                image: api:1
              - name: worker
                image: worker:1
            """,
            """
            containers:
              - name: worker
                image: worker:1
              - name: api
                image: api:2
            """);

        // One change, not four: the two entries were paired by their name field, so swapping them is
        // not a change to either. The path still addresses the element by index - that is how the
        // JSON path works and is unchanged here - so what it says is "one image differs".
        var change = Assert.Single(comparison.SemanticChanges);
        Assert.EndsWith(".image", change.Path.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_that_is_not_named_yaml_is_not_read_as_yaml()
    {
        // Nearly all text is valid YAML - these two log lines included - so sniffing would turn every
        // text comparison in the app into a comparison of two one-scalar documents.
        var comparison = await CompareAsync(
            "starting up\n",
            "shutting down\n",
            "a.log",
            "b.log");

        Assert.False(comparison.IsSemantic);
        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task An_explicit_mode_reads_a_manifest_that_has_no_extension()
    {
        var comparison = await CompareAsync(
            "replicas: 2\n",
            "replicas: 3\n",
            "rendered-output",
            "expected-output",
            new ComparisonOptions { Mode = ComparisonMode.Yaml });

        Assert.True(comparison.IsSemantic);
        Assert.Single(comparison.SemanticChanges);
    }

    [Fact]
    public async Task Broken_yaml_falls_back_to_text_and_says_why()
    {
        // A broken file is exactly when a diff is most wanted, so it must never refuse - and when the
        // user explicitly asked for YAML, it should say why they did not get it.
        var comparison = await CompareAsync(
            "key: [unclosed\n",
            "key: [unclosed\n",
            options: new ComparisonOptions { Mode = ComparisonMode.Yaml });

        Assert.False(comparison.IsSemantic);
        Assert.Contains("YAML", comparison.SemanticFallbackReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_json_file_can_be_compared_against_its_yaml_translation()
    {
        // Each side is read as whatever its name says it is, which costs nothing and makes a real
        // comparison possible: YAML is a superset of JSON, so both land in the same tree.
        var comparison = await CompareAsync(
            """{"name":"widget","port":8080}""",
            "name: widget\nport: 8081\n",
            "config.json",
            "config.yaml");

        Assert.True(comparison.IsSemantic);
        Assert.Equal("$.port", Assert.Single(comparison.SemanticChanges).Path.ToString());
    }

    [Fact]
    public async Task The_pretty_button_is_not_offered_for_yaml()
    {
        // It re-lays-out JSON and there is no YAML emitter behind it. A control that quietly does
        // nothing is worse than one that is not there.
        Assert.False((await CompareAsync(Deployment, Reordered)).IsJsonPair);
        Assert.True((await CompareAsync("""{"a":1}""", """{"a":2}""", "a.json", "b.json")).IsJsonPair);
    }
}
