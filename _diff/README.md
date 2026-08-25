# Fubar Diff

[![CI](https://github.com/Fubar83/fubar-diff/actions/workflows/ci.yml/badge.svg)](https://github.com/Fubar83/fubar-diff/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A native, cross-platform desktop **diff tool** built on **Avalonia 12 + .NET 10**. Compare two files
side by side, with the panes locked in alignment and changes highlighted line by line.

It is a sibling of [Fubar API Studio](https://github.com/Fubar83/Fubar-API-Studio) and shares its
design system, the [`Fubar.Controls`](https://github.com/Fubar83/fubar-components) package.

> **Status: early.** Two-way file comparison, semantic JSON, merge and save work end to end. Folder
> comparison, free-form editing and the other formats are not built yet — see [Roadmap](#roadmap).

## Features

- **Two-editor side-by-side view**, with each side showing its own file's line numbers — so the
  numbers still match what is on disk across insertions.
- **Aligned panes.** Insertions and deletions get a placeholder row opposite them, and the two
  editors scroll in lockstep, so the columns cannot drift apart.
- **Character-level diff** inside modified lines, so a one-word change reads at a glance.
- **Diff map** between the panes — one tick per change, coloured by kind, click or drag to jump.
- **Change navigation** — next/previous with wrap-around (F7 / F8).
- **Merge and save** — take the left or right version of a change (Alt+Left / Alt+Right), then save.
  The file's encoding, BOM, line endings and trailing newline are preserved byte-for-byte.
- **Semantic JSON**: compares structure, not text. Reordered properties and reformatting are not
  differences; array elements are matched by an auto-detected identity key, so an element inserted
  mid-array marks only itself. Includes a **Tree** view of the structural changes, and falls back to a
  text diff for anything that does not parse.
- **Comparison options**: ignore leading/trailing whitespace, ignore case, report key order, match
  arrays by position, or normalize XML.
- **Encoding aware** — detects UTF-8/UTF-16 BOMs and CRLF/LF/CR line endings, and declines binary
  files rather than rendering a screen of mojibake.
- **Search** inside either pane with Ctrl+F.
- **Drag and drop** two files onto the window to compare them.
- **Tabs** — several comparisons open at once (Ctrl+T / Ctrl+W), each with its own files, options and
  merge decisions.
- **Recent comparisons**, and your options and theme are remembered between sessions.
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

**Next.** Free-form editing in the panes; search/replace, settings, recent files, tabs and drag &
drop; a proper CLI, git integration and patch export; XML/YAML/CSV, directory comparison, and 3-way
merge.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the setup, the conventions, and the layering the
architecture tests enforce. By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md). Security issues: see [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE).
