using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Auth;

/// <summary>
/// The provider presets, and what seeding one produces.
///
/// <para>These assert the pieces of somebody else's service that this app claims to know - the ones
/// that fail silently and late. A missing <c>access_type=offline</c> looks like a working sign-in for
/// an hour and then cannot be renewed; a client secret sent to a public Entra client is rejected with
/// an error about client types. Neither is visible in a screenshot of a green tick.</para>
/// </summary>
public class SignInProviderTests
{
    private static SignInProvider Provider(string key) =>
        SignInProviderCatalog.ByKey(key) ?? throw new InvalidOperationException($"no provider {key}");

    private static IReadOnlyList<KeyValueItem> Fields(AuthTemplate template) =>
        template.SeedRequest.Body.UrlEncoded;

    private static string? Field(AuthTemplate template, string key) =>
        Fields(template).FirstOrDefault(f => f.Key == key)?.Value;

    // ---- the catalog itself ----------------------------------------------------------------------

    [Fact]
    public void Every_provider_has_a_distinct_key_and_something_to_show()
    {
        // The key is persisted into a committed profile, so a duplicate would make one provider
        // silently reopen as another.
        Assert.Distinct(SignInProviderCatalog.All.Select(p => p.Key));

        Assert.All(SignInProviderCatalog.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(p.SetupSummary));
        });
    }

    [Fact]
    public void An_unknown_key_is_ignored_rather_than_refused()
    {
        // A profile written by a newer version, or one naming a provider since removed. Everything the
        // sign-in needs was saved as plain URLs, so it still works - only the label is missing, and
        // failing the load over a label would lose the working parts too.
        Assert.Null(SignInProviderCatalog.ByKey("a-provider-from-the-future"));
        Assert.Null(SignInProviderCatalog.ByKey(null));
    }

    // ---- Google --------------------------------------------------------------------------------

    [Fact]
    public void Google_asks_for_offline_access_and_forces_consent()
    {
        // Google issues a refresh token ONLY with access_type=offline, and only re-issues one when
        // consent is asked for again. Without both, the second sign-in for an account comes back with
        // no refresh token and the failure surfaces an hour later as a token that cannot be renewed.
        var template = SignInProviderTemplate.For(Provider("google"));

        Assert.Equal("offline", template.AuthorizeParameters.Single(p => p.Key == "access_type").Value);
        Assert.Equal("consent", template.AuthorizeParameters.Single(p => p.Key == "prompt").Value);
    }

    [Fact]
    public void Google_sends_a_client_secret_because_its_desktop_clients_are_issued_one()
    {
        // Omitting it fails the exchange with invalid_client, which reads like a wrong client id.
        var template = SignInProviderTemplate.For(Provider("google"));

        Assert.Equal("{{client_secret}}", Field(template, "client_secret"));
    }

    // ---- Microsoft -----------------------------------------------------------------------------

    [Fact]
    public void Microsoft_sends_no_client_secret_because_a_public_client_must_not()
    {
        // Entra rejects a secret from a client registered as public. Registering the desktop app as
        // confidential to get around that means shipping a real secret to every desk.
        var template = SignInProviderTemplate.For(Provider("microsoft"));

        Assert.DoesNotContain(Fields(template), f => f.Key == "client_secret");
    }

    [Fact]
    public void Microsoft_asks_for_offline_access_as_a_scope_rather_than_a_parameter()
    {
        Assert.Contains("offline_access", SignInProviderTemplate.ScopeValue(Provider("microsoft")));
    }

    [Fact]
    public void The_tenant_is_substituted_into_every_url_that_carries_one()
    {
        var microsoft = Provider("microsoft");

        Assert.Equal(
            "https://login.microsoftonline.com/contoso.onmicrosoft.com/oauth2/v2.0/authorize",
            microsoft.AuthorizeEndpointFor("contoso.onmicrosoft.com"));

        Assert.Equal(
            "https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0",
            microsoft.IssuerFor("contoso.onmicrosoft.com"));
    }

    [Fact]
    public void An_empty_tenant_falls_back_to_the_default_rather_than_leaving_a_placeholder()
    {
        // A URL with a literal {tenant} in it fails at the provider with an error about the request,
        // which says nothing at all about the empty box that caused it.
        var microsoft = Provider("microsoft");

        Assert.DoesNotContain("{tenant}", microsoft.AuthorizeEndpointFor(""), StringComparison.Ordinal);
        Assert.DoesNotContain("{tenant}", microsoft.AuthorizeEndpointFor(null), StringComparison.Ordinal);
        Assert.Contains("/common/", microsoft.AuthorizeEndpointFor("  "), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tenant_is_trimmed_so_a_pasted_value_still_works()
    {
        // Tenant ids are pasted, and a paste brings whitespace often enough to matter.
        Assert.Contains("/abc/", Provider("microsoft").TokenEndpointFor(" abc "), StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_with_no_tenant_ignores_one_it_is_given()
    {
        Assert.Equal(
            "https://oauth2.googleapis.com/token",
            Provider("google").TokenEndpointFor("something"));
    }

    // ---- GitHub --------------------------------------------------------------------------------

    [Fact]
    public void Github_asks_for_json_because_its_token_endpoint_answers_form_encoded_otherwise()
    {
        // A form-encoded body defeats the JSONPath captures: the token arrives, nothing captures it,
        // and the editor reports a 200 with no token - the most confusing success there is.
        var template = SignInProviderTemplate.For(Provider("github"));

        Assert.Equal(
            "application/json",
            template.SeedRequest.Headers.Single(h => h.Key == "Accept").Value);
    }

    [Fact]
    public void Github_says_out_loud_that_it_ignores_pkce()
    {
        // The request still carries a challenge, because GitHub ignores unknown parameters - and an
        // ignored challenge protects nothing. Someone choosing this provider should know that from
        // the screen rather than infer it from a spec.
        Assert.Contains("PKCE", Provider("github").Caveat!, StringComparison.Ordinal);
    }

    // ---- what every seed must get right ----------------------------------------------------------

    [Fact]
    public void Every_provider_seeds_a_usable_authorization_code_exchange()
    {
        Assert.All(SignInProviderCatalog.All, provider =>
        {
            var template = SignInProviderTemplate.For(provider);

            Assert.Equal(OAuth2GrantType.AuthorizationCode, template.Grant);
            Assert.Equal("authorization_code", Field(template, "grant_type"));

            // The three the browser step writes into the session immediately before this runs.
            Assert.Equal("{{oauth2_code}}", Field(template, "code"));
            Assert.Equal("{{oauth2_code_verifier}}", Field(template, "code_verifier"));
            Assert.Equal("{{oauth2_redirect_uri}}", Field(template, "redirect_uri"));

            Assert.Equal(provider.Key, template.ProviderKey);
        });
    }

    [Fact]
    public void Credentials_are_seeded_as_variables_never_as_blank_boxes()
    {
        // A profile is committed. A literal secret in one is a secret in everybody's checkout, and the
        // shape of the seed is what decides which of those a hurried user ends up with.
        Assert.All(SignInProviderCatalog.All, provider =>
        {
            var template = SignInProviderTemplate.For(provider);

            Assert.Equal("{{client_id}}", Field(template, "client_id"));

            if (provider.NeedsClientSecret)
            {
                Assert.Equal("{{client_secret}}", Field(template, "client_secret"));
            }
        });
    }

    [Fact]
    public void Every_seed_captures_the_access_token_into_the_variable_the_bearer_header_reads()
    {
        Assert.All(SignInProviderCatalog.All, provider =>
        {
            var capture = SignInProviderTemplate.For(provider).SeedCaptures
                .Single(c => c.Expression == "$.access_token");

            Assert.Equal(AuthDefaults.AccessTokenVariable, capture.VariableName);
            Assert.Equal(CaptureScope.Session, capture.Scope);
        });
    }

    [Fact]
    public void Nothing_a_seed_captures_is_written_to_disk()
    {
        // Session scope, every rule, no exceptions: the environment scope persists to a committed
        // file, and everything a token endpoint returns is credential-shaped.
        Assert.All(SignInProviderCatalog.All, provider =>
            Assert.All(
                SignInProviderTemplate.For(provider).SeedCaptures,
                capture => Assert.Equal(CaptureScope.Session, capture.Scope)));
    }

    [Fact]
    public void Editing_a_seeded_parameter_does_not_change_the_preset()
    {
        // The catalog's lists are static and the editor's rows are edited in place. A preset whose
        // defaults drifted because someone typed in a box would be a genuinely baffling bug.
        var first = SignInProviderTemplate.For(Provider("google"));

        first.AuthorizeParameters.Single(p => p.Key == "access_type").Value = "online";

        var second = SignInProviderTemplate.For(Provider("google"));

        Assert.Equal("offline", second.AuthorizeParameters.Single(p => p.Key == "access_type").Value);
    }

    [Fact]
    public void Custom_seeds_a_shape_to_fill_in_rather_than_endpoints_it_cannot_know()
    {
        var custom = SignInProviderCatalog.CustomProvider;
        var template = SignInProviderTemplate.For(custom);

        Assert.Equal("", template.AuthorizeUrl);
        Assert.Equal("{{token_url}}", template.SeedRequest.Url);
        Assert.Null(custom.IssuerTemplate);
    }
}
