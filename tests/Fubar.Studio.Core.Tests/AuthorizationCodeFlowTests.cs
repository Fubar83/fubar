using System.Security.Cryptography;
using System.Text;
using System.Web;
using Fubar.Studio.Core.Auth;

namespace Fubar.Studio.Core.Tests;

/// <summary>
/// The authorization-code grant's pure half: the URL that gets opened, and the redirect that comes
/// back.
///
/// The rest of the flow needs a browser and a socket. These are the decisions that can be got wrong
/// silently, and one of them - the state check - is a security control rather than a convenience.
/// </summary>
public class AuthorizationCodeFlowTests
{
    private static AuthorizationRequest Build(string endpoint = "https://login.example.com/authorize") =>
        AuthorizationCodeFlow.Build(endpoint, "my-client", "openid profile", 7890);

    // ---- PKCE ------------------------------------------------------------------------------------

    [Fact]
    public void The_challenge_is_the_sha256_of_the_verifier_base64url_encoded()
    {
        // Getting this wrong means the provider hashes the verifier, compares it with the challenge,
        // and rejects the exchange - with an error about the code, not about the encoding.
        var pair = Pkce.Create();

        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(pair.Verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Equal(expected, pair.Challenge);
        Assert.Equal("S256", pair.Method);
    }

    [Fact]
    public void The_encoding_is_url_safe_and_unpadded()
    {
        // Ordinary base64 would be re-encoded in a query string and the provider would hash something
        // else. Over many pairs, at least one will contain a byte that base64 renders as + or /.
        for (var i = 0; i < 200; i++)
        {
            var pair = Pkce.Create();

            Assert.DoesNotContain('+', pair.Challenge);
            Assert.DoesNotContain('/', pair.Challenge);
            Assert.DoesNotContain('=', pair.Challenge);
            Assert.DoesNotContain('=', pair.Verifier);
        }
    }

    [Fact]
    public void Every_attempt_gets_its_own_secrets()
    {
        Assert.NotEqual(Pkce.Create().Verifier, Pkce.Create().Verifier);
        Assert.NotEqual(Pkce.CreateState(), Pkce.CreateState());
    }

    [Fact]
    public void The_verifier_is_long_enough_for_the_spec()
    {
        // RFC 7636 allows 43-128 characters; 32 random bytes base64url'd is 43.
        Assert.InRange(Pkce.Create().Verifier.Length, 43, 128);
    }

    // ---- The authorize URL -----------------------------------------------------------------------

    [Fact]
    public void The_url_carries_everything_the_provider_needs()
    {
        var request = Build();
        var query = HttpUtility.ParseQueryString(new Uri(request.AuthorizeUrl).Query);

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("my-client", query["client_id"]);
        Assert.Equal("openid profile", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(request.State, query["state"]);
        Assert.Equal(request.RedirectUri, query["redirect_uri"]);
    }

    [Fact]
    public void The_verifier_never_appears_in_the_url()
    {
        // The entire point of PKCE: the code travels through a browser, the verifier does not.
        var request = Build();

        Assert.DoesNotContain(request.Verifier, request.AuthorizeUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void The_redirect_is_a_loopback_address_on_the_given_port()
    {
        // RFC 8252 §7.3 - the only redirect a desktop app can receive without a custom scheme, and
        // what providers expect from a native client.
        Assert.Equal("http://127.0.0.1:7890/callback", Build().RedirectUri);
    }

    [Fact]
    public void An_endpoint_that_already_has_query_parameters_keeps_them()
    {
        // Tenant and audience parameters live there, and dropping them breaks exactly the providers
        // that need them.
        var request = Build("https://login.example.com/authorize?tenant=acme");
        var query = HttpUtility.ParseQueryString(new Uri(request.AuthorizeUrl).Query);

        Assert.Equal("acme", query["tenant"]);
        Assert.Equal("code", query["response_type"]);
    }

    [Fact]
    public void No_scope_means_no_scope_parameter()
    {
        var request = AuthorizationCodeFlow.Build("https://x/authorize", "c", null, 1234);

        Assert.Null(HttpUtility.ParseQueryString(new Uri(request.AuthorizeUrl).Query)["scope"]);
    }

    // ---- The callback ----------------------------------------------------------------------------

    [Fact]
    public void A_matching_callback_yields_the_code()
    {
        var result = AuthorizationCodeFlow.ReadCallback("?code=abc123&state=xyz", "xyz");

        Assert.True(result.Ok);
        Assert.Equal("abc123", result.Code);
    }

    [Fact]
    public void A_callback_with_the_wrong_state_is_REFUSED()
    {
        // The security control. Without it, any request reaching the loopback listener - a stray tab,
        // a page that guessed the port - could hand this process a code and complete a sign-in nobody
        // started.
        var result = AuthorizationCodeFlow.ReadCallback("?code=abc123&state=somebody-elses", "xyz");

        Assert.False(result.Ok);
        Assert.Null(result.Code);
        Assert.Equal("state_mismatch", result.Error);
    }

    [Fact]
    public void A_callback_with_no_state_at_all_is_refused_too()
    {
        Assert.Equal("state_mismatch", AuthorizationCodeFlow.ReadCallback("?code=abc123", "xyz").Error);
    }

    [Fact]
    public void The_providers_own_refusal_is_passed_through()
    {
        var result = AuthorizationCodeFlow.ReadCallback(
            "?error=access_denied&error_description=User+said+no&state=xyz", "xyz");

        Assert.False(result.Ok);
        Assert.Equal("access_denied", result.Error);
        Assert.Equal("User said no", result.ErrorDescription);
    }

    [Fact]
    public void A_redirect_carrying_neither_is_reported_rather_than_hanging()
    {
        var result = AuthorizationCodeFlow.ReadCallback("?state=xyz", "xyz");

        Assert.False(result.Ok);
        Assert.Equal("no_code", result.Error);
    }

    [Fact]
    public void A_query_string_with_or_without_its_question_mark_reads_the_same()
    {
        Assert.True(AuthorizationCodeFlow.ReadCallback("code=a&state=s", "s").Ok);
        Assert.True(AuthorizationCodeFlow.ReadCallback("?code=a&state=s", "s").Ok);
    }

    // ---- provider-specific authorize parameters ---------------------------------------------------

    [Fact]
    public void Extra_parameters_reach_the_authorize_url()
    {
        // Where the provider folklore lands. Google issues a refresh token only with
        // access_type=offline; Auth0 returns an opaque string instead of a JWT without audience.
        var request = AuthorizationCodeFlow.Build(
            "https://accounts.google.com/o/oauth2/v2/auth", "id", "openid", 7890,
            extraParameters: [new("access_type", "offline"), new("prompt", "consent")]);

        var query = HttpUtility.ParseQueryString(new Uri(request.AuthorizeUrl).Query);

        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
    }

    [Fact]
    public void An_extra_parameter_is_encoded_rather_than_pasted_in()
    {
        // Added through the same collection as everything else, so a value containing a space, an
        // ampersand or a colon cannot split the URL into parameters nobody asked for.
        var request = AuthorizationCodeFlow.Build(
            "https://login.example.com/authorize", "id", null, 7890,
            extraParameters: [new("audience", "https://api.example.com/v1 admin&x")]);

        var query = HttpUtility.ParseQueryString(new Uri(request.AuthorizeUrl).Query);

        Assert.Equal("https://api.example.com/v1 admin&x", query["audience"]);
        Assert.Null(query["x"]);
    }

    [Fact]
    public void An_extra_parameter_may_override_a_protocol_one()
    {
        // Deliberate. Guessing which of somebody else's parameters are sacred is how an allowlist ends
        // up blocking exactly the provider it was meant to support, and a user who needs a different
        // response_mode has no other way to say so.
        var request = AuthorizationCodeFlow.Build(
            "https://login.example.com/authorize", "id", null, 7890,
            extraParameters: [new("scope", "replaced")]);

        Assert.Equal("replaced", HttpUtility.ParseQueryString(new Uri(request.AuthorizeUrl).Query)["scope"]);
    }

    [Fact]
    public void A_nameless_extra_parameter_is_dropped_rather_than_sent()
    {
        // An empty row in the grid is someone part-way through typing, not a parameter.
        var request = AuthorizationCodeFlow.Build(
            "https://login.example.com/authorize", "id", null, 7890,
            extraParameters: [new("", "orphan"), new("   ", "orphan")]);

        Assert.DoesNotContain("orphan", request.AuthorizeUrl, StringComparison.Ordinal);
    }

    // ---- the redirect URI --------------------------------------------------------------------------

    [Fact]
    public void The_redirect_uri_uses_the_port_it_was_given()
    {
        // The whole point of pinning: this is the string the user registers with their provider, and
        // most providers match it exactly.
        Assert.Equal(
            "http://127.0.0.1:8765/callback",
            AuthorizationCodeFlow.Build("https://login.example.com/authorize", "id", null, 8765).RedirectUri);
    }

    [Fact]
    public void The_redirect_uri_can_be_shown_before_a_sign_in_runs()
    {
        // It has to be registered BEFORE the first attempt. Deriving it from a failure is the worst
        // way to learn it - the browser shows the provider's error page and this app hears nothing.
        Assert.Equal("http://127.0.0.1:8765/callback", AuthorizationCodeFlow.RedirectUriFor(8765));
    }

    [Fact]
    public void An_ephemeral_port_says_it_is_not_known_yet_rather_than_showing_a_number()
    {
        // Showing 0, or last attempt's number, would invite registering a URI that can never come back.
        Assert.DoesNotContain(":0", AuthorizationCodeFlow.RedirectUriFor(0), StringComparison.Ordinal);
        Assert.Contains("free port", AuthorizationCodeFlow.RedirectUriFor(0), StringComparison.Ordinal);
    }

    [Fact]
    public void The_redirect_host_is_the_ip_literal_not_localhost()
    {
        // RFC 8252 section 8.3: localhost depends on a name resolution this app does not control, and
        // on a machine where it resolves to ::1 first the browser reaches a listener that is not there.
        Assert.StartsWith(
            "http://127.0.0.1:",
            AuthorizationCodeFlow.Build("https://login.example.com/authorize", "id", null, 9001).RedirectUri,
            StringComparison.Ordinal);
    }
}
