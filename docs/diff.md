# Fubar Diff

[![CI](https://github.com/Fubar83/fubar/actions/workflows/ci.yml/badge.svg)](https://github.com/Fubar83/fubar/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A native, cross-platform desktop **diff tool** built on **Avalonia 12 + .NET 10**. Compare two files
side by side, with the panes locked in alignment and changes highlighted line by line.

It is a sibling of [Fubar API Studio](https://github.com/Fubar83/fubar) and shares its
design system, the [`Fubar.Controls`](https://github.com/Fubar83/fubar) package.

> **Status: early.** Two-way file comparison, semantic JSON, source-code comparison, three-way merge,
> folder comparison and save all work end to end. Free-form editing and the other formats are not built
> yet — see [Roadmap](#roadmap).

## Features

- **Two-editor side-by-side view**, with each side showing its own file's line numbers — so the
  numbers still match what is on disk across insertions.
- **Aligned panes.** Insertions and deletions get a placeholder row opposite them, and the two
  editors scroll in lockstep, so the columns cannot drift apart.
- **Character-level diff** inside modified lines, so a one-word change reads at a glance. In a language
  the tool knows, the split follows the language's own tokens: `==` becoming `===` highlights the whole
  operator rather than a lone third `=`.
- **Syntax highlighting**, for every language a TextMate grammar ships for, following the app theme.
  On by default; switch it off in Settings → Appearance.
- **Source-code comparison** for **C#, JavaScript, TypeScript, Java, Go, C, C++ and Python**, picked
  from the file extension:
  optionally ignore comments (a changed comment stops being a difference; a comment-only line that was
  added is drawn faintly rather than counted — the code on the line still compares normally) and ignore
  blank lines. Block comments, verbatim and raw strings, and template literals are tracked across lines,
  so the inside of a multi-line comment is treated as a comment even where it reads like code. Both
  options are off by default and say so on screen when the pair is not a language they apply to.
- **Ambiguous change groups are placed where they read best.** When a run of added or removed lines is
  bounded by lines identical to the ones just inside it, several placements describe the same two files
  and all are equally minimal — which is why a moved method so often shows up as a closing brace plus
  the start of the next one. Groups are slid toward blank lines and lower indentation, the same
  heuristic git uses, without changing what the diff says.
- **Moved code is shown as moved.** A block that was reordered rather than rewritten is tinted blue on
  both sides — not red here and green there — counted separately in the status line, and drawn blue in
  the diff map, so a reordered file stops looking like a rewritten one. It still counts as a change
  everywhere it should: the hunks, F7 / F8, the merge and the patch all describe what is genuinely on
  disk. Two blocks are paired only when their text appears exactly once on each side, so a run of `}`
  is never matched with an unrelated one — where the answer is ambiguous, nothing is claimed. Works for
  a block that travelled far enough to have no counterpart *and* for two methods that simply swapped
  places, which a line differ reports as a pile of rewritten lines.
- **Side-by-side or unified.** The **View** selector switches between the two-editor view and a single
  patch-style document — removals then additions, shared context between them — for a narrow window, a
  screenshot, or anyone who reads patches all day. The unified view can **wrap long lines**, which the
  side-by-side one cannot: two columns stay aligned by having the same number of visual lines, and a
  line that wraps on one side alone would pull them apart.
- **Collapse unchanged** — long stretches both files agree on fold behind a placeholder, keeping a few
  lines either side of each change. On by default; click any fold to expand it, or turn it off from the
  toolbar. A file of three thousand lines with two changes reads as two changes.
- **Binary and image comparison.** Two files that are not text are compared as bytes: whether they are
  identical, how big each is, where they first differ, and a hex dump of each side with the differing
  rows tinted. Because that dump is an ordinary diff result, the scroll sync, diff map, navigation and
  collapse-unchanged all work on it. **Two images are shown as pictures**, side by side above their
  bytes, at the same scale with each one's real dimensions underneath — PNG, JPEG, GIF, BMP, WebP and
  ICO, recognised from the file's own signature rather than its extension. Merging is refused here: the
  hex is a view of the bytes, not the file.
- **Diff map** between the panes — one tick per change, coloured by kind, click or drag to jump.
- **Diff pane** below the panes: the old line stacked directly above the new one, so you can read
  both versions of one change without scrolling between two blocks a screen apart - and with the same
  line right above its replacement, the character-level highlight is what catches the eye. Drag its
  edge to resize, or turn it off from the toolbar.
- **Change navigation** — next/previous with wrap-around (F7 / F8, or Alt+Up / Alt+Down). The current
  difference is marked with an accent bar and outline, so it stays findable among the other changes.
- **Merge and save** — take the left or right version of a change (Alt+Left / Alt+Right), then save.
  The file's encoding, BOM, line endings and trailing newline are preserved byte-for-byte.
- **Folder comparison** — two directory trees walked together and reported as a tree: changed, left
  only, right only. Identical files are **hidden by default** (the count is in the status line), since
  on two real checkouts they are most of what is there. `.git`, `bin`, `obj`, `node_modules` and
  friends are excluded out of the box and the list is editable, with `*` and `?` wildcards. Files are
  compared by **contents**, not size — two files of the same length are routinely different. Double-click
  any changed pair to open it as an ordinary comparison tab. Select any two files and compare **those**
  against each other, for a file that was renamed and so appears once on each side. **Copy** a file, or
  everything under a folder, to the other side — the button says what it would do and a confirmation
  names the paths and how many files would be replaced first. It copies and never deletes.
- **Snapshot review** — tick *One folder, linked by name* and it pairs files against each other inside a
  single folder: `Thing.verified.json` against `Thing.received.json`, which is what
  [Verify](https://github.com/VerifyTests/Verify) and ApprovalTests leave behind. New snapshots and
  baselines nothing produces any more are called out separately from changed ones. The rules are just
  markers (`.verified = .received`), editable, so any convention works. **Accept a snapshot** by copying
  the `.received` file leftwards over its baseline, after confirming.
- **Three-way merge** — give it a common ancestor and two edits and it settles everything only one side
  touched, plus everything both sides changed identically, on its own. What is left is the set that
  genuinely disagrees. Three panes, the ancestor in the middle, all locked in step; conflicts are marked
  in amber, navigation stops only on them by default (F7 / F8), and each one is resolved by taking left,
  base, right, or **both** (Alt+B) — for when neither edit is wrong and the answer is to keep the two of
  them, which is what two methods added at the same place actually needs. Within a region each edit highlights the characters it altered relative to the
  ancestor, so two conflicting versions of nearly the same line can be told apart at a glance. A
  **Diff pane** below stacks the three versions of the current region — left, base, right — so they can
  be read together rather than across three columns a screen apart. Save
  writes the merged file to whichever of the three you choose, in that file's own encoding and line
  endings. An unresolved conflict keeps the ancestor's text and says so, both in a banner before saving
  and in the status line after.
- **Semantic JSON**: compares structure, not text. Reordered properties and reformatting are not
  differences; array elements are matched by an auto-detected identity key, so an element inserted
  mid-array marks only itself. JSON opens in the **Json** view by default - the change tree plus both
  documents, each shown exactly as given (a minified file stays minified, not reformatted) - where
  stepping through changes (Prev/Next, or a click in the tree) highlights each one directly in both,
  immune to formatting or property-order differences since neither side depends on lining up with the
  other. It has its own **Diff pane** too, the same close-up shown below the aligned Text view but
  built from the change's own location in each side's raw text rather than an aligned row range.
  **Text** remains available for the aligned side-by-side view, and is the only mode for anything that
  does not parse as JSON.
- **Comparison options**: ignore leading/trailing whitespace, ignore case, or reformat (pretty-print
  JSON/XML in the Text view - opt-in, never automatic; the Json view always shows both sides exactly as
  given regardless of this) from the toolbar; a **Settings…** window holds the rest in three sections -
  Text compare (including **ignored text patterns**: regular expressions whose matches stop counting as
  differences, for the build timestamp or generated GUID that changes on every run - only the match is
  ignored, so a real change elsewhere on the line is still reported); Code compare (ignore comments,
  ignore blank lines); and JSON compare (report key order,
  match arrays by position, treat `null` and a missing property as equal, per-path array identity key
  overrides, and a list of JSON paths whose differences are never reported).
- **Format differences are reported, not hidden** — two files whose content matches but whose
  encoding, byte order mark, line endings or trailing newline do not are called out explicitly, since
  none of those reach the panes and "identical" would be wrong.
- **Reveal invisible characters** — non-breaking spaces, zero-width characters and bidirectional
  controls marked where they occur, for when the diff is right and looks wrong.
- **Normalize Unicode (NFC)** — optional, for text that renders identically but is encoded differently
  (macOS decomposes where Windows and Linux compose).
- **Encoding aware** — detects UTF-8/UTF-16 BOMs and CRLF/LF/CR line endings, and never renders a
  screen of mojibake: content that is not text is handed to the byte comparison above instead.
- **Follows the files** — when either file is saved elsewhere the comparison re-runs, so a diff kept
  open beside your editor stays current. It never discards unsaved merge decisions to do it: with any
  pending, it offers a Reload button instead.
- **Export as a patch** — copy the comparison to the clipboard or save it as a unified diff, the format
  `git apply`, `patch` and every review tool already understand. Three lines of context around each
  change, overlapping hunks merged, and the file names rather than your absolute paths.
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

Three open a merge instead, in the argument order `git mergetool` uses — `$BASE $LOCAL $REMOTE`:

```bash
dotnet run --project src/Fubar.Diff.UI -- --merge base.cs mine.cs theirs.cs
```

### Using it with git

Those two argument shapes are exactly what git passes its diff and merge tools, so it can be wired up
directly. Point `FubarDiff` at wherever you published it:

```bash
git config --global difftool.fubar.cmd 'FubarDiff "$LOCAL" "$REMOTE"'
git config --global mergetool.fubar.cmd 'FubarDiff --merge "$BASE" "$LOCAL" "$REMOTE"'
git config --global mergetool.fubar.trustExitCode false
git config --global diff.tool fubar
git config --global merge.tool fubar
```

Then `git difftool` opens a comparison per changed file, and `git mergetool` opens a three-way merge
per conflict. Save into **Right (mine)**, which is the working-tree file git is expecting you to
resolve — that is why `$LOCAL` lands on the right and it is the default destination.

`trustExitCode false` is deliberate: the app does not yet report resolution success through its exit
code, so git asks you whether the merge went well rather than assuming.

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
| `src/Fubar.Diff.Core` | Domain models, policy, and ports — `DiffLine`, `DiffResult`, `ComparisonOptions`, `HunkNavigator`, `ChangeGroupSlider`, the `Languages` scanner, `IDiffEngine`, `ITextFileReader`. BCL only. |
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

The three-way view has no diff map, unlike the two-way one — a merge asks "which of these needs me"
rather than "where are the changes", which the conflict count and next/previous answer directly, and a
map would have to colour its ticks from one of three columns with no right answer for the other two.
Merging more than two edits, and merging directories, are not built.

Move detection is exact: a block that moved AND was edited on the way is reported as an ordinary
change, because its two halves are no longer the same text. That is the conservative direction to be
wrong in — a mark that says "you can skip this" has to be right — but a similarity threshold would
catch more, and is the obvious next step.

Folder comparison copies but does not DELETE: there is no "make this side match that one", which
means removing what the other side does not have. That is deliberately still not built — a diff tool
that deletes the wrong file once is never trusted again, and the copy half delivers most of the value
with none of that risk.

**Cut.** A CLI with exit codes; semantic XML, YAML and CSV. Dropped deliberately rather than
forgotten. Patch export has since been built (toolbar → *Patch*), and git integration is partly there:
`--merge $BASE $LOCAL $REMOTE` is the argument order `git mergetool` passes, so it can be configured
as one.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the setup, the conventions, and the layering the
architecture tests enforce. By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md). Security issues: see [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE).
