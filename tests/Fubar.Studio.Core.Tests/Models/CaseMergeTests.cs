using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Models;

/// <summary>
/// What a case does to its endpoint.
///
/// <para>The split being tested is between the OPERATION - true every time the endpoint is called -
/// and the INVOCATION, which is true of one call. Every rule below follows from that.</para>
/// </summary>
public class CaseMergeTests
{
    private static RequestModel Endpoint() => new()
    {
        Name = "Get order",
        Method = "GET",
        Url = "{{baseUrl}}/orders/{orderId}",
        Headers = [new KeyValueItem { Key = "Accept", Value = "application/json" }],
        QueryParams = [new KeyValueItem { Key = "include", Value = "lines" }],
        Assertions = [new Assertion { Expected = "200" }],
    };

    [Fact]
    public void No_case_leaves_the_endpoint_alone()
    {
        var endpoint = Endpoint();

        Assert.Same(endpoint, CaseMerge.Apply(endpoint, null));
    }

    // ---- The URL ---------------------------------------------------------------------------------

    [Fact]
    public void A_path_parameter_is_filled_from_the_case()
    {
        var merged = CaseMerge.Apply(
            Endpoint(),
            new EndpointCase { Name = "default", PathParams = { ["orderId"] = "A-1234" } });

        Assert.Equal("{{baseUrl}}/orders/A-1234", merged.Url);
    }

    /// <summary>Two syntaxes, two owners: <c>{param}</c> is the endpoint's shape and
    /// <c>{{variable}}</c> is the environment's value. Filling one from the other's table would make
    /// it impossible to say which a missing value should have come from.</summary>
    [Fact]
    public void A_double_brace_variable_is_left_for_the_environment()
    {
        var merged = CaseMerge.Apply(
            Endpoint(),
            new EndpointCase { Name = "default", PathParams = { ["baseUrl"] = "http://nope" } });

        Assert.StartsWith("{{baseUrl}}", merged.Url, StringComparison.Ordinal);
    }

    /// <summary>An unresolved <c>/orders/{orderId}</c> says what is missing. <c>/orders/</c> is a
    /// different request that will get a plausible-looking answer from the wrong resource.</summary>
    [Fact]
    public void A_parameter_with_no_value_is_left_visible_rather_than_emptied()
    {
        var merged = CaseMerge.Apply(Endpoint(), new EndpointCase { Name = "empty" });

        Assert.Equal("{{baseUrl}}/orders/{orderId}", merged.Url);
    }

    [Fact]
    public void The_parameters_an_endpoint_needs_are_readable_from_its_url()
    {
        Assert.Equal(
            ["orderId", "lineId"],
            CaseMerge.PathParamNames("{{baseUrl}}/orders/{orderId}/lines/{lineId}"));
    }

    // ---- Merging ---------------------------------------------------------------------------------

    [Fact]
    public void Headers_and_query_parameters_are_a_union_with_the_case_winning()
    {
        var merged = CaseMerge.Apply(Endpoint(), new EndpointCase
        {
            Name = "default",
            Headers = [new KeyValueItem { Key = "Accept", Value = "text/csv" }],
            QueryParams = [new KeyValueItem { Key = "page", Value = "2" }],
        });

        Assert.Equal("text/csv", Assert.Single(merged.Headers).Value);
        Assert.Equal(["include", "page"], merged.QueryParams.Select(q => q.Key));
    }

    /// <summary>"No body" and "the endpoint's body" have to be different statements, or every case
    /// silently erases one.</summary>
    [Fact]
    public void A_null_body_inherits_and_an_explicit_none_does_not()
    {
        var endpoint = Endpoint();
        endpoint.Body = new RequestBody { Type = BodyType.Json, Raw = "{}" };

        Assert.Equal(BodyType.Json, CaseMerge.Apply(endpoint, new EndpointCase { Name = "a" }).Body.Type);

        Assert.Equal(
            BodyType.None,
            CaseMerge.Apply(endpoint, new EndpointCase { Name = "b", Body = new RequestBody() }).Body.Type);
    }

    /// <summary>A "not-found" case asserting 404 cannot also carry the endpoint's "status equals 200":
    /// merged, one of the two is guaranteed to fail.</summary>
    [Fact]
    public void Assertions_replace_rather_than_add_when_the_case_states_any()
    {
        var merged = CaseMerge.Apply(Endpoint(), new EndpointCase
        {
            Name = "not-found",
            Assertions = [new Assertion { Expected = "404" }],
        });

        Assert.Equal("404", Assert.Single(merged.Assertions).Expected);
    }

    [Fact]
    public void A_case_with_no_assertions_keeps_the_endpoints()
    {
        var merged = CaseMerge.Apply(Endpoint(), new EndpointCase { Name = "default" });

        Assert.Equal("200", Assert.Single(merged.Assertions).Expected);
    }

    /// <summary>Auth stops at the endpoint (spec §4.4). A case that needs a different user is an
    /// environment, not a case.</summary>
    [Fact]
    public void Auth_comes_from_the_endpoint()
    {
        var endpoint = Endpoint();
        endpoint.AuthProfileId = "profile-1";

        Assert.Equal("profile-1", CaseMerge.Apply(endpoint, new EndpointCase { Name = "a" }).AuthProfileId);
    }

    /// <summary>History is per endpoint. A case is a way of calling it, not a different thing to
    /// call, so it does not get an id of its own in the merged request.</summary>
    [Fact]
    public void The_merged_request_keeps_the_endpoints_id()
    {
        var endpoint = Endpoint();

        Assert.Equal(endpoint.Id, CaseMerge.Apply(endpoint, new EndpointCase { Name = "a" }).Id);
    }
}
