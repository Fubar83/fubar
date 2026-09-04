using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <summary>
/// Brings a <c>request.json</c> written before the format floor up to the current shape, once, on
/// open.
///
/// <para>Three legacy shapes were being kept alive by readers that ran on every load:
/// <c>LocalVariables</c> (retired when variables became environment-only),
/// <c>ResponseDiffIgnorePaths</c> (superseded by <c>Comparison</c>), and the pre-template
/// <c>AuthConfig</c> upgraded on the fly by <c>OAuth2LegacyTemplate</c>. Each carried its own branch
/// and its own tests, and the auth one carried a live hazard the codebase had already been bitten
/// by - two engines behind an invisible switch, where a guard added to one path silently did not
/// apply to the other.</para>
///
/// <para>Converting once and deleting the readers is only cheap while nothing is released, which is
/// exactly where this repository is - see <c>docs/decisions.md</c> §C. A migration REPORTS what it
/// changed, because it is rewriting files the user is about to read in a diff.</para>
/// </summary>
public static class LegacyRequestMigration
{
    /// <summary>What a migration did to one request, or that it did nothing.</summary>
    /// <param name="Changes">Human-readable, one per thing altered. Empty means the file was already
    /// current and must not be rewritten - touching a file to change nothing moves its timestamp and
    /// puts it in someone's diff for no reason.</param>
    public sealed record Result(IReadOnlyList<string> Changes)
    {
        public bool Changed => Changes.Count > 0;

        public static Result Unchanged { get; } = new([]);
    }

    /// <summary>
    /// Rewrites <paramref name="request"/> in place, returning what changed.
    ///
    /// <para>Idempotent: running it twice does nothing the second time, which is what makes it safe to
    /// call on every open rather than needing a version stamp to decide.</para>
    /// </summary>
    public static Result Apply(RequestModel request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var changes = new List<string>();

        // 1. Request-local variables. Retired when resolution became environment-only; anything still
        //    here has not been read by the app for a long time, so it is dropped rather than converted -
        //    converting would mean guessing which environment the user meant.
        if (request.LocalVariables.Count > 0)
        {
            changes.Add($"dropped {request.LocalVariables.Count} retired request-local variable(s)");
            request.LocalVariables = [];
        }

        // 2. Ignore paths folded into the comparison section. The model already knew how; it just never
        //    got to keep the result, because EffectiveComparison recomputed it on every read instead.
        if (request.ResponseDiffIgnorePaths.Count > 0)
        {
            if (request.Comparison is null)
            {
                request.Comparison = new ComparisonSettings { IgnoredPaths = [.. request.ResponseDiffIgnorePaths] };
                changes.Add($"moved {request.ResponseDiffIgnorePaths.Count} ignore rule(s) into the comparison section");
            }
            else
            {
                // Both present means a file half-migrated by an older build. The new section wins - it
                // is the one the editor writes - and the legacy list is dropped rather than merged,
                // because merging would resurrect a rule the user may have deliberately removed.
                changes.Add($"dropped {request.ResponseDiffIgnorePaths.Count} superseded ignore rule(s)");
            }

            request.ResponseDiffIgnorePaths = [];
        }

        // 3. The pre-template auth config. Upgraded permanently here rather than on every acquisition.
        if (UpgradeAuth(request.Auth))
        {
            changes.Add("upgraded the OAuth 2.0 configuration to an editable token request");
        }

        return changes.Count == 0 ? Result.Unchanged : new Result(changes);
    }

    /// <summary>
    /// Gives a legacy OAuth2 config the <see cref="AuthConfig.TokenRequest"/> the editor writes, so
    /// there is one shape and one code path rather than two behind a switch.
    ///
    /// <para>Returns false for anything already current, or for a scheme that never had a legacy form.
    /// Unlike the on-the-fly upgrade this replaces, credentials are left as the <c>{{tokens}}</c> the
    /// user wrote: this result is PERSISTED, and resolving them here would bake a secret into a
    /// committed file.</para>
    /// </summary>
    private static bool UpgradeAuth(AuthConfig auth)
    {
        if (auth is not { Type: AuthType.OAuth2, TokenRequest: null })
        {
            return false;
        }

        // null, not an identity function: FromLegacy's own doc says the editor passes null precisely
        // because the result is SAVED, and baking a resolved secret into a header about to be written
        // to a committed file would be exactly wrong. A migration is the editor's case, not the
        // provider's.
        var (request, captures) = OAuth2LegacyTemplate.FromLegacy(auth, resolveCredentials: null);

        auth.TokenRequest = request;
        auth.TokenCaptures = captures;

        // The legacy engine read expires_in through the token service rather than a configured path,
        // so an upgraded config has to be told where it is or every token would look non-expiring and
        // be cached for the life of the session.
        auth.ExpiresInExpression ??= "$.expires_in";

        return true;
    }
}
