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
dotnet run --project src/Fubar.Studio.UI -- --run --report results.xml   # run a collection; 0 pass, 1 fail, 2 could not
dotnet run --project src/Fubar.Diff.UI -- left.json right.json
dotnet run --project src/Fubar.Diff.UI -- --check left.json right.json   # headless; 0 same, 1 differ, 2 failed
dotnet run --project src/Fubar.Diff.UI -- --functional -q a.cs b.cs      # 0 unless the C# behaviour changed
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
- **DiffPlex is confined to `Fubar.Diff.Infrastructure`**, behind `IDiffEngine`, and **Roslyn**
  (`Microsoft.CodeAnalysis.CSharp`, syntax only) likewise, behind `ICodeStructureParser`. The *language*
  scanner is not an engine and lives in `Fubar.Diff.Core/Languages` - it is hand-written, BCL-only
  domain policy (what a comment is), not an adapter over anything.

`tests/Fubar.Studio.Architecture.Tests` and `tests/Fubar.Diff.Architecture.Tests` fail the build on any
of this.

## Invariants that are easy to break

Behaviour that looks like a detail, is not, and has usually already been broken once. This was ~875
lines in this file, of which roughly 85% described the diff engine — which an API Studio contributor
no longer depends on at all. Split per app:

- **[docs/invariants-diff.md](docs/invariants-diff.md)** — 61 entries. Alignment and filler
  discipline, semantic JSON, three-way merge, the location map, folder and binary comparison,
  scrolling and highlighting.
- **[docs/invariants-studio.md](docs/invariants-studio.md)** — 7 entries. The CLI/window split,
  OpenAPI import, collection runs, and the comparison-settings hierarchy.

**Ports live in Core, adapters in Infrastructure**, wired in each app's
`Infrastructure/ServiceCollectionExtensions.cs` and `UI/Composition.cs`. Since the Roslyn adapter
moved to `Fubar.Diff.Infrastructure.Code`, *which project references an adapter* is itself a
constraint — `Fubar.Studio.Architecture.Tests` fails if the C# compiler reappears in API Studio's
output.

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
- **With NOTHING selected, every change draws `Faded`** (Diff). Both `Emphasis` helpers
  (`ChangeLineBackgroundRenderer`, `CharSpanColorizer`) treat "outside the current range" and "there is
  no current range" the same way. It used to be the opposite - a negative range meant everything drew
  at `Normal` - which was survivable when only inserted/deleted rows were tinted, and became a wall of
  colour once every changed row got a background. A document nobody has navigated yet should read as
  one even wash saying "the changes are here", with nothing pretending to be the current one.
- **Every changed row gets a line tint, and a MODIFIED row takes the colour of its own side** (Diff).
  `LineBackground` returned null for `Modified` for a long time, on the argument that the row's
  character spans are more precise than a full-row wash - true, but it left the commonest kind of
  change with no row-level mark at all, so scanning for "which lines changed" worked for insertions
  and not for edits. Both now: the row says where (`LineOpacity`, 0.12/0.28), the span says what
  (0.30/0.55), and the gap between them is what keeps the span the louder of the two -
  `ChangeTintTests` pins that ordering. Which colour a modified row takes comes from
  `DiffEditorPane.Side` (removal colour on the left, addition colour on the right), NOT from the row's
  own spans: deriving it from `Spans[0].Kind` was tried and is wrong, because a line that only had text
  added to it has no deleted spans on the left, so half the modified rows in an ordinary diff fell
  through to the neutral fallback and came out a third colour. A pane that is neither side (the
  unified view, a three-way base column) leaves `Side` null and gets that fallback.
- **The two Diff pane close-ups have NO full-line tint at all, in either mode** (Diff). Text mode:
  `ChangeLineBackgroundRenderer.Draw` returns immediately when `_emphasized`, so `DiffLineColors.LineBackground`
  is dead code there regardless of `ChangeKind` - a close-up is a pane full of nothing BUT the current
  difference, where a band across its whole width says nothing the pane's own border does not.
  (The MAIN panes are the opposite case and tint every changed row - see above.) Since a whole
  inserted/deleted row normally
  carries NO character spans at all (the full-line tint used to say "this whole row is the diff" on its
  own - see `FileComparisonServiceTests.Only_modified_rows_get_inline_spans`), `CharSpanColorizer`
  synthesizes one covering the row's entire text when `_emphasized` and `Spans.Count == 0`, so the
  close-up still shows something. Json mode: `RawJsonPane.Emphasized` swaps `CurrentHunkRenderer` (a
  full-width band, still used by the MAIN Json panes) for `SpanTextColorizer`, which highlights only the
  exact characters a `SourceSpan` covers using its `StartColumn`/`EndColumn` - the first renderer to
  actually use those columns; every other consumer of `SourceSpan` in this codebase only reads the line
  range. Do not restore a full-line/full-width wash to either close-up "to make it easier to scan" -
  that is precisely what both changes were replacing with something more precise.
- **The Json panes mark EVERY change, not just the current one** (Diff). `JsonChangeSpanColorizer`
  paints each change's own `SourceSpan` faintly and the current one at full strength;
  `CurrentHunkRenderer` still bands and brackets the current change's lines on top. Character spans
  rather than full-width bands, unlike the aligned views: a Json document is unaligned and one line
  routinely holds several properties, so banding the line would claim the whole of
  `{"a": 1, "b": 2}` changed when only `b` did. It must be fed `DiffPaneViewModel.SemanticChanges` -
  the list whose spans address each side's RAW text - never the canonicalized list, which is a line or
  two out as soon as "Reformat for display" is on. The close-up (`JsonDetailPane`) passes no changes at
  all: it shows an excerpt renumbered from line 1, so whole-document spans would land on whatever text
  happened to sit at those numbers.
- **The same executable is a window AND a batch tool, and only unambiguous flags choose the second**
  (Diff). `CommandLine.IsHeadless` is checked in `Program.Main` before Avalonia is configured, because
  a run that must exit with a status code cannot also be showing a window. The list is deliberately
  short - `--check`, `--quiet`/`-q`, `--report`, `--report-format`, `--help`, `--version` - and two
  bare file names or `--merge` are NOT on it: those are what `git difftool` and `git mergetool` pass,
  and turning one into a silent batch job would break every git integration with no error to go on.
  Exit codes are `diff`'s (0 same, 1 different, 2 could not tell) and a format-only difference counts
  as different. On Windows a GUI executable has no console at all until `ParentConsole.Attach` runs.
- **`.fubardiff.json` is for facts about FILES, not preferences about reading** (Diff). Ignored paths,
  array keys, ignored patterns, the comparison mode - things that are true for the whole team and
  every checkout. The theme, auto-reload and the Pretty button's layout stay in `AppSettings`, which
  is per machine. It is applied in two places (`ComparisonViewModel.CurrentOptions` and `CliRunner`)
  rather than inside `FileComparisonService`, because the service is also entered by paths that carry
  options captured earlier (a re-diff after an edit) and applying it there would make the rules come
  and go. Composition rule: single values are overridden by the later rule, lists ADD - including to
  whatever the session already has. A broken config is reported and ignored, never fatal.
- **A user's alignment anchor is an instruction, not a hint** (Diff). `ComparisonOptions.Alignments`
  is honoured absolutely by `DiffPlexDiffEngine`, at any size, by splitting the documents there and
  aligning each region independently (`SegmentedLineAligner.AlignAround` - the same machinery as the
  large-file path, which finds its anchors instead of being given them). Two rules that look like
  details and are not: the anchored row is `Modified` unless the two lines are genuinely equal,
  because "these correspond" is not "these match" and marking a rewritten line unchanged would hide
  the difference the user was lining up to read; and anchors are dropped when a PATH changes, because
  they describe two particular files. `AlignmentAnchors.Add` resolves conflicts by dropping what the
  new anchor crosses - refusing it would leave the user hunting for a forgotten decision.
- **The cost of a big comparison is the ALIGNMENT, not the rendering** (Diff). Measured before
  guessing, and the guess was wrong: on a 1,000,000-line pair the pipeline took 15.8 s, of which 15.5
  was one call into the diff engine - reading, normalising, inline spans, building both aligned
  documents and computing folds came to under 800 ms between them. `SegmentedLineAligner` is the fix
  (trim the identical head and tail, split the rest at lines unique to both sides, align each piece),
  used only above `DiffPlexDiffEngine.SegmentedFrom` so ordinary comparisons keep byte-identical
  output. Before optimising anything here, measure - the scratch benchmark shape is in the commit that
  added this.
- **Never ask `SideBySideDiffBuilder` for an alignment** (Diff). It runs a WORD-level diff for every
  modified line to fill in sub-pieces this codebase does not read (character spans come from
  `DiffPlexInlineDiffEngine`, computed on display text rather than comparison keys). Two 1.8 MB
  minified documents took 68 seconds, essentially all of it inside a word diff whose output was
  discarded; going straight to `IDiffer.CreateDiffs` with a `LineChunker` and pairing the blocks up
  by hand is 13 ms. The pairing rule - first min(deleted, inserted) lines of a block become modified
  rows, the remainder one-sided - is the builder's own, and must stay that way.
- **Anything that walks one document's properties against the other's must not use `Find` naively**
  (Diff). It looks like an O(1) lookup and was a linear scan; every caller is inside a loop over the
  other side, so a 120,000-property minified document spent 45 SECONDS in `ArrayKeyScanner` alone,
  looking for arrays it never found. `JsonAstObject.Find` now indexes itself above
  `JsonAstObject.IndexFrom` properties (lazily, first-wins so duplicate names keep their documented
  meaning). `JsonSemanticDiffer.CompareObjects` builds its own dictionary and is fine.
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
**"Settings never throw" needs somewhere for a settings failure to GO** (Diff). `ISettingsStore.Load`
returns defaults and `SaveAsync` returns false rather than throwing, which is right - losing a preference
must never stop the app. The cost is that a settings problem has no natural way to surface, and that is
not theoretical: `CaptureOptions` built its dictionary with `ToDictionary`, which throws on a duplicate
key, and it runs inside the `OptionsChanged` handler that saves. One duplicated array-key override
therefore threw out through whatever toggle raised the event and NOTHING was saved for the rest of the
session - every option still worked, and every one was gone at the next start. Reported as "my settings
do not stick"; the settings file was a day old while the toolbar showed things switched on.

Three rules now, and all three are needed. Building the settings must not throw: duplicates collapse
last-wins, matching `ApplyArrayKeyAsync`, which replaces rather than appends. Duplicates are also
stopped at the source - the Settings window's Add replaces an existing entry for a path, the same way
the change tree's menu does. And `ShellViewModel.Persist` wraps the capture, because it runs from an
event handler where an escaping exception silently kills every later save; the failure sets
`SettingsError`, which `MainWindow` shows as a banner. Do not remove that banner to "keep the window
clean" - it is the only thing standing between a settings bug and a user discovering it a day later.

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
  neither occupies the toolbar during the many sessions that are only ever a read. `MergeWindow`'s
  three file pickers collapse to a one-line summary after a successful merge
  (`MergeViewModel.IsFileRowExpanded`) for the same reason - the comparison window went further and
  removed its picker row outright (see below). Do not "restore" these to
  always-visible-but-disabled - the row they cost is a row of diff, which is the thing the app exists
  to show.
- **The "Reformat" checkbox (`NormalizeStructure`) used to be labeled "Normalize XML" and hidden
  whenever `Pane.IsSemantic` was true** - i.e. hidden exactly for JSON, the one format users most want
  to reformat. It backs both XML and JSON already (`TextLineNormalizer.Canonicalize`); the bug was
  purely the toolbar's `IsVisible` binding. It is now unconditionally visible, like "Ignore whitespace"
  and "Ignore case" - it is a no-op on content that is neither, which is fine. It lives in
  `SettingsWindow` rather than the toolbar now; the toolbar keeps only the options reached for
  mid-comparison.
- **An Avalonia type selector matches the EXACT type, so `Button.foo` does not style a `ToggleButton`**
  (Controls). `ToggleButton` derives from `Button`, and `Classes="toolbar-btn"` on one looked right in
  the XAML and rendered as a stock Fluent button on screen - which is how the Json view's Pretty toggle
  came to look unlike everything around it. `ButtonStyles.axaml` therefore spells out
  `ToggleButton.toolbar-btn` separately rather than reaching for `:is(Button)`, because the checked
  state needs somewhere to live anyway. Same trap for any future `RadioButton`/`SplitButton` class.
- **One `ControlHeight` for every button class** (Controls). `.toolbar-btn` / `.primary-btn` /
  `.secondary-btn` / `ToggleButton.toolbar-btn` all set `MinHeight` from it, with vertical padding
  deliberately smaller so the height decides the box. If a button in a row looks wrong, fix
  `ButtonStyles.axaml` - do NOT put `Height` on the instance, which is what the Gallery's blue button
  used to carry and what made the mismatch invisible in every diff.
- **Do not set `VerticalAlignment` in the shared button styles** (Controls). It was tried and reverted:
  API Studio's Send button is a `Panel` child that stretches to match the URL bar beside it, and
  `Center` shrank it to `MinHeight`. `MinHeight` alone gives an even toolbar row without taking that
  away.
- **F5 means two different things, and picking wrong loses work** (Diff).
  `ComparisonViewModel.RefreshDiffAsync` re-diffs what the PANES hold when there are unsaved edits, and
  re-reads both files from disk only when there are none. Reloading over typed text discards the only
  copy of it. `IsDiffStale` is the other half: set the moment a pane is edited, cleared when the
  re-diff lands, and shown in the status bar - do not "tidy it away" because it usually clears itself
  within a few hundred ms, since `LiveDiff` off is a supported mode where it stays up until F5.
- **`JsonView` brings its own Prev/Next strip, and Fubar Diff turns it off** (Diff).
  `JsonView.ShowToolbar` defaults to TRUE for API Studio, which embeds the view where there is no
  toolbar to put buttons in; the diff window sets it False and drives navigation from its own toolbar
  through `DiffPaneViewModel.NextDifferenceCommand`, which walks semantic changes in the Json view and
  hunks everywhere else. Do not "simplify" by deleting the strip - one host still needs it - and do
  not point a toolbar's Prev/Next at `NextChangeCommand` again: in the Json view that walks hunks
  nobody is looking at. The caption the strip carried lives in the status bar now, fed by
  `ComparisonViewModel` watching `JsonCaption` (guarded on `CurrentSemanticChange is not null`, or the
  "none selected" form raised by every load would overwrite the summary just written there).
- **Radio/check `MenuItem`s need their binding mode spelled out** (Diff). The View menu's
  `IsChecked` bindings read computed properties (`IsModeAuto`, `Pane.IsSideBySideViewVisible`), and a
  toggled MenuItem writes back to whatever it is bound to - so those are `Mode=OneWay` and the state
  is changed by the item's `Command` instead. The two genuine two-way ones (Diff pane, Wrap) say
  `Mode=TwoWay` for the opposite reason: do not rely on the default either way.
- **A settings row's explanation is a `Description`, not a tooltip** (both apps). `fc:SettingRow`
  exists for this: a header, a plain sentence under it, the control on the right. The Diff settings
  window was a column of terse labels whose meaning lived entirely in `ToolTip.Tip`, which is where an
  explanation goes to be missed. Tooltips are for the second-order detail, not for what the option
  does. Keep new rows in that shape, and keep the sentence short and in the user's words.
- **`ExtendClientAreaToDecorationsHint` means `Window.Title` must be empty** (both apps). Both main
  windows draw their own tab strip into the native title-bar row; a non-empty Title has the OS paint
  its own text over the first tab. Both also snap `WindowState.FullScreen` back to `Maximized` in
  `OnPropertyChanged`, because this Avalonia version draws a full-screen caption button that cannot be
  removed or hidden (it lives outside the window's visual tree).

## Workflow notes

- Commit/push only when asked; branch off `main` first if needed.
- The design docs in `docs/` (LeftPane / RequestEditorPane / ResponsePane) are the canonical behaviour
  spec for API Studio's panes.

**A hand-written `InitializeComponent` silently breaks every `x:Name` in the file** (both apps). The
XAML compiler generates one that assigns the named fields; writing
`private void InitializeComponent() => AvaloniaXamlLoader.Load(this);` overrides it, the fields stay
null, and the failure is a NullReferenceException in the constructor. `OpenComparisonWindow` did this
and its drop targets were null - and because the caller was an `async void` click handler, the
exception took the whole PROCESS down rather than showing an error. Every other window in this
codebase relies on the generated one; new ones must too. A headless test that merely CONSTRUCTS a
window catches it, which is why `OpenComparisonTests` has one that does nothing else.

**"Built but never wired" is a recurring failure here, and it has now happened twice** (both apps).
`WorkspaceExplorerViewModel.NewWorkspaceAsync` existed, worked, and was bound to NOTHING - so API
Studio could be installed and then not started, because the only route in demanded an existing
`fubar.json`. That is the same shape as the Diff options that were fully built in Core with
persistence fields waiting while `ComparisonViewModel` never read them. Before concluding a feature is
missing, grep for it; before concluding one is DONE, grep for a binding to it. A command with no
`Command="{Binding …}"` anywhere in a `.axaml` is dead code that looks alive.
