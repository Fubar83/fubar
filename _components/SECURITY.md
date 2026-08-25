# Security Policy

## Supported versions

`Fubar.Controls` is pre-1.0 and moves fast. Security fixes land on `main` and in the next
tagged release. Only the latest published package version is supported.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately via GitHub's [private vulnerability reporting](https://github.com/Fubar83/fubar-components/security/advisories/new)
(the **Report a vulnerability** button on the repository's *Security* tab). If that is
unavailable, email the maintainer at the address on their GitHub profile.

Please include:

- a description of the issue and its impact,
- a minimal reproduction (a XAML snippet or a Gallery page is ideal),
- affected package version / commit, OS, and .NET runtime.

You can expect an acknowledgement within a few days. Once a fix is available we will
coordinate disclosure and credit you in the release notes unless you prefer to remain
anonymous.

## Scope notes

`Fubar.Controls` is a UI component library. It performs no network I/O, reads no
credentials, and writes nothing to disk. The realistic threat surface is therefore small,
but the following are in scope:

- **`JsonEditor` / JSON schema completion** parse untrusted text. Crashes, hangs, or
  unbounded memory growth on malformed or adversarial input are valid reports.
- **Supply chain**: the published package is built by the release workflow in this
  repository and carries a Sigstore build-provenance attestation. Verify with
  `gh attestation verify <file> --repo Fubar83/fubar-components`. Report anything that
  suggests a package was not produced by that workflow.

Rendering glitches and layout bugs are **not** security issues — please file those as
ordinary bugs.
