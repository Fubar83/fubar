# Fubar

[![CI](https://github.com/Fubar83/fubar/actions/workflows/ci.yml/badge.svg)](https://github.com/Fubar83/fubar/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Two native, cross-platform desktop tools built on **Avalonia 12 + .NET 10**, and the design system
they share.

| | |
| --- | --- |
| **[Fubar API Studio](docs/api-studio.md)** | An API client — a native, open-source Postman/Insomnia alternative. Request builder, environments, OAuth 2.0, OpenAPI import, assertions and captures. Ships as `FubarAPIStudio`. |
| **[Fubar Diff](docs/diff.md)** | A diff tool. Two-editor side-by-side comparison with character-level highlighting, semantic JSON, and hunk-level merge. Ships as `FubarDiff`. |
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
dotnet run --project src/Fubar.Controls.Gallery   # the component sandbox
```

### Packaging release binaries

Each app has its own publish script, producing self-contained per-runtime binaries (zip for Windows,
`tar.gz` for Linux, a `.app` for macOS). Both run from any OS with PowerShell 7+:

```powershell
./build/publish-api-studio.ps1
./build/publish-diff.ps1
```

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
