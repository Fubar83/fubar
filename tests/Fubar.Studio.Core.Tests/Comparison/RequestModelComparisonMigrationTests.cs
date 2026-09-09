using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Core.Tests.Comparison;

/// <summary>
/// The one-time conversion of a pre-floor <c>request.json</c>.
///
/// <para>These assertions used to be about <c>RequestModel.MigrateLegacyIgnorePaths</c> and
/// <c>EffectiveComparison</c> - readers that ran on every load and kept three legacy shapes alive
/// indefinitely. The behaviour is the same; it now happens once, on open, and the readers are gone.
/// See docs/decisions.md §C.</para>
/// </summary>
public class RequestModelComparisonMigrationTests
{
    [Fact]
    public void Legacy_ignore_paths_move_into_the_comparison_section()
    {
        var request = new RequestModel { Name = "r", ResponseDiffIgnorePaths = ["$.meta.requestId"] };

        var result = LegacyRequestMigration.Apply(request);

        Assert.True(result.Changed);
        Assert.Equal(["$.meta.requestId"], request.Comparison!.IgnoredPaths!.Add);
        Assert.Empty(request.ResponseDiffIgnorePaths);
    }

    /// <summary>
    /// A file half-migrated by an older build. The new section wins - it is what the editor writes -
    /// and the legacy list is dropped rather than merged, because merging would resurrect a rule the
    /// user may have deliberately removed.
    /// </summary>
    [Fact]
    public void An_existing_comparison_section_is_not_overwritten()
    {
        var request = new RequestModel
        {
            Name = "r",
            ResponseDiffIgnorePaths = ["$.old"],
            Comparison = new ComparisonSettings { IgnoredPaths = InheritedPaths.FromAdded(["$.new"]) },
        };

        LegacyRequestMigration.Apply(request);

        Assert.Equal(["$.new"], request.Comparison!.IgnoredPaths!.Add);
        Assert.Empty(request.ResponseDiffIgnorePaths);
    }

    [Fact]
    public void Retired_request_local_variables_are_dropped()
    {
        var request = new RequestModel
        {
            Name = "r",
            LocalVariables = [new KeyValueItem { Key = "host", Value = "x" }],
        };

        var result = LegacyRequestMigration.Apply(request);

        Assert.True(result.Changed);
        Assert.Empty(request.LocalVariables);
    }

    [Fact]
    public void A_legacy_oauth_config_gains_a_token_request()
    {
        var request = new RequestModel
        {
            Name = "r",
            Auth = new AuthConfig
            {
                Type = AuthType.OAuth2,
                TokenUrl = "https://id.example.com/token",
                ClientId = "{{client_id}}",
                ClientSecret = "{{client_secret}}",
            },
        };

        var result = LegacyRequestMigration.Apply(request);

        Assert.True(result.Changed);
        Assert.NotNull(request.Auth.TokenRequest);
        // Without this every token would look non-expiring and be cached for the session.
        Assert.Equal("$.expires_in", request.Auth.ExpiresInExpression);
    }

    /// <summary>
    /// The result is SAVED, so credentials stay as the {{tokens}} the user wrote. Baking a resolved
    /// secret into a header about to be written to a committed file is the failure this whole area is
    /// about.
    /// </summary>
    [Fact]
    public void A_migrated_oauth_config_keeps_its_variables_unresolved()
    {
        var request = new RequestModel
        {
            Name = "r",
            Auth = new AuthConfig
            {
                Type = AuthType.OAuth2,
                TokenUrl = "https://id.example.com/token",
                ClientId = "{{client_id}}",
                ClientSecret = "{{client_secret}}",
            },
        };

        LegacyRequestMigration.Apply(request);

        var serialised = System.Text.Json.JsonSerializer.Serialize(request.Auth.TokenRequest);
        Assert.Contains("{{client_secret}}", serialised, StringComparison.Ordinal);
    }

    /// <summary>Idempotent, which is what makes it safe to run on every open rather than needing a
    /// version stamp to decide.</summary>
    [Fact]
    public void A_current_request_is_left_alone()
    {
        var request = new RequestModel
        {
            Name = "r",
            Comparison = new ComparisonSettings { IgnoreCase = true },
        };

        Assert.False(LegacyRequestMigration.Apply(request).Changed);
        Assert.False(LegacyRequestMigration.Apply(request).Changed);
    }

    /// <summary>A second pass must change nothing, or the file would be rewritten on every open and
    /// land in someone's diff every time.</summary>
    [Fact]
    public void Running_it_twice_changes_nothing_the_second_time()
    {
        var request = new RequestModel { Name = "r", ResponseDiffIgnorePaths = ["$.meta.requestId"] };

        Assert.True(LegacyRequestMigration.Apply(request).Changed);
        Assert.False(LegacyRequestMigration.Apply(request).Changed);
    }

    [Fact]
    public void Every_change_is_reported_so_the_shell_can_say_what_happened()
    {
        var request = new RequestModel
        {
            Name = "r",
            ResponseDiffIgnorePaths = ["$.a"],
            LocalVariables = [new KeyValueItem { Key = "k", Value = "v" }],
            Auth = new AuthConfig { Type = AuthType.OAuth2, TokenUrl = "https://id.example.com/token" },
        };

        var result = LegacyRequestMigration.Apply(request);

        Assert.Equal(3, result.Changes.Count);
    }
}
