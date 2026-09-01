# Fubar Diff

[![CI](https://github.com/Fubar83/fubar/actions/workflows/ci.yml/badge.svg)](https://github.com/Fubar83/fubar/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A native, cross-platform desktop **diff tool** built on **Avalonia 12 + .NET 10**. Compare two files
side by side, with the panes locked in alignment and changes highlighted line by line.

It is a sibling of [Fubar API Studio](https://github.com/Fubar83/fubar) and shares its
design system, the [`Fubar.Controls`](https://github.com/Fubar83/fubar) package.

> **Status: early.** Two-way file comparison, semantic JSON and YAML, source-code comparison, structural
> C# comparison, three-way merge with an editable result, folder comparison, editing and save all work
> end to end. Editing the three-way merge's three INPUT panes, structural comparison for languages other
> than C#, and the other formats, are not built yet — see [Roadmap](#roadmap).

## Features

- **Two-editor side-by-side view**, with each side showing its own file's line numbers — so the
  numbers still match what is on disk across insertions.
- **Aligned panes.** Insertions and deletions get a placeholder row opposite them, and the two
  editors scroll in lockstep, so the columns cannot drift apart.
- **Align two lines by hand** when the aligner pairs the wrong ones. Put the caret on a line in each
  pane and press **Ctrl+Shift+A** (or View → *Align the two carets*): those two lines are paired, the
  regions either side of them are compared independently, and the status bar says how many pairings
  are in force — click it to clear them. This is the one thing no option can express. Ignore
  whitespace, ignore case, ignore comments all change what *counts* as a difference; none of them
  changes which lines *correspond*, and on a rewritten block or a reordered config that is the only
  thing wrong with the diff. Pairing two lines does not claim they match — a rewritten line still
  reads as changed — and the pairings are dropped when either file is replaced, since they were a
  statement about those two files.
- **Rules that live with the repository.** Drop a `.fubardiff.json` beside your code and everyone
  comparing those files — in the window, in CI, on a new laptop — gets the same answer:

  ```json
  {
    "ignoreWhitespace": true,
    "rules": [
      {
        "files": "*.json",
        "ignoredPaths": ["$.requestId", "$.timestamp"],
        "arrayKeys": { "$.users": "id" }
      },
      { "files": "*.min.js", "mode": "text" }
    ]
  }
  ```

  It is found by walking up from the file being compared, like `.editorconfig`, and the nearest one
  wins. What belongs in it is what is true of the *files* — which fields change on every run, what
  identifies a list's items, how a generated file should be read. What stays in Settings is what is
  true of the *reader*: the theme, reloading, how the Pretty button lays things out. Comments and
  trailing commas are allowed, because it is a file people edit by hand; a broken one is reported and
  then ignored, because a trailing comma should not cost you a comparison. The status bar says when a
  config is in force.
- **It runs without a window.** `FubarDiff --check old.json new.json` compares and exits — 0 if they
  match, 1 if they differ, 2 if the question could not be answered, which is what `diff` and
  `git diff --exit-code` mean and what a script author will assume. `--report out.html` writes a
  self-contained page; `--report out.json` writes something a gate can test; `--report - --report-format
  patch` pipes a unified diff. Every comparison option has a flag, including `--ignore-path`, so the
  CI check that matters — *"is this response the same apart from the request id and the timestamp?"* —
  is one line. `--help` for the rest.
- **Big files.** A million-line pair compares in about 1.4 seconds even with fifty thousand separate
  changes in it, and in a third of a second when the changes are in one place. Two 1.8 MB minified
  documents — one line each — take milliseconds as text and about 300 ms compared as JSON. The work is
  split rather than skipped: identical heads and tails are trimmed, what remains is cut at lines unique
  to both sides, and each piece is aligned on its own. The character-level highlight is the one thing
  given up on a line long enough to be a whole bundle; the line is still marked as changed.
- **Every difference is tinted, quietly.** Each changed row carries a low-contrast background — the
  removal colour on the left, the addition colour on the right — so a glance down either pane shows
  where the changes are, including lines that were merely edited rather than added or removed. The
  difference you are actually on is drawn stronger, with an accent bar and an outline around it, so
  "which one did F8 just take me to?" never has to be answered by colour density.
- **Character-level diff** inside modified lines, so a one-word change reads at a glance: the row says
  which line, the highlight says which words. In a language
  the tool knows, the split follows the language's own tokens: `==` becoming `===` highlights the whole
  operator rather than a lone third `=`.
- **Syntax highlighting**, for every language a TextMate grammar ships for, following the app theme.
  On by default; switch it off in Settings → General.
- **Source-code comparison** for **C#, JavaScript, TypeScript, Java, Go, C, C++ and Python**, picked
  from the file extension:
  optionally ignore comments (a changed comment stops being a difference; a comment-only line that was
  added is drawn faintly rather than counted — the code on the line still compares normally) and ignore
  blank lines. Block comments, verbatim and raw strings, and template literals are tracked across lines,
  so the inside of a multi-line comment is treated as a comment even where it reads like code. Both
  options are off by default and say so on screen when the pair is not a language they apply to.
- **Structural C# comparison** — the thing no other diff tool does. For a pair of C# files, Fubar Diff
  parses both with Roslyn and works out what happened **member by member**, in a panel beside the
  ordinary diff: *`Total` — method · changed and moved*, *`Render → Draw` — method · renamed*,
  *`Add` — method · reformatted*. Click one and both panes scroll to it, so a large file is navigated
  by meaning rather than by pressing Next through fourteen hunks.

  The answer worth the whole feature is the one at the top: **"No functional changes."** A file that
  someone ran a formatter over, reordered three methods in and rewrapped the comments of produces
  hundreds of changed lines, and looks *exactly* like a file with a bug fixed in it. Today the only
  way to tell them apart is to read every hunk. This says it in one sentence, in the panel and in the
  status bar. In CI, `--functional` makes it an exit code:

  ```bash
  FubarDiff --functional -q old/Api.cs new/Api.cs   # 0 if only formatting, order and comments changed
  ```

  Members are matched by signature first, then by name, then by an identical body — which is what
  turns a renamed method into *renamed* rather than into one large deletion beside one large
  insertion, and a moved one into *moved* rather than both. A member is only claimed to be a rename
  when its body appears exactly once on each side; where the answer is ambiguous, nothing is claimed.
  Move detection runs a longest-increasing-subsequence, so inserting a method at the top of a file
  does not mark every method below it as having moved.

  It **changes nothing about the text diff beside it**. A reformatted C# file genuinely differs on
  disk, a review is about those bytes, and a tool that quietly called the two identical would be lying
  about what it was shown. The line diff keeps saying exactly what changed; this says what it meant.
  On by default, and free for everything that is not C# — see Settings → General to turn it off.
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
- **Side-by-side or unified.** **View → Layout** switches a TEXT comparison between the two-editor
  view and a single patch-style document — removals then additions, shared context between them — for a narrow window, a
  screenshot, or anyone who reads patches all day. The unified view can **wrap long lines**, which the
  side-by-side one cannot: two columns stay aligned by having the same number of visual lines, and a
  line that wraps on one side alone would pull them apart.
- **Collapse unchanged** — long stretches both files agree on fold behind a placeholder, keeping a few
  lines either side of each change. On by default; click any fold to expand it, or turn it off from the
  toolbar (*Collapse*). A file of three thousand lines with two changes reads as two changes.
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
  edge to resize, or turn it off under View.
- **Change navigation** — next/previous with wrap-around (F7 / F8, or Alt+Up / Alt+Down). The current
  difference is marked with an accent bar and outline, so it stays findable among the other changes.
- **Merge and save** — take the left or right version of a change (Alt+Left / Alt+Right), then save.
  The file's encoding, BOM, line endings and trailing newline are preserved byte-for-byte.
- **Editable panes** — turn on *Edit* and type straight into either side; the diff re-runs as you
  pause. Taking a side is an edit too, so it applies immediately and **Ctrl+Z** takes it back along
  with anything you typed. Find gains Replace. Off by default, and side-by-side text comparisons only.
- **F5 refreshes the comparison**, and the status bar says when it needs to. With something typed,
  that means re-comparing what the panes now hold — never re-reading from disk, which would throw your
  edits away; with nothing typed it re-reads both files, which is what F5 after a build or a checkout
  is asking for. While an edit is waiting to be compared the status bar says **Diff out of date** and
  offers a Refresh button, so the counts on screen are never quietly describing the previous version.
  On a pair big enough that comparing takes a noticeable moment, switch off *Update the diff while you
  type* in Settings → General: the diff then waits for F5, and says so until you press it.
- **Both files are saved independently** — each side gets its own Save button when it has unsaved
  changes, Ctrl+S writes whatever changed, and *Save as* leaves the compared file alone. Closing a tab
  or the window with unsaved changes asks first, naming the files. If a file changes on disk while you
  have unsaved changes, you are asked which version wins rather than being told after the fact.
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
  endings. An unresolved conflict keeps the ancestor's text and says so in the status bar, both before
  saving and after.
- **The merged result is editable, not a preview.** A **Result** pane sits under the three columns
  showing the merged file as it currently stands, updated on every decision — and you can type into it.
  That matters because the answer to a real conflict is regularly *neither* side: two people edited the
  same line, and what belongs there is a third line that exists in neither file. Until there was
  somewhere to write it, the only way through was to resolve the conflict badly, save, and fix it in an
  editor afterwards. Once you have typed into it, Save writes **what that pane holds** rather than
  rebuilding from the decisions, and the status bar says *Hand-edited* so it is never a surprise.
  Clicking a resolve button after that would rebuild the document and discard what you wrote, so it
  asks first — and *Keep my edits* is the default, including if you dismiss the dialog.
- **Semantic JSON**: compares structure, not text. Reordered properties and reformatting are not
  differences; array elements are matched by an auto-detected identity key, so an element inserted
  mid-array marks only itself. JSON opens in the **Json** view by default - the change tree plus both
  documents, each shown exactly as given (a minified file stays minified, not reformatted) - where
  every difference is marked in both documents at once and stepping through them (the toolbar's ◀ ▶,
  F7 / F8, or a click in the tree) brings each one up strongly in turn, immune to formatting or
  property-order differences since neither side depends on
  lining up with the other. The buttons walk semantic changes here and text hunks elsewhere, so there
  is one pair of them wherever you are, and the status bar names the change you are on. An added or removed field highlights its key as well as its value; one whose value merely
  changed highlights the value alone. It has its own **Diff pane** too, the same close-up shown below
  the aligned Text view but built from the change's own location in each side's raw text rather than
  an aligned row range. Set **Compare** to **Text** for the aligned side-by-side view instead — which
  is also the only mode for anything that does not parse as JSON.
- **Semantic YAML**, through the same machinery. A `.yaml` or `.yml` file is read as structure, so a
  manifest whose keys were reordered between two branches reports the two things that changed rather
  than the whole file. Multi-document files (`---` separated, as Kubernetes manifests usually are)
  compare document by document; lists of objects are matched by an identity field like everything
  else; ignore paths, the change tree, the highlighting and the headless `--check` all work exactly as
  they do for JSON, because YAML is parsed into the same tree. `port: 8080` and `port: "8080"` are a
  number and a string and are reported as different — the change most likely to break something — and
  `country: NO` stays the string it was written as. YAML is chosen by **file name**, never guessed at:
  almost any text is valid YAML, so sniffing it would turn every log comparison into a comparison of
  two one-line documents. Force it with **View → Compare as → Yaml** (or `--mode yaml`) for a file
  that has no extension. Comments are not part of YAML's data model, so a change to one shows in Text
  mode and not here.
- **Right-click an array in the change tree** to choose how its elements are matched: by position, or
  by any field that could identify them — the auto-detected one first, then every other field that
  would actually work, plus a dotted path like `meta.id` for identity that is nested. A field missing
  from some element, or repeated across two, is never offered: it would silently fail to match. The
  choice is per array, because one document can hold a list of users where order means nothing beside
  a list of steps where order is the whole content.
- **A Pretty button on each JSON document** re-lays-out that side for reading — for a minified file
  next to a formatted one. Indent size or tabs, whether simple objects stay on one line, and key
  sorting are all in Settings. It changes nothing about the comparison and never touches the file, and
  numbers are written back exactly as authored.
- **A toolbar of eight things.** Open, ◀ ▶, three toggle buttons — *Whitespace*, *Collapse*, *Edit* —
  then **View** and **⋯**. That is the whole row, plus the merge and save buttons that appear when
  there is something to merge or save. The three toggles are the options reached for while reading a
  particular diff; **View** holds everything about what is on screen (compare as Auto / Text / Json,
  side-by-side or unified layout, the diff pane, wrapping); **⋯** holds patch export and Settings.
- **Settings you can read.** Every option is a row with a plain sentence under it saying what turning
  it on does — no hovering to find out what "Normalize Unicode (NFC)" was going to do to your files.
  Four groups: General (theme, reloading, updating while you type, syntax highlighting), What counts as
  a difference (whitespace, case, encoding, comments, blank lines), JSON (key order, list matching,
  null vs missing) and Display (invisible characters, reformatting, and how the Pretty button lays a
  document out). **Advanced** is collapsed and holds the three rules that need a pattern rather than a
  switch: ignored text (regular expressions whose matches stop counting — a build timestamp, a
  generated id), which field identifies a JSON list's items, and JSON paths never to report.
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
- **One way in: Open.** A single toolbar button (Ctrl+O) picks both files in one dialog — select two,
  or one to fill whichever side is free. The same menu replaces one side of an open comparison, swaps
  the two (for a pair that opened the wrong way round), reopens something recent, and starts a folder
  comparison or a three-way merge. There is no picker row taking up a band of the window: what is being
  compared is written on the tab.
- **Drag and drop** two files onto the window to compare them.
- **Tabs in the title bar** — several comparisons open at once (Ctrl+T / Ctrl+W), each with its own
  files, options and merge decisions, in the space the window was already spending on decoration. A tab
  is named for its pair and carries a dot while it holds unsaved changes.
- **A status bar rather than banners** — what the comparison found, whether the files have changed on
  disk (with the Reload button beside it), whether the diff is out of date, and whether anything is
  unsaved, all along the bottom. Only an error — the thing you just asked for did not happen — still
  gets a band across the top.
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
code, so git asks you whether the merge went well rather than assuming. (The headless mode below does
use exit codes, and means something different by them — "these files differ", not "the merge worked".)

### On the command line

The same executable answers without opening anything, as long as the arguments say so. Two file names
still open a window — that is what a difftool passes — so the headless modes are the flags that have
no meaning on screen: `--check`, `--quiet`, `--functional`, `--report`, `--help`, `--version`.

```bash
FubarDiff --check expected.json actual.json      # 0 same, 1 different, 2 could not tell
FubarDiff -q a.txt b.txt                         # the same, silently
FubarDiff --functional -q old/Api.cs new/Api.cs  # 0 unless the behaviour changed (C#)
FubarDiff --report diff.html src/a.cs src/b.cs   # a self-contained page for a build artifact
FubarDiff --report result.json a.json b.json     # fields a gate can test
FubarDiff --report - --report-format patch a b > changes.patch
```

`--functional` changes the question from "do these files differ" to "did anything *meaningful*
change" — see **Structural C# comparison** above. It is opt-in and separate from `--check` on purpose:
the default has to stay "do these bytes differ", because that is what a script author will assume, and
a check that quietly passed on a changed file is the worst thing a diff tool can do. It only answers
where the structural pass actually ran; anything else falls through to the ordinary answer rather than
being guessed at. A JSON report carries the same thing as a `code` object with a `noFunctionalChange`
flag, for a gate that wants to say more than pass/fail.

The exit codes are `diff`'s: **0** the files are the same, **1** they differ, **2** something went
wrong — a file that could not be read is never reported as a clean result. A format-only difference
(encoding, BOM, line endings) counts as different, because the files are not interchangeable even
though the panes would show the same text.

Every comparison option has a flag — `--ignore-whitespace`, `--ignore-case`, `--ignore-comments`,
`--mode json`, and `--ignore-path` as many times as needed. That last one is what makes this useful as
a gate rather than a curiosity:

```bash
FubarDiff --check --ignore-path '$.requestId' --ignore-path '$.timestamp' expected.json actual.json
```

Reordered keys are not differences, the two volatile fields are ignored, and the exit code answers the
only question that was being asked. A report goes to standard output with `--report -`, and the
summary line moves to standard error there so a piped patch stays a patch.

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

**Structural comparison is C# only, and the shape is built to take a second language.** The parser sits
behind `ICodeStructureParser` in Core, and the differ, the summary, the panel and `--functional` all
work on a language-neutral tree — a second language arrives as one adapter and nothing above it
changes. TypeScript and Java are the obvious next two. What is *not* free is the parser itself: the
value of this feature comes entirely from being right about what a member is and where it ends, and a
regex-shaped approximation would produce a confidently wrong answer, which is worse than no answer.
So a language is added when a real parser for it is, not before — the same rule `SourceLanguage`
already holds itself to.

**Open.** Editing is now built, but only in the side-by-side view of a text comparison — the unified
view has its own row numbering, the Json view shows each side unaligned, and a hex dump cannot be
written back as text.

The three-way merge's **result** is now editable — see *The merged result is editable* above. Its three
**input** panes are still read-only, and that half of the argument still holds: a merge's decisions are
keyed by region index, any edit to an input needs a full re-merge, and a re-merge renumbers those
regions and so discards every decision made so far — an edit half way through a long merge would
quietly throw the work away. There is also nowhere to save an edited input to; the window writes the
merged result to a destination you pick, not the three files it read. The result pane sidesteps all of
that by being downstream of the decisions rather than upstream of them, and it is what people actually
wanted from editing here: somewhere to write the line neither side has. Fix the decisions (keyed by
content rather than index, so they survive a re-merge) and editing the inputs becomes worth doing too;
until then, edit an input in a two-way comparison and start the merge again.

Very large files used to be the other gap and are now largely closed — see **Big files** below. What
remains is memory rather than time: both documents, the alignment and both editors are held at once,
which is why the reader still refuses anything over 64 MB. It is also why *Update the diff while you
type* is a setting — see Settings → General — rather than always on.

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
forgotten. Patch export has since been built (toolbar → *⋯* → *Copy patch* / *Save patch*), and git integration is partly there:
`--merge $BASE $LOCAL $REMOTE` is the argument order `git mergetool` passes, so it can be configured
as one.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the setup, the conventions, and the layering the
architecture tests enforce. By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md). Security issues: see [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE).
