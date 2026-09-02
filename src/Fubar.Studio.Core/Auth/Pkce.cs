using System.Security.Cryptography;
using System.Text;

namespace Fubar.Studio.Core.Auth;

/// <summary>
/// A PKCE pair: the secret kept locally and the challenge sent to the provider.
/// </summary>
/// <param name="Verifier">The secret. Sent only in the token exchange, never in the browser URL.</param>
/// <param name="Challenge">The SHA-256 of the verifier, base64url encoded. Sent in the authorize URL.</param>
public sealed record PkcePair(string Verifier, string Challenge)
{
    /// <summary>Always S256. See <see cref="Pkce"/> for why "plain" is not offered.</summary>
    public string Method => "S256";
}

/// <summary>
/// Proof Key for Code Exchange (RFC 7636).
///
/// The authorization code comes back through a browser redirect, which means it travels through the
/// user's URL bar, their history, and any handler registered for the redirect. PKCE makes an
/// intercepted code useless on its own: the token exchange must also present the verifier, which never
/// left this process.
///
/// Only S256 is generated. The spec also allows <c>plain</c>, where the challenge IS the verifier -
/// which provides no protection whatsoever against the interception this exists to prevent, and is in
/// the spec for clients that cannot compute SHA-256. A desktop app is not one of those.
/// </summary>
public static class Pkce
{
    /// <summary>
    /// A fresh pair. The verifier is 32 random bytes base64url-encoded, giving 43 characters - the
    /// spec allows 43 to 128, and there is no reason to be at the bottom of that range.
    /// </summary>
    public static PkcePair Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));

        return new PkcePair(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    /// <summary>
    /// An opaque value echoed back by the provider, so a redirect that arrives can be told apart from
    /// one belonging to a different attempt - or to nobody.
    ///
    /// Checking it is what stops a stray or forged redirect completing someone else's sign-in, which
    /// is why <see cref="AuthorizationCodeFlow"/> refuses a callback whose state does not match.
    /// </summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// base64url per RFC 4648 §5: the URL-safe alphabet, and no padding. Ordinary base64 would be
    /// re-encoded in a query string and the provider would hash something else.
    /// </summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
