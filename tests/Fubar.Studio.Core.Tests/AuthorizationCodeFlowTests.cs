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
}
