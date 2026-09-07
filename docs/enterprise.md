# Deploying Fubar API Studio

What an administrator can configure, and — as importantly — what this can and cannot enforce.

## What the app does not do

Worth stating first, because it is the part most tools cannot say:

- **No telemetry.** Nothing is reported anywhere, ever.
- **No account, no sign-in, no cloud sync, no licence server.**
- **No outbound requests except the ones you configure** — plus an OpenAPI spec URL you ask it to
  import, and your own OAuth provider's endpoints.

Workspaces are files in your own repository. Secrets are in your own OS keyring.

## Machine policy

A read-only file the app never writes:

| Platform | Path |
| --- | --- |
| Windows | `%ProgramData%\Fubar\policy.json` |
| macOS, Linux | `/etc/fubar/policy.json` |

```json
{
  "forbidEnvironmentCaptures": true,
  "requireClientCertificate": false,
  "allowedHosts": ["*.corp.example", "localhost"],
  "maxHistoryEntries": 0,
  "auditLogPath": "/var/log/fubar/runs.jsonl"
}
```

| Key | Effect |
| --- | --- |
| `forbidEnvironmentCaptures` | Capture rules may not write to the active environment. **The one worth setting first** — Environment scope persists a captured value to `environments/*.json`, which is committed, and the headline capture is an access token. |
| `requireClientCertificate` | Every environment must name a client certificate. |
| `allowedHosts` | Wildcard patterns requests may be sent to. Empty means no restriction. |
| `maxHistoryEntries` | Cap on execution-history entries per request. `0` disables history entirely. |
| `auditLogPath` | Append one JSON line per collection run. |

Read **once at startup**, deliberately: a policy that changed under a running session would mean a
capture allowed at the top of a collection run and refused at the bottom, which is harder to explain
than either answer alone. Restart to pick up a change.

A malformed policy file applies **nothing** and says so in the About panel. Applying half a policy
would be worse than applying none, and refusing to launch over an administrator's typo would be worse
than both.

### What policy is not

**On a machine where the user is a local administrator, this is advice.** They can edit the file, or
run a build of their own. It is worth setting because it stops the accidental commit — which is the
realistic mistake — not because it stops a determined person. `allowedHosts` in particular is a
guard-rail against sending staging credentials to production by mistake, and is not a network control.

If you need enforcement rather than defaults, the controls that actually bind are outside this app:
file permissions on the policy file, and whatever governs what runs on the machine.

## CI

The same binary is the batch tool. A build agent has no OS keyring, so secrets come from the
environment:

```bash
FubarAPIStudio --run --env CI \
  --var api_key="$API_KEY" \
  --report results.xml \
  --audit-log runs.jsonl
```

Precedence, highest first: `--var`, `--env-file`, `FUBAR_VAR_<KEY>`, session values, the OS keyring,
the environment file. `{{api_key}}` is fed by `FUBAR_VAR_API_KEY`. Values supplied this way are never
logged and never written back to a workspace file.

An unresolved `{{variable}}` **stops the run with exit code 2** rather than sending the literal text.

### Validating a workspace in a pull request

```bash
FubarAPIStudio --validate -w ./api-tests
```

Checks every workspace file against the published schemas, and warns about credential-shaped values in
committed files. Exit `0` valid, `1` invalid, `2` could not tell. `--strict` turns warnings into
failures.

The schemas encode the rule that matters: a variable marked *secret* or *session* **must** have a null
value on disk. A leaked token therefore fails the pull request rather than being found later.

## Corporate networks

Per environment, in `environments/*.json`:

```json
{
  "name": "Production",
  "transport": {
    "clientCertificateThumbprint": "A1B2C3...",
    "certificateAuthorityPaths": ["ca/internal-root.pem"],
    "proxyUrl": "http://proxy.corp:8080",
    "proxyBypass": ["*.internal", "localhost"]
  }
}
```

Client certificates are referenced **by thumbprint into the OS store**, never as a file path plus a
password — the workspace is committed, and a PFX with its password beside it is exactly the leak
everything else here is arranged to prevent.

Extra certificate authorities are consulted **only after normal validation has already failed**, and
a hostname mismatch still fails: trusting an internal CA widens what is accepted by that CA and
nothing else.

Proxy bypass entries take the wildcard form you would write anywhere else (`*.internal`).

## Audit

`--audit-log <path>`, or the `auditLogPath` policy key, appends one JSON object per line: when, which
user, which host, which workspace and environment, and the per-request verdict.

**No captured values, no request bodies, no response bodies** — this file is written to be kept, which
is the same reason the run report omits them.

It is deliberately a small feature. If you need more, the honest answer today is that this is what
exists.

## Deployment

Releases carry a Sigstore build-provenance attestation and a CycloneDX SBOM:

```bash
gh attestation verify FubarAPIStudio-win-x64.zip --repo Fubar83/fubar
```

Windows binaries are Authenticode-signed and macOS bundles are notarized and stapled **when the
signing credentials are configured on the release workflow**; a build without them still produces
working artifacts, unsigned. Check the release notes for the version you are deploying.
