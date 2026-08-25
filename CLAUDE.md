# CLAUDE.md

Guidance for Claude Code (and contributors) working in this repository.

## What this is

**Fubar Diff** — a cross-platform desktop **diff tool** built on **Avalonia 12 + .NET 10**, C#, MVVM.
The shipped binary is `FubarDiff`; the on-screen title is "Fubar Diff". Sibling project to
[Fubar API Studio](https://github.com/Fubar83/Fubar-API-Studio), sharing its design system via the
[`Fubar.Controls`](https://github.com/Fubar83/fubar-components) package.

**Early-stage.** Two-editor side-by-side comparison, character-level diff, change navigation, a diff
map, hunk-level merge with save, and semantic JSON comparison all work end to end. Folder comparison,
free-form editing and the other formats are not built yet. The layering below is already in place and
should be followed for new work.

## Architecture (read before changing structure)

Clean, layered, and **enforced by tests** (`tests/Fubar.Diff.Architecture.Tests`). Dependencies point
inward only:

```
Fubar.Controls  (shared design system + component library — a NuGet package from its own repo:
                 https://github.com/Fubar83/fubar-components)
      ▲ consumed by
Presentation ── Fubar.Diff.UI            Views + thin ViewModels + Composition root (DI)
      │  → depends on
Application ── Fubar.Diff.Application    Use-case services (FileComparisonService)
      │  → depends on
Core / Domain ── Fubar.Diff.Core         Entities + domain policy + PORTS (interfaces)
      ▲ implements ports
Infrastructure ── Fubar.Diff.Infrastructure   Adapters: DiffPlex engine, normalizer, file reader
```

**Dependency rules (the arch tests will fail the build if violated):**
- `Core` depends on nothing but the BCL — no Avalonia, no DiffPlex, no `Microsoft.Extensions`.
- `Application` depends only on `Core`.
- `Infrastructure` depends only on `Core`.
- `Fubar.Diff.UI` depends on `Application` + `Core`; **UI ViewModels must NOT reference
  `Fubar.Diff.Infrastructure`** — `Composition.cs` is the one allowed UI→Infrastructure edge.
- **DiffPlex is confined to `Infrastructure`.** It is an implementation detail of one adapter; if it
  leaks, swapping the algorithm stops being a one-file change, which is the whole point of `IDiffEngine`.

## How semantic JSON fits in

Semantic comparison is a **refinement of the text pass, not a second pipeline**. The text differ
decides how the two documents line up; `JsonSemanticPass` then decides which of those rows actually
matter, and `SemanticLineFilter` downgrades the rest to context. That is why every renderer, the diff
map, navigation and merge work identically in both modes — there is only ever one `DiffResult` shape.

Building an alignment from the AST instead would mean reimplementing filler rows, hunk grouping and
ordering a second time, and giving the two modes subtly different behaviour. Do not "improve" it that
way without a concrete reason the filter cannot cover.

The parser is hand-written (`Infrastructure/Json/JsonAstParser.cs`) because `System.Text.Json` gives no
per-node line and column, which is exactly what is needed to show a tree-based difference in a text
editor. It is **iterative with an explicit stack**: nesting depth is attacker-controlled, and a
recursive parser would die with an uncatchable `StackOverflowException`.

## The invariant that is easiest to break

**Comparison keys are not display text.** The normalizer produces a key per line (trimmed, case-folded)
that the engine matches on; `FileComparisonService` then projects every row back onto the real document
lines before anyone sees them. Skip that projection and turning on "ignore case" shows the user a
lower-cased copy of their own file. `FileComparisonServiceTests` pins this down — do not delete it.

The exception is `ILineNormalizer.Canonicalize` (structure normalization): its output **is** displayed,
because comparing canonical JSON only makes sense if you can see the canonical form.

## Where things live

| Area | Location |
| --- | --- |
| Domain models, policy, ports | `src/Fubar.Diff.Core` (`Models/`, `Comparison/`, `Files/`, `Merge/`, `Rendering/`, `Json/`) |
| Use-case services | `src/Fubar.Diff.Application` (`Comparison/`, `Merge/`) |
| Diff engine, inline (character) diff, JSON parser, normalizer, file reader/writer, DI wiring | `src/Fubar.Diff.Infrastructure` |
| Views + ViewModels + DI (`Composition.cs`) | `src/Fubar.Diff.UI` (`Rendering/` = AvaloniaEdit hooks, `Controls/` = diff map) |
| Settings, recent files | `src/Fubar.Diff.Core/Settings` + `src/Fubar.Diff.Infrastructure/Settings` (`%APPDATA%/fubar-diff/settings.json`) |
| Reusable controls + theme/design system | External: the `Fubar.Controls` package |
| Packaging | `build/publish.ps1` |

## Build / run / test

```bash
dotnet build FubarDiff.slnx                # whole solution (must be warning-clean)
dotnet test  FubarDiff.slnx                # all tests
dotnet run   --project src/Fubar.Diff.UI   # run the app
dotnet run   --project src/Fubar.Diff.UI -- left.json right.json   # compare on startup
./build/publish.ps1                        # self-contained per-RID binaries (pwsh 7+)

# Changing a shared control and this app together (step-into debugging, no pack/restore):
dotnet build FubarDiff.slnx -p:UseLocalComponents=true
```

Tests (xUnit v3): `Fubar.Diff.Core.Tests`, `Fubar.Diff.Application.Tests`,
`Fubar.Diff.Infrastructure.Tests`, and `Fubar.Diff.Architecture.Tests` (NetArchTest — the boundary
guard). Keep the suite green; a refactor must not change behavior.

## Conventions

- **Ports live in Core**, implementations in Infrastructure, wired in
  `Infrastructure/ServiceCollectionExtensions.cs` (`AddFubarDiffInfrastructure`). Application services
  are registered in `UI/Composition.cs`.
- **Domain policy lives in Core, not ViewModels** — e.g. `HunkNavigator` owns the next/previous
  wrap-around rules precisely so they can be tested without a UI. Put new rules there.
- **MVVM** via CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`);
  `ViewModelBase : ObservableObject`.
- **Editors are read-only.** The aligned documents contain filler lines, so editor text is NOT file
  text. Typing would desynchronise the panes and there is no offset map yet to put it back. Merge
  goes through hunk commands on the domain model instead.
- **Generic UI belongs in `Fubar.Controls`**, not here. Anything that knows what a "hunk" is stays
  app-side; a reusable primitive goes to the package (with a Gallery page).
- **Central Package Management**: versions live in `Directory.Packages.props`; reference packages
  without a `Version` in the `.csproj`.
- **Keep it warning-clean**; analyzers are on repo-wide and CI builds + tests every push/PR.

## Gotchas

- **`Application` name collision**: the `Fubar.Diff.Application` namespace shadows Avalonia's
  `Application` type inside `Fubar.Diff.*` code (a namespace member outranks a using-alias, so an
  alias cannot fix it). Qualify Avalonia's type as **`Avalonia.Application`**.
- **Build fails with locked DLLs while the app is running** → `taskkill //F //IM FubarDiff.exe` first.
- **Filler-line discipline is the central invariant**: editor line `i` is always `DiffResult.Lines[i]`,
  on BOTH sides. Never read the editors back to save - go through `MergedDocument`, or you will write
  the filler blanks into the user's file. Because both sides have the same line count, scroll sync is
  a plain vertical-offset copy; do not "improve" it into a line-mapping scheme.
- **Viewport size must come from `TextView.DefaultLineHeight`, not `VisualLines.Count`** - a document
  shorter than the pane reports only the lines it drew, which collapses the diff map's scale.
- **Test both theme variants.** A token used only in Dark throws at runtime in Light.
- **Settings never throw.** `Load` returns defaults and `SaveAsync` returns false on failure - losing a
  preference is a nuisance, refusing to start over a corrupt settings file is not acceptable.
- **Avalonia 12 renamed drag-drop types**: `DragEventArgs.Data` is now `DataTransfer`, typed
  `IDataTransfer` (not `IDataObject`), and files come from `TryGetFiles()`.

## Workflow notes

- Commit/push only when asked; branch off `main` first if needed.
