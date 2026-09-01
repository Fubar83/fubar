# Fubar

[![CI](https://github.com/Fubar83/fubar/actions/workflows/ci.yml/badge.svg)](https://github.com/Fubar83/fubar/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Two native, cross-platform desktop tools built on **Avalonia 12 + .NET 10**, and the design system
they share.

| | |
| --- | --- |
| **[Fubar API Studio](docs/api-studio.md)** | An API client — a native, open-source Postman/Insomnia alternative. Request builder, environments, OAuth 2.0, OpenAPI import, assertions and captures. Ships as `FubarAPIStudio`. |
| **[Fubar Diff](docs/diff.md)** | A diff tool. Side-by-side comparison with character-level highlighting, semantic JSON and YAML, folder comparison, three-way merge with an editable result, and a **structural C# comparison** that says which members changed and which were only reformatted or moved. Runs headless for CI. Ships as `FubarDiff`. |
| **Fubar.Controls** | The shared design system: colour tokens with Dark/Light variants, and a catalog of composable Avalonia controls. Has its own sandbox app, the Gallery. |

## Why one repository

These started as three. The split existed so `Fubar.Controls` could be consumed as a NuGet package,
which made sense while the sharing ran one way — both apps depending on one library.

It stopped making sense once API Studio needed the **diff view** as well. That made the diff engine a
second shared library and the dependency graph a mesh, so every cross-cutting change would have meant
two or three pull requests plus a package publish and a version bump. Consolidating turns all of that
into project references and a single build.

The trade is real and deliberate: `Fubar.Controls` is no longer consumable from outside this
repository.

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Fubar83/fubar.git
cd fubar

dotnet build Fubar.slnx
dotnet test  Fubar.slnx

dotnet run --project src/Fubar.Studio.UI          # API Studio
dotnet run --project src/Fubar.Diff.UI            # Diff, empty
dotnet run --project src/Fubar.Diff.UI -- a.json b.json   # Diff, two files
dotnet run --project src/Fubar.Diff.UI -- --check a.json b.json    # headless; 0 same, 1 differ, 2 failed
dotnet run --project src/Fubar.Controls.Gallery   # the component sandbox
```

### Packaging release binaries

Each app has its own publish script, producing self-contained per-runtime binaries (zip for Windows,
`tar.gz` for Linux, a `.app` for macOS). Both run from any OS with PowerShell 7+:

```powershell
./build/publish-api-studio.ps1
./build/publish-diff.ps1
```

### Releasing

**One app per tag, and the tag says which.** The two ship on their own schedules and are at different
stages of maturity, so a single `v*` tag would force them to release together — meaning either holding
the ready one back or shipping the unready one.

```bash
git tag diff-v0.1.0-beta.1   && git push origin diff-v0.1.0-beta.1     # Fubar Diff
git tag studio-v0.1.0-beta.1 && git push origin studio-v0.1.0-beta.1   # Fubar API Studio
```

That builds six runtimes on their native runners, attaches a Sigstore build-provenance attestation and
SHA-256 checksums, and publishes a GitHub Release. **A version containing a hyphen is treated as a
prerelease** (semver), so `0.1.0-beta.1` is marked as one and never becomes the repository's *Latest
release* — that is derived from the version rather than from a flag someone has to remember.

A tag that names no app is rejected rather than guessed at. To rehearse without publishing anything,
run the workflow manually from the Actions tab: it builds and uploads artifacts, and the release step
is skipped for anything that is not a tag.

> **Binaries are unsigned.** No Authenticode certificate, no macOS notarization — Windows SmartScreen
> and macOS Gatekeeper will both warn. On macOS, `xattr -d com.apple.quarantine "Fubar Diff.app"`.
> Every archive does carry a verifiable provenance attestation:
> `gh attestation verify <file> --repo Fubar83/fubar`.

## Layout

```
src/
  Fubar.Controls/            shared design system + controls   ─┐
  Fubar.Controls.Gallery/    living style guide for it          │ consumed by both apps
  Fubar.Studio.{Core,Application,Infrastructure,UI}/   API Studio
  Fubar.Diff.{Core,Application,Infrastructure,UI}/     Diff
tests/                       one suite per project, plus architecture guards per app
docs/                        per-app notes and pane specs
build/                       publish scripts
```

Both apps follow the same clean, layered architecture, and both have a NetArchTest suite that fails
the build if the layering breaks. `Fubar.Controls` has its own guard asserting it depends on nothing
but Avalonia, AvaloniaEdit and the BCL — which matters more here than it did across repositories,
since a stray `using` would now compile fine.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md). Security issues: see [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE).
