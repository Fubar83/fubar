# Security Policy

## Supported versions

Fubar Diff is pre-1.0 and moves fast. Security fixes land on `main` and in the next tagged release.
Only the latest release is supported.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately via GitHub's [private vulnerability reporting](https://github.com/Fubar83/fubar-diff/security/advisories/new)
(the **Report a vulnerability** button on the repository's *Security* tab). If that is unavailable,
email the maintainer at the address on their GitHub profile.

Please include:

- a description of the issue and its impact,
- steps to reproduce, with the input files if they can be shared safely,
- affected version / commit, OS, and .NET runtime.

You can expect an acknowledgement within a few days. Once a fix is available we will coordinate
disclosure and credit you in the release notes unless you prefer to remain anonymous.

## Scope notes

Fubar Diff opens files you point it at and renders them. It makes no network requests and stores no
credentials, so the realistic threat surface is file parsing:

- **Malicious or malformed input files.** Crashes, hangs, or unbounded memory growth while reading or
  diffing a file are valid reports. The reader already caps files at 64 MB and rejects binary
  content; a way around either is worth reporting.
- **JSON/XML normalization** parses untrusted content. XML entity expansion or similar parser abuse is
  in scope.
- **Supply chain**: release binaries are built by this repository's `build` workflow and carry a
  Sigstore provenance attestation. Verify with
  `gh attestation verify <file> --repo Fubar83/fubar-diff`.

Please do not attach files containing real secrets to a report — a diff tool is exactly the sort of
thing people point at config files.
