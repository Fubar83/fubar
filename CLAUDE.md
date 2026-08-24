# CLAUDE.md

Guidance for Claude Code (and contributors) working in this repository.

## What this is

**Fubar.Controls** — a reusable, **app-agnostic** Avalonia 12 / .NET 10 **design system + component
library**, published to nuget.org as the `Fubar.Controls` package. It ships the shared look and feel
(colour tokens, shared styles) plus a catalog of composable controls, and is consumed by
[Fubar API Studio](https://github.com/Fubar83/Fubar-API-Studio) and
[Fubar Diff](https://github.com/Fubar83/fubar-diff).

The library was extracted (with history) from the API Studio repository; that is why early commits
mention it.

## The one rule

> `Fubar.Controls` depends on **Avalonia + AvaloniaEdit + the BCL, and nothing else**. It must never
> reference a host application, a view model, or a domain concept belonging to one.

If a control needs to know what a "request", "workspace", or "diff hunk" is, it belongs in the app,
not here. `tests/Fubar.Controls.Tests/ArchitectureTests.cs` enforces both halves: an **allowlist** on
referenced assemblies, and a check that no public type name leaks a domain word.

This is not stylistic. Two unrelated apps consume this package; a dependency added here is a
dependency forced on both.

## Layout

| Area | Location |
| --- | --- |
| Control classes | `src/Fubar.Controls/Controls/` |
| Default styles / control themes | `src/Fubar.Controls/Themes/` |
| Colour tokens (Dark + Light) | `src/Fubar.Controls/Themes/Palette.axaml` |
| Theme aggregate (the consumer's single `StyleInclude`) | `src/Fubar.Controls/Themes/Fubar.Controls.axaml` |
| Value converters | `src/Fubar.Controls/Converters/` |
| Living style guide / dev harness | `src/Fubar.Controls.Gallery/` |
| Headless Avalonia tests | `tests/Fubar.Controls.Tests/` |
| Local packing | `build/pack.ps1` |

## Build / run / test

```bash
dotnet build Fubar.Controls.slnx                   # must be warning-clean
dotnet test  Fubar.Controls.slnx                   # headless Avalonia (xunit.v3)
dotnet run   --project src/Fubar.Controls.Gallery  # the dev harness - use this
./build/pack.ps1                                   # pack locally (pwsh 7+)
```

**Develop against the Gallery**, not against a consuming app. It references only this library, so it
both demonstrates the component and proves the boundary holds. Every new or changed control needs a
Gallery page — that is how a reviewer sees it.

## Conventions

- **A new control is three things**: the class in `Controls/`, its default style in `Themes/`, and a
  merge entry in `Themes/Fubar.Controls.axaml`. Miss the third and it silently does not render.
- **Colours come from `Palette.axaml` tokens via `DynamicResource`** — never hard-coded, and always
  defined in *both* the Dark and Light `ThemeDictionaries`. Check both variants before you finish;
  the Gallery has a theme switcher for exactly this.
- **Dumb by default**: `StyledProperty` inputs, `RoutedEvent`/`ICommand` outputs, no app types, no
  business logic. A "smart" control (only `TabStrip` today) may own gestures and transient state, but
  only against a generic abstraction the host implements (`ITabDragHost`) — never an app view model.
- **Small and composable**: prefer several single-purpose controls over one with a mode flag. The
  composed controls (`Chip`, `SearchBox`, `Card`, `Banner`) are built from the primitives.
- **Central Package Management**: versions live in `Directory.Packages.props`; reference packages
  without a `Version` in the `.csproj`. Shared build settings are in `Directory.Build.props`.
- **Keep it warning-clean**; analyzers are on repo-wide and CI builds + tests every push/PR.

## Packaging & versioning

- Versions are derived from git tags by **MinVer** (`v0.1.0` → `0.1.0`). **Never hand-edit a version
  number** — tag instead. An untagged build produces a `0.0.0-alpha.N` prerelease.
- `git tag v0.1.0 && git push --tags` triggers `.github/workflows/release.yml`, which builds, tests,
  packs, attaches a Sigstore provenance attestation, pushes to nuget.org (needs the `NUGET_API_KEY`
  repository secret) and cuts a GitHub Release.
- `CS1591` is suppressed in `Fubar.Controls.csproj` — the package ships an XML doc file, but most
  `StyledProperty` fields and overrides are still undocumented. Backfilling those is open work; do
  not "fix" it by removing `GenerateDocumentationFile`.
- The **public API is a contract** — two repos consume it. Call out breaking changes in the PR and in
  `CHANGELOG.md`.

## Consuming this library

Both consumer repos default to a `PackageReference` and support an opt-in source build:

```bash
dotnet build -p:UseLocalComponents=true   # swaps to a ProjectReference into ../fubar-components
```

Use that when changing the library and an app together. `./build/pack.ps1 -PushTo ../fubar-diff`
covers the rarer case of validating the actual packaged artifact.

## Gotchas

- **A style that isn't merged into `Fubar.Controls.axaml` does nothing** — the most common "my
  control renders unstyled" cause.
- **`avares://Fubar.Controls/...` URIs work identically from a package** — the XAML is compiled into
  the assembly. If a resource 404s, the assembly name changed, not the packaging.
- **Test both theme variants.** A token defined only in the Dark dictionary throws at runtime in
  Light, and no compile-time check catches it.

## Workflow notes

- Commit/push only when asked; branch off `main` first if needed.
