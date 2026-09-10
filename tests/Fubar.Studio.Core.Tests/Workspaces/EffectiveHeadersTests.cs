using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Core.Tests.Workspaces;

/// <summary>
/// What a request actually sends once its folders have had their say. The rule the runner and the
/// editor both go through, so that a run and a Send cannot ask two different questions.
/// </summary>
public class EffectiveHeadersTests
{
    private static InheritedHeader Inherited(string key, string value, bool enabled = true, string source = "Folder: api") =>
        new(new KeyValueItem { Key = key, Value = value, Enabled = enabled }, source);

    private static KeyValueItem Direct(string key, string value, bool enabled = true) =>
        new() { Key = key, Value = value, Enabled = enabled };

    private static List<KeyValueItem> Resolve(
        IEnumerable<InheritedHeader> inherited,
        IEnumerable<KeyValueItem> direct,
        params string[] suppressed) =>
        EffectiveHeaders.Resolve(inherited, direct, suppressed);

    [Fact]
    public void Inherited_headers_come_first_in_chain_order_then_the_requests_own()
    {
        var headers = Resolve(
            [Inherited("X-Root", "root", source: "Folder: Workspace Root"), Inherited("X-Api", "api")],
            [Direct("X-Own", "own")]);

        Assert.Equal(["X-Root", "X-Api", "X-Own"], headers.Select(h => h.Key));
    }

    [Fact]
    public void A_folder_header_the_folder_switched_off_is_not_handed_down()
    {
        var headers = Resolve([Inherited("X-Api", "api", enabled: false)], []);

        Assert.Empty(headers);
    }

    [Fact]
    public void A_suppressed_key_is_dropped_whatever_case_it_was_written_in()
    {
        var headers = Resolve([Inherited("Accept", "application/json")], [], "accept");

        Assert.Empty(headers);
    }

    [Fact]
    public void Suppression_does_not_touch_the_requests_own_header_of_the_same_name()
    {
        var headers = Resolve(
            [Inherited("Accept", "application/json")],
            [Direct("Accept", "application/xml")],
            "Accept");

        Assert.Equal(["application/xml"], headers.Select(h => h.Value));
    }

    [Fact]
    public void A_disabled_or_nameless_row_is_not_sent()
    {
        var headers = Resolve([], [Direct("X-Off", "no", enabled: false), Direct("  ", "nameless")]);

        Assert.Empty(headers);
    }

    /// <summary>Not collapsed by key: a repeated header is legal, and the executor adds them in order,
    /// so the request's own arrives last - which is what "closest wins" means on the wire.</summary>
    [Fact]
    public void A_key_at_both_levels_keeps_both_rows_with_the_requests_own_last()
    {
        var headers = Resolve([Inherited("Accept", "application/json")], [Direct("Accept", "application/xml")]);

        Assert.Equal(["application/json", "application/xml"], headers.Select(h => h.Value));
    }

    [Fact]
    public void Apply_leaves_the_original_request_alone()
    {
        var request = new RequestModel { Name = "r", Headers = [Direct("X-Own", "own")] };

        var sent = EffectiveHeaders.Apply(request, new InheritanceChain([Inherited("X-Api", "api")], null, null, []));

        Assert.Equal(["X-Api", "X-Own"], sent.Headers.Select(h => h.Key));
        Assert.Equal(["X-Own"], request.Headers.Select(h => h.Key));
    }

    [Fact]
    public void Apply_carries_everything_else_through()
    {
        var request = new RequestModel
        {
            Name = "r",
            Method = "POST",
            Url = "https://example.test/",
            TimeoutSeconds = 7,
            Assertions = [new Assertion()],
        };

        var sent = EffectiveHeaders.Apply(request, new InheritanceChain([Inherited("X-Api", "api")], null, null, []));

        Assert.Equal("POST", sent.Method);
        Assert.Equal("https://example.test/", sent.Url);
        Assert.Equal(7, sent.TimeoutSeconds);
        Assert.Single(sent.Assertions);
    }

    [Fact]
    public void A_chain_handing_nothing_down_returns_the_request_untouched()
    {
        var request = new RequestModel { Name = "r" };

        Assert.Same(request, EffectiveHeaders.Apply(request, new InheritanceChain([], null, null, [])));
        Assert.Same(request, EffectiveHeaders.Apply(request, null));
    }
}
