# Security Policy

## Supported versions

Both apps are pre-1.0 and move fast. Security fixes land on `main` and in the
next tagged release. Only the latest release is supported.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately via GitHub's [private vulnerability reporting](https://github.com/Fubar83/fubar/security/advisories/new)
(the **Report a vulnerability** button on the repository's *Security* tab). If that is
unavailable, email the maintainer at the address on their GitHub profile.

Please include:

- a description of the issue and its impact,
- steps to reproduce (a minimal spec / request / workspace if relevant),
- affected version / commit, OS, and .NET runtime.

You can expect an acknowledgement within a few days. Once a fix is available we will
coordinate disclosure and credit you in the release notes unless you prefer to remain
anonymous.

## Advisory: check your existing workspaces

Two defects fixed before 0.1.0 could put a credential into a file you commit. Both are fixed, but
**a fix does not un-commit anything** — if you used a build from before this, check.

1. **Environment-scoped captures wrote their value to `environments/*.json`.** A capture rule with
   scope *Environment* assigned the value directly, so capturing `$.access_token` persisted the token
   to a tracked file. If the variable was marked *Secret*, its on-disk value — documented as always
   empty — was overwritten with the real secret.
2. **`.fubar/` was not always ignored.** Execution history keeps up to 200 responses per request,
   response bodies included. The ignore rule was written only when creating a workspace *and* only
   when no `.gitignore` already existed, so pointing *New Workspace* at a repository you already had
   left that history tracked.

To check a workspace:

```bash
# A Secret variable should have "value": null. Anything credential-shaped with a real string is a find.
grep -rEn '"value"\s*:\s*"[^"]{12,}"' environments/ | grep -Ei 'token|secret|key|password|auth|bearer'

# Response bodies that captured a credential.
grep -rlEi '"(access_token|refresh_token|id_token|api_key)"' .fubar/ 2>/dev/null

# Was any of it committed?
git log --oneline -- environments/ .fubar/ | head
```

**If you find something, rotate it at the provider — revoke and reissue.** Deleting the file or
rewriting Git history hides the credential; only rotation makes it harmless, and it must be assumed
compromised from the moment it was pushed anywhere others can read.

## Scope notes

Fubar API Studio is a desktop HTTP client. A few things are worth keeping in mind:

- **Secrets** (tokens, passwords, API keys) can be stored as workspace variables. Values
  marked *secret* are kept out of plain sight in the UI, and OAuth2 access tokens /
  expiry are held **only in memory** (session variables) and never written to disk.
- **No telemetry, no account, no cloud sync.** The app makes outbound requests only to hosts you
  configure — plus the OpenAPI spec URL you ask it to import, and your OAuth provider's own
  endpoints. Workspaces are files in your own repository; secrets are in your own OS keyring.
- The app makes outbound HTTP requests to whatever hosts *you* configure. Treat imported
  OpenAPI specs and shared workspaces like any other untrusted input.
- Never paste real production credentials into a bug report.
