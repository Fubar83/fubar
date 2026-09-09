using System.Text.Json.Serialization;

namespace Fubar.Studio.Core.Models;

/// <summary>
/// How connections for one environment are made: client certificate, extra certificate authorities,
/// and proxy. The three things a corporate network asks for, and without which the answer to "can it
/// call our API" is no, before any other feature matters.
///
/// <para>Per ENVIRONMENT rather than per workspace, because these differ exactly the way environments
/// do - a staging API behind a private CA and a production one behind mutual TLS are the normal shape.
/// The client cache is keyed by (workspace, environment) plus a fingerprint of this section, so two
/// environments never share a handler; see <c>SessionScope</c>.</para>
/// </summary>
public sealed class TransportSettings
{
    /// <summary>
    /// Thumbprint of a client certificate in the current user's certificate store, for mutual TLS.
    ///
    /// <para>A thumbprint, deliberately - never a file path plus a password. The workspace is
    /// committed, and a PFX path with its password beside it in a tracked file is precisely the leak
    /// the rest of this design exists to prevent. The certificate stays where the OS put it.</para>
    /// </summary>
    public string? ClientCertificateThumbprint { get; set; }

    /// <summary>
    /// Extra root certificates to trust, as file paths (PEM or DER), for an internal CA the machine
    /// does not already trust. Relative paths resolve against the workspace root, so a committed
    /// workspace carrying its own CA works on a colleague's machine.
    ///
    /// <para>These ADD to the system roots, and are only consulted when normal validation has already
    /// failed - so trusting an internal CA never weakens validation for anything else.</para>
    /// </summary>
    public List<string> CertificateAuthorityPaths { get; set; } = [];

    /// <summary>Proxy URL, e.g. <c>http://proxy.corp:8080</c>. Null uses the system proxy, which is
    /// what most machines want and what the app has always done.</summary>
    public string? ProxyUrl { get; set; }

    /// <summary>Hosts that bypass the proxy - the usual internal suffixes.</summary>
    public List<string> ProxyBypass { get; set; } = [];

    /// <summary>True when nothing here departs from the defaults, so the section can be omitted from
    /// the file rather than persisted as a bag of nulls.</summary>

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(ClientCertificateThumbprint)
        && CertificateAuthorityPaths.Count == 0
        && string.IsNullOrWhiteSpace(ProxyUrl)
        && ProxyBypass.Count == 0;

    /// <summary>
    /// A stable string identifying this configuration, for the HTTP client cache key.
    ///
    /// <para>Load-bearing: clients are cached per (workspace, environment), so without this a change
    /// here would keep handing back the handler built before it - and, worse, two environments that
    /// resolved to the same scope would share a client with the wrong certificate on it.</para>
    /// </summary>
    public string Fingerprint() =>
        string.Join(
            '|',
            ClientCertificateThumbprint ?? "",
            string.Join(',', CertificateAuthorityPaths),
            ProxyUrl ?? "",
            string.Join(',', ProxyBypass));
}
