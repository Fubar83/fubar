# CLAUDE.md

Guidance for Claude Code (and contributors) working in this repository.

## What this is

A monorepo holding two Avalonia 12 / .NET 10 desktop apps and the design system they share:

| Project | What it is |
| --- | --- |
| `src/Fubar.Studio.*` | **Fubar API Studio** — a native Postman/Insomnia alternative. Binary: `FubarAPIStudio`. |
| `src/Fubar.Diff.*` | **Fubar Diff** — a diff tool. Binary: `FubarDiff`. |
| `src/Fubar.Controls` | The shared design system + control library. Sandbox: `Fubar.Controls.Gallery`. |

These were three repositories until 2026-08-25, with `Fubar.Controls` shipped as a NuGet package. The
split was reversed when API Studio needed the **diff view** too: that made the sharing a mesh rather
than one-way, and every cross-cutting change would have needed several PRs plus a package publish.
Everything is now a project reference. **Nothing here is packed** — `Directory.Build.props` sets
`IsPackable=false` repo-wide.

Per-app detail lives in [`docs/api-studio.md`](docs/api-studio.md), [`docs/diff.md`](docs/diff.md) and
[`docs/controls.md`](docs/controls.md); changelogs are `docs/changelog-*.md`.

## Build / run / test

```bash
dotnet build Fubar.slnx                # everything (must be warning-clean)
dotnet test  Fubar.slnx                # every suite

dotnet run --project src/Fubar.Studio.UI                   # API Studio
dotnet run --project src/Fubar.Diff.UI -- left.json right.json
dotnet run --project src/Fubar.Controls.Gallery            # component sandbox

./build/publish-api-studio.ps1         # self-contained per-RID binaries (pwsh 7+)
./build/publish-diff.ps1
```

## Architecture

Both apps are clean-layered and **enforced by tests**. Dependencies point inward only:

```
Fubar.Controls          shared design system - depends on Avalonia + AvaloniaEdit + BCL, nothing else
      ▲ consumed by both apps
Presentation ── *.UI              Views + thin ViewModels + Composition root (DI)
Application  ── *.Application     Use-case / orchestration services
Core         ── *.Core            Entities + domain policy + PORTS (interfaces)
Infrastructure ── *.Infrastructure  Adapters implementing the ports
```

- `Core` depends on nothing but the BCL. `Application` and `Infrastructure` depend only on `Core`.
- `*.UI` depends on `Application` + `Core`; **UI ViewModels must NOT reference `*.Infrastructure`** —
  `Composition.cs` is the one allowed UI→Infrastructure edge in each app.
- **`Fubar.Controls` must not reference either app.** `Fubar.Controls.Tests.ArchitectureTests` holds
  an allowlist for this, and it matters MORE here than it did across repositories: with everything a
  project reference away, a stray `using Fubar.Studio.Core` in the library would compile fine and
  nothing else would object.
- **DiffPlex is confined to `Fubar.Diff.Infrastructure`**, behind `IDiffEngine`. The *language*
  scanner is not an engine and lives in `Fubar.Diff.Core/Languages` - it is hand-written, BCL-only
  domain policy (what a comment is), not an adapter over anything.

`tests/Fubar.Studio.Architecture.Tests` and `tests/Fubar.Diff.Architecture.Tests` fail the build on any
of this.

## Invariants that are easy to break

**Comparison keys are not display text** (Diff). The normalizer produces a key per line for matching;
`FileComparisonService` projects every row back onto the real document lines before rendering.
Skip it and "ignore case" shows the user a lower-cased copy of their own file. The same rule extends
to character spans, which is why they are computed *after* projection.

**Filler-line discipline** (Diff). Editor line `i` is always `DiffResult.Lines[i]`, on both sides.
Never read the editors back to save — go through `MergedDocument`, or the filler blanks get written
into the user's file. Both sides having equal line counts is also what makes scroll sync a plain
offset copy rather than a line-mapping scheme. **This invariant is deliberately NOT preserved by
`AlignedText.BuildCompact`** — the stacked Diff pane shows each side as its own compact block with no
filler at all, since a stacked layout has no row-count-parity requirement to protect. Only the
side-by-side main panes (`AlignedText.Build`) need fillers; do not "fix" `BuildCompact` to add them.

**Semantic JSON is a refinement, not a second pipeline** (Diff). The text differ decides how lines
line up; `JsonSemanticPass` decides which of them matter. One `DiffResult` shape means every renderer,
the diff map, navigation and merge work in both modes. The trap here: `SemanticChanges` (from
`JsonSemanticDiffer.Compare`) is unaffected by alignment quality - it parses and diffs the AST
directly - but `Result.Modified`/`Inserted`/`Deleted` (the ROW-level counts) come from
`SemanticLineFilter` acting on the raw text alignment, so those can look strange when the two sides
are formatted very differently (a minified file against a pretty one). That is accepted, not a bug to
chase: Text mode never reformats a file to fix its own alignment (see below), and the Json view is
what handles that pairing properly.

**Text mode never reformats a file - not even JSON, not even automatically** (Diff). It used to
pretty-print both sides before alignment whenever semantic comparison applied, specifically so a
minified-vs-pretty pair still lined up; that was removed because it silently rewrote the user's
content to compensate for Text mode's own limitation. The Json view exists for exactly that pairing
and needs no reformatting to handle it (see below), so Text mode is free to just show what it was
given. The one surviving way to reformat for display is the pre-existing, explicit "Reformat" toggle
(`ComparisonOptions.NormalizeStructure`, labeled "Normalize XML" until it was made visible for JSON
too - see below) - opt-in, and `TextLineNormalizer`'s diff-aware pretty-printer (`PrettyPrintJson`,
keeping all-scalar containers on one line so an array of small objects like `{"id": 1}` does not
explode into boilerplate braces a line differ then mismatches) still backs it. Because canonicalised
output IS what gets displayed (`FileComparisonServiceTests.Canonicalisation_output_IS_displayed`), a
Take Left/Take Right + Save after turning this on saves the reformatted text - which is the point: the
user opted in, so it is fine for it to stick if they then choose to save. Do not reintroduce an
unconditional "canonicalize before alignment" step - that is precisely what was removed, and why.

**Ignore rules are applied where differences are DECIDED, not where they are drawn** (Diff).
`JsonSemanticDiffer.Compare` marks changes through `JsonIgnoreRules` before returning, so the tree, the
text view's line filter, the diff map and navigation all agree. Filtering in a view instead would make
that view disagree with the others about what changed.

**Fubar Diff has no click-to-ignore affordance - `DiffPaneViewModel.IgnorePathCommand` is left null on
purpose** (Diff). It exists for API Studio, where a comparison belongs to a request that can remember
the rule; Fubar Diff's own way to set `IgnoredPaths` is the manual list in `SettingsWindow` instead.
Before that window existed, `IgnoredPaths`, `IgnoreNullVsMissing` and `ArrayKeyOverrides` were fully
built in Core (`JsonComparisonOptions`, `ArrayKeyResolver`) and even had persistence fields waiting in
`AppSettings`, but `ComparisonViewModel.CurrentOptions()`/`ApplyDefaults`/`CaptureOptions` never
actually read or wrote any of them - the feature was completely inert from the UI's side despite every
other piece of it working. Check that a Core option is *read somewhere in `ComparisonViewModel`* before
assuming a UI gap here means "not built yet" - it may mean "built, but never wired to a control."

**The Json view has no alignment at all, on purpose** (Diff). `RawJsonPane` shows each side's raw,
unaligned text and highlights the current change's own `JsonAstNode.Span` directly - no fillers, no
line-for-line correspondence between the two sides. This is what makes it immune to the class of
problem noted above: there is no shared line numbering for a formatting or property-order difference
to break. Do not "simplify" it by routing this view through `AlignedText` - that would reintroduce
exactly the dependency it exists to avoid. There is no standalone Tree mode any
more - `Text` and `Json` are the only two `DiffViewMode` values, and `DiffPaneViewModel.Show` picks
between them itself (Json whenever semantic comparison ran) rather than leaving whatever was
previously selected.

**The Json view has its own detail pane, built from spans rather than rows** (Diff). `DiffDetailPane`
(Text mode) excerpts a hunk's aligned ROWS via `AlignedText.BuildCompact`, which only makes sense where
rows are aligned in the first place. `JsonDetailPane` is the Json-mode counterpart: it excerpts a
change's own lines directly from `LeftRawText`/`RightRawText` via `JsonSpanExcerpt.Build`, and reuses
`RawJsonPane` (not `DiffEditorPane`) for the same reason the main Json panes do - no cross-side line
correspondence to preserve. Both panes are toggled by the same "Diff pane" checkbox
(`DiffPaneViewModel.IsDetailVisible`); `JsonView.axaml.cs` collapses its row heights identically to
`DiffView.axaml.cs` (duplicated on purpose - the two views are otherwise independent, and the collapse
logic is small enough that sharing it is not worth a base class). One consequence of the highlight
being line-range-only (see above): for a minified, single-line file, the excerpt on that side is the
*entire* line, since there is no column-level highlighting to narrow it further - not a bug, just what
"the change's own lines" means when the whole document is one line.

**Two semantic change lists exist for a reason - do not collapse them** (Diff). `FileComparison`
carries `SemanticChanges` (spans into `Left`/`Right`, used by Text mode's line filter, ignore rules and
the tree) and `OriginalSemanticChanges` (spans into `OriginalLeftText`/`OriginalRightText` - each
side's text exactly as given - used by the Json view's highlighting). Now that Text mode no longer
reformats JSON automatically, the two are usually IDENTICAL text; they can still diverge when the
user explicitly turns on "Reformat" for a JSON file, and the pairing has to stay correct for that
case too. They are guaranteed to agree on path, kind and count regardless - canonicalizing never
reorders or renames anything - which is what lets `DiffPaneViewModel` pair the tree (built from the
first list) with navigation (walking the second) by matching `JsonPath` strings rather than the
`JsonChange` objects, whose spans can legitimately differ between the two. `RecompareAsync`/`Recompare`
thread the original text through explicitly from the previous result rather than recomputing it from
`Left`/`Right` - that would silently substitute the canonicalized text the moment NormalizeStructure
was toggled after the first render.

**A three-way merge REUSES the two-way aligner rather than aligning three documents** (Diff).
`ThreeWayMerger` is handed two ordinary `IDiffEngine` alignments - ancestor against each edit - and
reads only their `Unchanged` rows. Wherever both agree a line survived, all three documents are
synchronised; everything between two such points is one region to classify. This is not just less code
than a three-way alignment: it is what makes a merge agree with the two-way diff of the same files,
because every comparison option, every code rule and the slider are already baked into the keys and
rows before the merge looks at them. Two consequences that read as bugs and are not. (1) A `Modified`
row is NOT a match - the aligner paired two lines that differ, and taking that as "survived" would
merge one side's edit away silently. (2) Two edits with no surviving line between them are ONE region,
so adjacent changes from both sides become a single conflict rather than two decisions whose answers
would have to agree with each other; git resolves it the same way.

**Three-way rows produce the same `AlignedDocument` the two-way view uses, and that is the whole
budget** (Diff). `ThreeWayAlignedText.Build` emits exactly what `AlignedText.Build` does, so
`DiffEditorPane`, `CharSpanColorizer`, `SourceLineNumberMargin` and `ChangeLineBackgroundRenderer`
needed no knowledge of merging at all - a third pane cost one view and one view model. The filler
discipline extends with it: row `i` is `ThreeWayResult.Lines[i]` in ALL THREE editors, which is what
keeps scroll sync a plain offset copy and makes a region one horizontal band. The tint mapping is
deliberate and worth not "fixing": the ancestor column is tinted as removed, a side that MOVED is
tinted as added, and a side that did not move is left untinted even inside a region. Tinting all three
columns everywhere would hand the single question a merge asks - who moved? - straight back to the
reader. Character spans are computed against the ANCESTOR for both edits, never left-against-right: a
merge IS two independent sets of changes to one starting point, and in a conflict "what did each of
them do" is the question, where a left-vs-right span would show the disagreement while hiding that both
may have rewritten the line. The ancestor column carries no spans of its own - it is already tinted
whole as the text being replaced, and a third set of highlights would ask the reader to cross-reference
three things to answer one question.

**`IsConflict` is a flag on `AlignedLine`, not a fifth `ChangeKind`** (Diff). Same reasoning as
`IsIgnored`, which it sits beside: a conflicting row is an ordinary `Inserted`/`Deleted` row to every
renderer, hunk-grouper and navigator, and a fifth kind would land in every exhaustive switch over the
four that exist. `ChangeLineBackgroundRenderer` checks both flags BEFORE the by-kind lookup, for
opposite reasons - an ignored row would otherwise get no tint (its Kind is `Unchanged`), and a
conflicting row would otherwise get the SAME tint as the changes that need no decision, which is the
one thing a merge view must not do.

**An unresolved conflict saves the ANCESTOR, and the UI must say so** (Diff).
`ThreeWayMergedDocument` has a defined answer for a region nobody decided, and `MergeService` does not
refuse to write one - stopping half way through a long merge to save what you have is legitimate, and
a service that threw would make it impossible. That makes it the UI's job: `MergeViewModel` shows a
banner before (`HasUnresolvedConflicts`) and names the count in the status line after. Do not "fix"
this by throwing in the service, and do not drop the warnings - the fallback is only acceptable while
it cannot be a surprise.

**Sliding a change group is a PRESENTATION pass, and its safety comes from one rule** (Diff).
`ChangeGroupSlider` moves a run of added or removed lines to the placement that reads best, and it is
allowed to because the diff is genuinely AMBIGUOUS there: when a group is bounded by lines identical to
the ones just inside it, several placements describe the same two documents and every one is equally
minimal, so the aligner had no grounds to prefer one. It only ever moves a group across a line
IDENTICAL (under the comparison keys) to the one leaving it, which is what makes both documents, the
counts and the hunk count provably unchanged - only the pairing of equal lines moves. Two things follow.
It runs BEFORE projection, so it compares keys rather than display text (with "ignore case" on, two
lines the user can see differ were matched as equal, and the slider has to agree with the diff that
already made that call) - but it SCORES on the display lines, because indentation is the whole signal
and trimming it is exactly what a key may have done. And an ignored row is deliberately not slideable
context: it is drawn faintly precisely so the reader can see where it is.

**Do not reach for a cleverer alignment algorithm before measuring** (Diff). Patience diffing was built
here, behind `IDiffEngine`, decorating `DiffPlexDiffEngine` - and then removed, because on measurement
it produced an answer identical to DiffPlex's on every realistic C#/TS/JSON case tried (a method
inserted between two others, a nested block added, a switch case added, an appended arrow function, a
JSON object appended), and on the one case where it differed - a moved method - it was not better,
merely differently shredded. DiffPlex is not a naive LCS and does not have the brace-matching failure
patience is famous for fixing. The thing that DID fix the moved-method case is the slider above, which
is a post-pass over ANY aligner's output. If a diff reads badly, check whether the alignment is wrong
or merely badly PLACED before replacing the engine - they need different fixes, and only one of them
was actually the problem here.

**Comment stripping produces a KEY, and keys are not display text** (Diff). The same rule as the
normalizer, and the one most likely to be broken by a "helpful" change: with "ignore comments" on,
`CodeLines.ComparisonLines` is the document with its comments removed, and `FileComparisonService`
projects the real lines back over every row before anyone sees them. The stripping also takes the
whitespace immediately BEFORE a comment - without that, `foo(); // note` reduces to `foo(); ` and still
fails to match the same line written without the comment, which is the entire point of the option.

**Scanning a line for comments needs the whole document** (Diff). `SourceScanner.Scan` threads state
across lines because a line cannot be classified on its own: the middle of a `/* … */`, of a C#
verbatim string or of a JS template literal reads as ordinary code in isolation. `ScanLine`'s
single-line overload exists ONLY for the inline differ, which is handed two already-matched lines with
no document around them and where being wrong costs a slightly worse highlight rather than a wrong
answer. Anything deciding what a line MEANS must use `Scan`.

**Highlighting is keyed by file EXTENSION, comparison by `SourceLanguage`, and they know different
amounts** (Diff). The scanner claims a language only where it has real rules (C#, JS, TS); TextMate
colours anything it ships a grammar for. A Python file gets nothing from the code rules and is still
far easier to read coloured, so `DiffEditorPane.SyntaxExtension` takes an extension rather than a
language. Tying them together would mean either colouring nothing outside the short list or claiming to
compare languages we cannot scan. `DiffEditorPane` installs TextMate on FIRST USE, not in its
constructor, and swallows grammar failures - highlighting is a reading aid layered over the thing the
user actually opened the app for, so it must degrade to plain text rather than take the pane down. That
silence is why `Fubar.Diff.Controls.Tests` asserts the grammars resolve at all: a missing one would
look exactly like a `.log` file, forever.

**An ignored row is `Unchanged` + `IsIgnored`, never its own `ChangeKind`** (Diff). That is what keeps
it out of `IsChange`, and therefore out of hunks, counts, the diff map and F7/F8 — while still letting
a renderer draw a faint band. Promoting it to a `ChangeKind` would silently put every ignored row back
into the hunk list and make navigation stop on the fields the user asked not to see.
`IgnoredRowNavigationTests` pins this.

**Comparison settings inherit PER SETTING, not per level** (Studio). `ComparisonSettings` has every
member nullable precisely so a request overriding one option keeps inheriting the rest;
`ComparisonSettingsResolver.Resolve` folds global → folder(s) → request and reports, for each setting,
both the value and which level it came from. Layers are ordered root-most first (the same order
`GetInheritanceChainAsync` already produces for headers), so "last one wins" means "closest wins". Two
traps: (1) lists REPLACE rather than union - an empty non-null list is a real override meaning "ignore
nothing here", which is what keeps a request's rules readable as the complete truth about that request;
(2) `Studio.Core` must NOT reference `Fubar.Diff.*` (the architecture tests enforce it), which is why
`ComparisonSettings` is a parallel shape rather than a reuse of `ComparisonOptions` -
`ComparisonSettingsMapper` in `Studio.UI` is the single place the two vocabularies meet, and adding a
setting to one side should break its compile until the other side has it too.

**A format-only difference is a REAL difference, and the lines cannot show it** (Diff). The reader
consumes the BOM as a preamble and splits on every terminator, so a UTF-8-with-BOM file and its
BOM-less twin - or CRLF vs LF, or UTF-16 vs UTF-8 - produce byte-identical `Lines` and an empty
`DiffResult`. `TextFormat` captured all of this from the start but nothing ever COMPARED two of them,
so the tool said "the files are identical" about files that were not, which is the worst possible
answer right after someone's version control told them otherwise. `TextFormatComparer` (Core) decides
it, `FileComparison.FormatDifference` carries it, and the UI reports it in both the status line and a
banner - the banner matters because when it is the ONLY difference there is nothing else on screen to
notice. Do not fold this into `DiffResult.AreIdentical`: that is about content, and conflating the two
would make every hunk-counting consumer wrong.

**The comparison pipeline is fast; measure before "optimising" it** (Diff). A 60,000-line source
comparison takes about 90 ms end to end, of which ~65 ms is DiffPlex's own aligner and ~15 ms is
everything this codebase adds (scanner, code rules, slider, projection). The JSON path costs ~2 ms on
a file that is not JSON, which is where the obvious-looking waste is - four whole-document
`string.Join`s and two parse attempts - and it is not worth removing. `PipelineScaleTests` guards the
thing that WOULD matter: the budgets there are absurdly generous on purpose, because they exist to
catch an accidentally quadratic scan (60 ms becomes minutes) rather than a 20% regression, and a
timing assertion tight enough to catch the latter fails on a loaded CI agent instead.

**Ports live in Core, adapters in Infrastructure**, wired in each app's
`Infrastructure/ServiceCollectionExtensions.cs` and `UI/Composition.cs`.

## Conventions

- **Domain policy lives in Core, not ViewModels** — e.g. `HunkNavigator`, `MergedDocument`,
  `AuthApplier` — so the rules are testable without a UI.
- **MVVM** via CommunityToolkit.Mvvm source generators; `ViewModelBase : ObservableObject`.
- **Style classes bind as `Classes.name="{Binding Flag}"`** — Avalonia's `Classes` is not bindable, so
  view models expose a bool per class rather than a class-name string.
- **Generic UI belongs in `Fubar.Controls`** (with a Gallery page). Anything that knows a domain
  concept — what a hunk is, what a request is — stays app-side.
- **Central Package Management**: versions live in `Directory.Packages.props`; reference packages
  without a `Version`.
- **Keep it warning-clean**; analyzers are on repo-wide and CI builds + tests every push/PR.

## Gotchas

- **`Application` name collision**: the `Fubar.Studio.Application` / `Fubar.Diff.Application`
  namespaces shadow Avalonia's `Application` type inside `Fubar.*` code (a namespace member outranks a
  using-alias, so an alias cannot fix it). Qualify Avalonia's as **`Avalonia.Application`**.
- **Build fails with locked DLLs while an app is running** → `taskkill //F //IM FubarDiff.exe` (or
  `FubarAPIStudio.exe`) first.
- **A style not merged into `Themes/Fubar.Controls.axaml` does nothing** — the usual cause of "my
  control renders unstyled".
- **Viewport size must come from `TextView.DefaultLineHeight`, not `VisualLines.Count`** — a document
  shorter than the pane reports only the lines it drew, which collapses the diff map's scale.
- **Background renderers paint in registration order** (Diff). `CurrentHunkRenderer` is added *after*
  `ChangeLineBackgroundRenderer` on the same layer so the current-difference marker lands on top of
  the change tint. Swap the order and it disappears under it.
- **`DiffLineColors`' `DiffEmphasis` parameter is what makes the current difference read as CURRENT**
  (Diff). Three levels, not a bool: `Faded` for a real change that is not the one just navigated to
  (main panes only - a hunk outside `ChangeLineBackgroundRenderer`/`CharSpanColorizer`'s
  `SetCurrentRange`), `Normal` for the current hunk in the main panes, `Emphasized` for the two "Diff
  pane" close-ups (DiffDetailPane, JsonDetailPane - `DiffEditorPane.Emphasized` / `RawJsonPane.Emphasized`,
  `false` by default, `True` only in those two close-ups' own XAML). `LineBackground` is never called
  with `Emphasized` - `ChangeLineBackgroundRenderer.Draw` skips itself entirely when emphasized (see
  below) - so only `SpanBackground` actually has three meaningfully different levels; `LineBackground`
  only ever sees `Faded` or `Normal`.
- **The two Diff pane close-ups have NO full-line tint at all, in either mode** (Diff). Text mode:
  `ChangeLineBackgroundRenderer.Draw` returns immediately when `_emphasized`, so `DiffLineColors.LineBackground`
  is dead code there regardless of `ChangeKind` - Modified rows already lost their line tint earlier
  (only `SpanBackground` tints them, precisely, since a full-row wash competed with that rather than
  helping), and Inserted/Deleted rows now lose it too. Since a whole inserted/deleted row normally
  carries NO character spans at all (the full-line tint used to say "this whole row is the diff" on its
  own - see `FileComparisonServiceTests.Only_modified_rows_get_inline_spans`), `CharSpanColorizer`
  synthesizes one covering the row's entire text when `_emphasized` and `Spans.Count == 0`, so the
  close-up still shows something. Json mode: `RawJsonPane.Emphasized` swaps `CurrentHunkRenderer` (a
  full-width band, still used by the MAIN Json panes) for `SpanTextColorizer`, which highlights only the
  exact characters a `SourceSpan` covers using its `StartColumn`/`EndColumn` - the first renderer to
  actually use those columns; every other consumer of `SourceSpan` in this codebase only reads the line
  range. Do not restore a full-line/full-width wash to either close-up "to make it easier to scan" -
  that is precisely what both changes were replacing with something more precise.
- **`TextEditor.ScrollToVerticalOffset` silently clamps to the CURRENTLY KNOWN extent, not the whole
  document** (Diff). Calling it for a line AvaloniaEdit has never scrolled towards is a no-op - the
  ScrollViewer only learns the document is that tall once something (`ScrollToLine`) asks it to make
  that position visible first. `EditorScroll.CenterOnLine` calls `ScrollToLine` before
  `ScrollToVerticalOffset` for exactly this reason; dropping the first call silently breaks centering
  for any line far from wherever the pane last scrolled, with no exception and no warning - it just
  quietly stays put. Confirmed by adding temporary logging, not by reading docs; do the same before
  "simplifying" this away again.
- **Collapsing a `Grid` row needs its `RowDefinition` height zeroed**, not just `IsVisible=false` on
  the child — `DiffView`'s detail pane would otherwise leave a 190px blank band.
- **`git mergetool` passes `$BASE $LOCAL $REMOTE`, and LOCAL is the RIGHT-hand side** (Diff). LOCAL is
  "mine" - the file being merged into - which is the right-hand column by the convention the two-way
  window already set; REMOTE is "theirs" and goes left. `StartupFiles.FromArgs` therefore does NOT
  pass its arguments through in order, and the swap is invisible to any test whose left and right
  files are interchangeable — it shipped wrong once and a smoke test with a symmetric argument order
  did not notice. `StartupFilesTests` pins it with three distinguishable names.
- **An owned window cannot be shown before its owner is** (Diff). `Window.Show(owner)` throws "Cannot
  show window with non-visible owner" from `OnFrameworkInitializationCompleted`, where `MainWindow` has
  been constructed but not yet displayed — which is exactly where opening `--merge`'s window belongs.
  `App` defers it to the main window's `Opened` event for this reason; the exception is immediate and
  fatal, not a silent misbehaviour, so it will find you.
- **Settings never throw**: `Load` returns defaults, `SaveAsync` returns false. Losing a preference is
  a nuisance; refusing to start over a corrupt settings file is not acceptable.
- **`ExecutionSnapshot.ResponseBody` is optional and must stay that way** — null for an empty body, one
  over `HistoryBodyPolicy`'s cap, and every ledger written before the field existed. Anything reading
  it needs the disabled path, not a `!`. The cap is why: 200 entries per request times an unbounded
  response would turn a workspace into a cache nobody asked for.
- **The pinned response (`IResponseBaselineService`) is in-memory only.** It is a scratch comparison;
  persisting response bodies outside the workspace's own history would put whatever they contain
  somewhere the user did not choose. It is a singleton so it survives switching request — which is
  also why panes must unsubscribe from it on dispose.
- **Avalonia 12 renamed drag-drop types**: `DragEventArgs.Data` is now `DataTransfer`, typed
  `IDataTransfer`, with files via `TryGetFiles()`.
- **Test both theme variants.** A token defined only in Dark throws at runtime in Light.
- **A control that cannot do anything right now should be HIDDEN, not disabled** (both apps). The
  codebase argued this for one control ("Recent is hidden rather than disabled when empty: an
  always-greyed control on first run is just clutter") and it is now the general rule: Fubar Diff's
  merge group binds `IsVisible` to `Pane.HasCurrentHunk` and its save group to `HasUnsavedMerge`, so
  neither occupies the toolbar during the many sessions that are only ever a read. The file pickers
  collapse to a one-line summary after a successful compare (`ComparisonViewModel.IsFileRowExpanded`)
  for the same reason. Do not "restore" these to always-visible-but-disabled - the row they cost is a
  row of diff, which is the thing the app exists to show.
- **The "Reformat" checkbox (`NormalizeStructure`) used to be labeled "Normalize XML" and hidden
  whenever `Pane.IsSemantic` was true** - i.e. hidden exactly for JSON, the one format users most want
  to reformat. It backs both XML and JSON already (`TextLineNormalizer.Canonicalize`); the bug was
  purely the toolbar's `IsVisible` binding. It is now unconditionally visible, like "Ignore whitespace"
  and "Ignore case" - it is a no-op on content that is neither, which is fine.

## Workflow notes

- Commit/push only when asked; branch off `main` first if needed.
- The design docs in `docs/` (LeftPane / RequestEditorPane / ResponsePane) are the canonical behaviour
  spec for API Studio's panes.
