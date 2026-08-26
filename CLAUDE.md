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
offset copy rather than a line-mapping scheme.

**Semantic JSON is a refinement, not a second pipeline** (Diff). The text differ decides how lines
line up; `JsonSemanticPass` decides which of them matter. One `DiffResult` shape means every renderer,
the diff map, navigation and merge work in both modes. This makes ALIGNMENT the load-bearing step: if
the two sides are formatted so differently that raw-line alignment has nothing sane to match (a
minified file against a pretty one), "which lines matter" cannot fix a starting alignment that never
made sense. `FileComparisonService.Compare` pretty-prints both sides via
`ILineNormalizer.CanonicalizeJson` before alignment whenever semantic comparison is possible, precisely
so this refinement has something coherent to refine. That printer keeps all-scalar containers on one
line on purpose - `System.Text.Json`'s generic indented writer expands even `{"id": 1}` across three
lines, and an array of small objects then hands the line-based differ a wall of identical boilerplate
braces it will match to each other across unrelated elements.

**Ignore rules are applied where differences are DECIDED, not where they are drawn** (Diff).
`JsonSemanticDiffer.Compare` marks changes through `JsonIgnoreRules` before returning, so the tree, the
text view's line filter, the diff map and navigation all agree. Filtering in a view instead would make
that view disagree with the others about what changed.

**Hybrid mode has no alignment at all, on purpose** (Diff). `RawJsonPane` shows each side's raw,
unaligned text and highlights the current change's own `JsonAstNode.Span` directly - no fillers, no
line-for-line correspondence between the two sides. This is what makes it immune to the class of
problem the alignment fix above patches around: there is no shared line numbering for a formatting or
property-order difference to break. Do not "simplify" it by routing Hybrid through `AlignedText` -
that would reintroduce exactly the dependency it exists to avoid.

**An ignored row is `Unchanged` + `IsIgnored`, never its own `ChangeKind`** (Diff). That is what keeps
it out of `IsChange`, and therefore out of hunks, counts, the diff map and F7/F8 — while still letting
a renderer draw a faint band. Promoting it to a `ChangeKind` would silently put every ignored row back
into the hunk list and make navigation stop on the fields the user asked not to see.
`IgnoredRowNavigationTests` pins this.

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

## Workflow notes

- Commit/push only when asked; branch off `main` first if needed.
- The design docs in `docs/` (LeftPane / RequestEditorPane / ResponsePane) are the canonical behaviour
  spec for API Studio's panes.
