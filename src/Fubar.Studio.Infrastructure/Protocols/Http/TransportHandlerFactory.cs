using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Infrastructure.Protocols.Http;

/// <summary>
/// Builds the <see cref="HttpClientHandler"/> for one environment: cookie jar, manual redirects, and -
/// when the environment asks for them - a client certificate, extra certificate authorities and a
/// proxy.
///
/// <para>These three are what an internal API behind mutual TLS or a private CA needs, and without
/// them the answer to "can it call our API" was no. The system proxy was already inherited by
/// default; the other two had no hook at all.</para>
/// </summary>
public static class TransportHandlerFactory
{
    /// <summary>What could not be set up, so the caller can say so instead of failing every request
    /// with a confusing TLS error later. Empty when everything resolved.</summary>
    public sealed record Problems(IReadOnlyList<string> Messages)
    {
        public bool Any => Messages.Count > 0;
    }

    public static (HttpClientHandler Handler, Problems Problems) Create(TransportSettings? transport, string? workspaceRootPath)
    {
        var problems = new List<string>();

        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            // Redirects are followed manually by HttpRequestExecutor so it can strip injected credential
            // headers on a cross-origin hop (the built-in handler only strips `Authorization`, not custom
            // API-key headers) - see RequestExecutionContext.SensitiveHeaderNames.
            AllowAutoRedirect = false,
        };

        if (transport is null || transport.IsEmpty)
        {
            return (handler, new Problems(problems));
        }

        if (!string.IsNullOrWhiteSpace(transport.ClientCertificateThumbprint))
        {
            if (FindClientCertificate(transport.ClientCertificateThumbprint!) is { } certificate)
            {
                handler.ClientCertificates.Add(certificate);
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            }
            else
            {
                problems.Add(
                    $"No certificate with thumbprint \"{transport.ClientCertificateThumbprint}\" in the current user's "
                    + "or the machine's store, so no client certificate will be presented.");
            }
        }

        if (transport.CertificateAuthorityPaths.Count > 0)
        {
            var roots = LoadExtraRoots(transport.CertificateAuthorityPaths, workspaceRootPath, problems);
            if (roots.Count > 0)
            {
                handler.ServerCertificateCustomValidationCallback = ValidatorFor(roots);
            }
        }

        if (!string.IsNullOrWhiteSpace(transport.ProxyUrl))
        {
            if (Uri.TryCreate(transport.ProxyUrl, UriKind.Absolute, out var proxyUri))
            {
                handler.Proxy = new WebProxy(proxyUri) { BypassList = [.. BypassPatterns(transport.ProxyBypass, problems)] };
                handler.UseProxy = true;
            }
            else
            {
                problems.Add($"\"{transport.ProxyUrl}\" is not a valid proxy URL, so the system proxy is being used.");
            }
        }

        return (handler, new Problems(problems));
    }

    /// <summary>
    /// Validation against the extra roots, and ONLY when ordinary validation has already failed.
    ///
    /// <para>Never <c>return true</c>. A callback that accepts everything is how a tool that wanted to
    /// trust one internal CA ends up trusting every forged certificate on the network, and it is
    /// indistinguishable from working until it matters. Adding a root widens what is accepted by
    /// exactly that root and nothing else - and a name mismatch still fails, since a private CA says
    /// nothing about whether this is the host that was asked for.</para>
    /// </summary>
    private static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> ValidatorFor(
        List<X509Certificate2> extraRoots) =>
        (_, certificate, _, errors) =>
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            // A wrong hostname is not something a trusted root can excuse.
            if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
                || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable)
                || certificate is null)
            {
                return false;
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            foreach (var root in extraRoots)
            {
                chain.ChainPolicy.CustomTrustStore.Add(root);
            }

            return chain.Build(certificate);
        };

    /// <summary>
    /// Turns the bypass entries people actually write into what <see cref="WebProxy"/> actually takes.
    ///
    /// <para><see cref="WebProxy.BypassList"/> is a list of REGULAR EXPRESSIONS, which nothing about
    /// the setting suggests: every other proxy configuration on a developer's machine takes
    /// <c>*.internal</c>, and typing that here threw a RegexParseException out of WebProxy the first
    /// time the proxy was used - taking down the request with an error naming neither the proxy nor
    /// the setting. So the wildcard form is translated, and anything that still will not compile is
    /// dropped with a message rather than left to detonate later.</para>
    /// </summary>
    private static List<string> BypassPatterns(IEnumerable<string> entries, List<string> problems)
    {
        var patterns = new List<string>();

        foreach (var entry in entries.Select(e => e?.Trim()).Where(e => !string.IsNullOrEmpty(e)))
        {
            var host = System.Text.RegularExpressions.Regex.Escape(entry!)
                .Replace("\\*", "[^/]*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal);

            // WebProxy matches each expression against the whole "scheme://host:port", not against the
            // host - so the pattern has to allow for both ends. Anchored at both, or "corp.com" would
            // also bypass "corp.com.evil.example", which is a bypass nobody asked for.
            var pattern = $"^[a-zA-Z][a-zA-Z0-9+.-]*://{host}(:\\d+)?/?$";

            try
            {
                _ = System.Text.RegularExpressions.Regex.Match("", pattern);
                patterns.Add(pattern);
            }
            catch (ArgumentException ex)
            {
                problems.Add($"Proxy bypass entry \"{entry}\" could not be used: {ex.Message}");
            }
        }

        return patterns;
    }

    private static List<X509Certificate2> LoadExtraRoots(
        IEnumerable<string> paths, string? workspaceRootPath, List<string> problems)
    {
        var roots = new List<X509Certificate2>();

        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            // Relative to the workspace, so a workspace that carries its own CA works on a colleague's
            // machine rather than only on the one it was configured on.
            var resolved = Path.IsPathRooted(path) || workspaceRootPath is null
                ? path
                : Path.Combine(workspaceRootPath, path);

            try
            {
                roots.Add(X509CertificateLoader.LoadCertificateFromFile(resolved));
            }
            catch (Exception ex)
            {
                problems.Add($"Could not read the certificate authority \"{resolved}\": {ex.Message}");
            }
        }

        return roots;
    }

    private static X509Certificate2? FindClientCertificate(string thumbprint)
    {
        // Normalised: thumbprints are routinely pasted with spaces from a certificate dialog.
        var wanted = new string(thumbprint.Where(char.IsLetterOrDigit).ToArray());

        foreach (var location in (StoreLocation[])[StoreLocation.CurrentUser, StoreLocation.LocalMachine])
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly);

                foreach (var candidate in store.Certificates)
                {
                    if (string.Equals(candidate.Thumbprint, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
            {
                // A store that cannot be opened (a locked-down agent, a platform without one) is not an
                // error worth throwing over - the caller reports the certificate as not found.
            }
        }

        return null;
    }
}
