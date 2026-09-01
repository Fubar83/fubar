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

git tag diff-v0.1.0-beta.1             # release ONE app; the tag prefix picks it (studio-v… for the other)
```

**Releases are per app.** `.github/workflows/build.yml` fires on `diff-v*` and `studio-v*` only, and a
tag naming no app is rejected rather than guessed at. Whether a release is a prerelease is DERIVED from
the version (a hyphen means one, per semver) rather than set by a flag, because a beta published as the
repository's "Latest release" is the one mistake here that reaches users. See README → Releasing.

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
- **DiffPlex is confined to `Fubar.Diff.Infrastructure`**, behind `IDiffEngine`.

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
