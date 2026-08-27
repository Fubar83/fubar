# Fubar Diff

[![CI](https://github.com/Fubar83/fubar/actions/workflows/ci.yml/badge.svg)](https://github.com/Fubar83/fubar/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A native, cross-platform desktop **diff tool** built on **Avalonia 12 + .NET 10**. Compare two files
side by side, with the panes locked in alignment and changes highlighted line by line.

It is a sibling of [Fubar API Studio](https://github.com/Fubar83/fubar) and shares its
design system, the [`Fubar.Controls`](https://github.com/Fubar83/fubar) package.

> **Status: early.** Two-way file comparison, semantic JSON, merge and save work end to end. Folder
> comparison, free-form editing and the other formats are not built yet — see [Roadmap](#roadmap).

## Features

- **Two-editor side-by-side view**, with each side showing its own file's line numbers — so the
  numbers still match what is on disk across insertions.
- **Aligned panes.** Insertions and deletions get a placeholder row opposite them, and the two
  editors scroll in lockstep, so the columns cannot drift apart.
- **Character-level diff** inside modified lines, so a one-word change reads at a glance.
- **Diff map** between the panes — one tick per change, coloured by kind, click or drag to jump.
- **Diff pane** below the panes: the old line stacked directly above the new one, so you can read
  both versions of one change without scrolling between two blocks a screen apart - and with the same
  line right above its replacement, the character-level highlight is what catches the eye. Drag its
  edge to resize, or turn it off from the toolbar.
- **Change navigation** — next/previous with wrap-around (F7 / F8, or Alt+Up / Alt+Down). The current
  difference is marked with an accent bar and outline, so it stays findable among the other changes.
- **Merge and save** — take the left or right version of a change (Alt+Left / Alt+Right), then save.
  The file's encoding, BOM, line endings and trailing newline are preserved byte-for-byte.
- **Semantic JSON**: compares structure, not text. Reordered properties and reformatting are not
  differences; array elements are matched by an auto-detected identity key, so an element inserted
  mid-array marks only itself. JSON opens in the **Json** view by default - the change tree plus both
  documents, each shown exactly as given (a minified file stays minified, not reformatted) - where
  stepping through changes highlights each one directly in both, immune to formatting or
  property-order differences since neither side depends on lining up with the other. **Text** remains
  available for the aligned side-by-side view, and is the only mode for anything that does not parse
  as JSON.
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
git clone https://github.com/Fubar83/fubar.git
cd fubar-diff

dotnet build Fubar.slnx
dotnet test  Fubar.slnx
dotnet run   --project src/Fubar.Diff.UI
```

Two files can be named on the command line to compare them immediately:

```bash
dotnet run --project src/Fubar.Diff.UI -- old.json new.json
```

The shared UI components live alongside this app in `src/Fubar.Controls`, referenced directly - a
change to a control and a change to the app that uses it go in one build and one commit.

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
| `Fubar.Controls` *(package)* | The shared design system and control library. Lives in [fubar-components](https://github.com/Fubar83/fubar). |
| `src/Fubar.Diff.Core` | Domain models, policy, and ports — `DiffLine`, `DiffResult`, `ComparisonOptions`, `HunkNavigator`, `IDiffEngine`, `ITextFileReader`. BCL only. |
| `src/Fubar.Diff.Application` | Use cases — `FileComparisonService` orchestrates read → normalize → align → project. |
| `src/Fubar.Diff.Infrastructure` | Adapters — the DiffPlex-backed engine, text/JSON/XML normalization, and file access. |
| `src/Fubar.Diff.UI` | The desktop app (Avalonia + MVVM). Ships as `FubarDiff`. |
| `tests/*` | xUnit projects per layer, plus an architecture suite that fails the build if the layering breaks. |

The diff algorithm itself is [DiffPlex](https://github.com/mmanela/diffplex) (MIT), used only behind
the `IDiffEngine` port in Infrastructure — swapping it is a one-file change, and a test enforces that.

## Roadmap

**Open.** Free-form editing in the panes — the editors are read-only because the aligned documents
contain filler lines, and typing needs a bidirectional editor↔source offset map. That also gates
search/**replace** (find works today). Virtualised diffing for very large files is the other gap: the
whole aligned document is currently materialised per side, under a 64 MB reader cap.

**Cut.** A CLI with exit codes, git integration and patch export; semantic XML, YAML, CSV, directory
comparison and 3-way merge. Dropped deliberately rather than forgotten — `MergedDocument` already
produces the line model a patch would need, and `MergeState` was designed so a third side is additive,
if any of it is ever revived.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the setup, the conventions, and the layering the
architecture tests enforce. By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md). Security issues: see [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE).
