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

## Scope notes

Fubar API Studio is a desktop HTTP client. A few things are worth keeping in mind:

- **Secrets** (tokens, passwords, API keys) can be stored as workspace variables. Values
  marked *secret* are kept out of plain sight in the UI, and OAuth2 access tokens /
  expiry are held **only in memory** (session variables) and never written to disk.
- The app makes outbound HTTP requests to whatever hosts *you* configure. Treat imported
  OpenAPI specs and shared workspaces like any other untrusted input.
- Never paste real production credentials into a bug report.
