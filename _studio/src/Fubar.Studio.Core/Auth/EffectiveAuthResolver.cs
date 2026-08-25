using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Core.Auth;

/// <summary>The auth that actually applies to a request and where it comes from.</summary>
public sealed record EffectiveAuth(AuthConfig? Config, string? Source);

/// <summary>
/// Domain policy for which auth applies to a request (RequestEditorPane.md §5): a directly-selected
/// profile wins, then an inline scheme set on the request, then - only when the request is set to
/// Inherit - whichever profile the folder chain resolves to. Nothing applies for None / bare Inherit.
/// </summary>
public static class EffectiveAuthResolver
{
    public static EffectiveAuth Resolve(
        AuthType type,
        AuthConfig inlineConfig,
        AuthProfile? selectedProfile,
        InheritanceChain? inheritanceChain,
        IReadOnlyList<AuthProfile> availableProfiles)
    {
        if (type == AuthType.Profile && selectedProfile is not null)
        {
            return new EffectiveAuth(selectedProfile.Config, $"Auth: {selectedProfile.Name}");
        }

        if (type is AuthType.Bearer or AuthType.ApiKey or AuthType.Basic or AuthType.OAuth2)
        {
            return new EffectiveAuth(inlineConfig, "Auth (this request)");
        }

        if (type == AuthType.Inherit
            && inheritanceChain?.AuthProfileId is { } inheritedProfileId
            && availableProfiles.FirstOrDefault(p => p.Id == inheritedProfileId) is { } inheritedProfile)
        {
            return new EffectiveAuth(inheritedProfile.Config, inheritanceChain.AuthSourceName);
        }

        return new EffectiveAuth(null, null);
    }
}
