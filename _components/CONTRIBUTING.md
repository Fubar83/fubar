# Contributing to Fubar.Controls

Thanks for your interest in improving Fubar.Controls! This guide covers how to get set up, the
conventions the library follows, and how to get a change merged.

By participating in this project you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Any editor works; Visual Studio,
JetBrains Rider, and VS Code (with the C# Dev Kit) all understand the `.slnx` solution.

```bash
git clone https://github.com/Fubar83/fubar-components.git
cd fubar-components
dotnet build Fubar.Controls.slnx
dotnet test  Fubar.Controls.slnx
dotnet run   --project src/Fubar.Controls.Gallery
```

The **Gallery** is the primary development harness — it is a living style guide that references only
`Fubar.Controls`, so it both demonstrates the library and proves the library stands on its own.
Develop against the Gallery, not against a host app.

## The one rule

> **`Fubar.Controls` is app-agnostic.** It depends on Avalonia (plus AvaloniaEdit for the JSON
> editor) and nothing else. It must never reference a host application, a view model, or a domain
> concept belonging to one.

If a control needs to know what a "request", "environment", or "workspace" is, it does not belong
here — it belongs in the app. Generic building blocks belong here; anything bound to a domain
concept stays app-side. `tests/Fubar.Controls.Tests/ArchitectureTests.cs` enforces this.

## How to contribute

- **Bugs & features:** please [open an issue](https://github.com/Fubar83/fubar-components/issues)
  first (use the templates). For bugs, include a minimal XAML repro.
- **Small fixes** (typos, obvious bugs): a direct PR is fine.
- **Larger changes:** open an issue to discuss the approach before investing a lot of time.
- **Security issues:** do **not** open a public issue. Follow [SECURITY.md](SECURITY.md).

### Pull request workflow

1. Fork and create a branch off `main` (e.g. `feat/split-button`, `fix/tab-drag-cursor`).
2. Make your change, with tests where it makes sense.
3. Add or update a **Gallery page** for any new or changed control — that is how reviewers see it.
4. Ensure `dotnet build Fubar.Controls.slnx` is warning-clean and `dotnet test` is green.
5. Update `CHANGELOG.md` under **Unreleased**.
6. Open the PR against `main`, fill in the template, and link the issue it closes.

CI (build + test) must pass before a PR can be merged.

## Conventions

- **Templated controls** go in `Controls/` as C# classes; their default styles/themes go in
  `Themes/` and are merged into `Themes/Fubar.Controls.axaml`. Nothing renders unless it is
  included there.
- **Colours come from tokens** in `Themes/Palette.axaml`, never hard-coded. Every token must be
  defined for both the Dark and Light `ThemeDictionaries` — test both variants before opening a PR.
- **Small and composable**: prefer several single-purpose controls over one control with a mode flag.
- **Match the surrounding code** — naming, comment density, and idioms. Comments explain *why*, not
  *what*.
- Keep the build **warning-clean**; analyzers are enabled repo-wide (`Directory.Build.props`).
- Package versions are centrally managed in `Directory.Packages.props` (Central Package Management) —
  add versions there, not in individual `.csproj` files.
- `.editorconfig` defines formatting; run `dotnet format` if in doubt.

## Public API & versioning

This library ships as a **NuGet package** consumed by other repositories, so its public surface is a
contract. Version numbers are derived from git tags by [MinVer](https://github.com/adamralph/minver) —
never hand-edit a version. Call out any breaking change explicitly in the PR and in `CHANGELOG.md`.

## Tests

`tests/Fubar.Controls.Tests` runs headless Avalonia (`Avalonia.Headless.XUnit`), so control
behaviour is testable without a display. Add tests next to the behaviour you change.

```bash
dotnet test Fubar.Controls.slnx
```

## License

By contributing, you agree that your contributions are licensed under the project's
[MIT License](LICENSE).
