# Fubar Diff

[![CI](https://github.com/Fubar83/fubar-diff/actions/workflows/ci.yml/badge.svg)](https://github.com/Fubar83/fubar-diff/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A native, cross-platform desktop **diff tool** built on **Avalonia 12 + .NET 10**. Compare two files
side by side, with the panes locked in alignment and changes highlighted line by line.

It is a sibling of [Fubar API Studio](https://github.com/Fubar83/Fubar-API-Studio) and shares its
design system, the [`Fubar.Controls`](https://github.com/Fubar83/fubar-components) package.

> **Status: early.** The side-by-side file comparison below works end to end. Folder comparison and
> merge editing are not built yet — see [Roadmap](#roadmap).

## Features

- **Side-by-side comparison** of two text files, with line numbers on both sides.
- **Aligned panes.** Insertions and deletions get a placeholder row opposite them, and both sides
  share a single scroller, so the two columns cannot drift apart.
- **Change navigation** — jump to the next or previous change, wrapping at either end.
- **Comparison options**: ignore leading/trailing whitespace, ignore case, or normalize JSON/XML so a
  pure reformat is not reported as a difference.
- **Encoding aware** — detects UTF-8/UTF-16 BOMs and CRLF/LF/CR line endings, and declines binary
  files rather than rendering a screen of mojibake.
- **Dark and light themes**, switchable at runtime.

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Fubar83/fubar-diff.git
cd fubar-diff

dotnet build FubarDiff.slnx
dotnet test  FubarDiff.slnx
dotnet run   --project src/Fubar.Diff.UI
```

Two files can be named on the command line to compare them immediately:

```bash
dotnet run --project src/Fubar.Diff.UI -- old.json new.json
```

The shared UI components come from the separate
[fubar-components](https://github.com/Fubar83/fubar-components) repository as the `Fubar.Controls`
NuGet package. To build against a local checkout of that library instead of the published package —
useful when changing a control and this app together:

```bash
dotnet build FubarDiff.slnx -p:UseLocalComponents=true
```

### Packaging release binaries

`build/publish.ps1` produces self-contained, per-runtime binaries and packages each one (zip for
Windows, `tar.gz` for Linux, a `.app` for macOS). It runs from any OS with PowerShell 7+:

```powershell
./build/publish.ps1                        # all default runtimes
./build/publish.ps1 -Runtimes osx-arm64    # just one
./build/publish.ps1 -Version 1.2.3
```

## Project structure

Clean, layered, and enforced by tests — dependencies point inward only.

| Project | Role |
| --- | --- |
| `Fubar.Controls` *(package)* | The shared design system and control library. Lives in [fubar-components](https://github.com/Fubar83/fubar-components). |
| `src/Fubar.Diff.Core` | Domain models, policy, and ports — `DiffLine`, `DiffResult`, `ComparisonOptions`, `HunkNavigator`, `IDiffEngine`, `ITextFileReader`. BCL only. |
| `src/Fubar.Diff.Application` | Use cases — `FileComparisonService` orchestrates read → normalize → align → project. |
| `src/Fubar.Diff.Infrastructure` | Adapters — the DiffPlex-backed engine, text/JSON/XML normalization, and file access. |
| `src/Fubar.Diff.UI` | The desktop app (Avalonia + MVVM). Ships as `FubarDiff`. |
| `tests/*` | xUnit projects per layer, plus an architecture suite that fails the build if the layering breaks. |

The diff algorithm itself is [DiffPlex](https://github.com/mmanela/diffplex) (MIT), used only behind
the `IDiffEngine` port in Infrastructure — swapping it is a one-file change, and a test enforces that.

## Roadmap

- Folder comparison (recursive tree, per-file status).
- Inline (unified) view as an alternative to side-by-side.
- Merge editing — copy a hunk across and save the result.
- Word-level highlighting within a modified line.
- Syntax highlighting via the `JsonEditor`/TextMate stack already in `Fubar.Controls`.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the setup, the conventions, and the layering the
architecture tests enforce. By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md). Security issues: see [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE).
