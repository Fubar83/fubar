namespace Fubar.Studio.Core.Settings;

/// <summary>
/// Settings an administrator can impose on an installation, read from a machine-wide file the app
/// never writes.
///
/// <para><c>AppSettings</c> is one per-user file, so nothing could be required of an installation: no
/// mandated proxy or CA, no allowed-host list, and no way to forbid the one setting that leaks
/// credentials - capturing to Environment scope, which writes a token into a committed file.</para>
///
/// <para><b>Deliberately small.</b> Every key here has to be enforceable and worth enforcing; a policy
/// file full of preferences nobody checks is worse than none, because it implies control that is not
/// there. And it is honest about its own reach: on a machine where the user is an administrator,
/// policy is advice. That is said plainly in docs/enterprise.md rather than left implied.</para>
/// </summary>
public sealed class MachinePolicy
{
    /// <summary>Nothing imposed - what every installation without a policy file gets.</summary>
    public static MachinePolicy None { get; } = new();

    /// <summary>
    /// Forbid capture rules that write to the active environment.
    ///
    /// <para>The single most valuable key: Environment scope persists a captured value to
    /// <c>environments/*.json</c>, which is committed, and the headline capture is an access token.</para>
    /// </summary>
    public bool ForbidEnvironmentCaptures { get; set; }

    /// <summary>Require every environment to name a client certificate, for an estate on mutual TLS.</summary>
    public bool RequireClientCertificate { get; set; }

    /// <summary>
    /// Hosts requests may be sent to, as wildcard patterns (<c>*.corp.example</c>). Empty means no
    /// restriction.
    ///
    /// <para>Not a security boundary and not sold as one - it stops an accidental send to the wrong
    /// environment, which is the realistic mistake, rather than a determined user.</para>
    /// </summary>
    public List<string> AllowedHosts { get; set; } = [];

    /// <summary>Cap on response bodies kept in execution history, or null for the built-in default.
    /// Zero turns history off entirely.</summary>
    public int? MaxHistoryEntries { get; set; }

    /// <summary>Write a structured line per collection run to this path. Null leaves auditing off.</summary>
    public string? AuditLogPath { get; set; }

    /// <summary>True when this policy imposes nothing, so the UI can stay quiet about it.</summary>
    public bool IsEmpty =>
        !ForbidEnvironmentCaptures
        && !RequireClientCertificate
        && AllowedHosts.Count == 0
        && MaxHistoryEntries is null
        && string.IsNullOrWhiteSpace(AuditLogPath);
}

/// <summary>Reads the machine policy. Never writes one - that is the administrator's file.</summary>
public interface IMachinePolicyService
{
    /// <summary>The policy in force, or <see cref="MachinePolicy.None"/>.</summary>
    MachinePolicy Current { get; }

    /// <summary>Where it was read from, for the About panel to show. Null when there is none.</summary>
    string? Source { get; }
}
