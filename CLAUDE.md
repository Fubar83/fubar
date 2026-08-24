# CLAUDE.md

Guidance for Claude Code (and contributors) working in this repository.

## What this is

**Fubar Diff** — a cross-platform desktop **diff tool** built on **Avalonia 12 + .NET 10**, C#, MVVM.
The shipped binary is `FubarDiff`; the on-screen title is "Fubar Diff". Sibling project to
[Fubar API Studio](https://github.com/Fubar83/Fubar-API-Studio), sharing its design system via the
[`Fubar.Controls`](https://github.com/Fubar83/fubar-components) package.

**Early-stage.** Side-by-side file comparison works end to end; folder comparison and merge editing
are not built yet. The layering below is already in place and should be followed for new work.

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
| Domain models, policy, ports | `src/Fubar.Diff.Core` (`Models/`, `Comparison/`, `Files/`) |
| Use-case services | `src/Fubar.Diff.Application/Comparison` |
| Diff engine, normalizer, file reader, DI wiring | `src/Fubar.Diff.Infrastructure` |
| Views + ViewModels + DI (`Composition.cs`) | `src/Fubar.Diff.UI` |
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
- **Style classes bind as `Classes.name="{Binding Flag}"`** — Avalonia's `Classes` property is not
  itself bindable, which is why `DiffRowViewModel` exposes a bool per class rather than a string.
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
- **Both panes share one scroller on purpose.** Two independently scrolling `ScrollViewer`s is the
  classic side-by-side diff bug; do not "improve" `DiffView.axaml` by splitting them.
- **Test both theme variants.** A token used only in Dark throws at runtime in Light.

## Workflow notes

- Commit/push only when asked; branch off `main` first if needed.
