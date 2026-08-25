# Contributing to Fubar Diff

Thanks for your interest in improving Fubar Diff! This guide covers how to get set up, the
conventions the codebase follows, and how to get a change merged.

By participating in this project you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Any editor works; Visual Studio,
JetBrains Rider, and VS Code (with the C# Dev Kit) all understand the `.slnx` solution.

```bash
git clone https://github.com/Fubar83/fubar-diff.git
cd fubar-diff
dotnet build FubarDiff.slnx
dotnet test  FubarDiff.slnx
dotnet run   --project src/Fubar.Diff.UI -- old.json new.json
```

The shared UI components are **not** in this repository — they live in
[fubar-components](https://github.com/Fubar83/fubar-components) and arrive as the `Fubar.Controls`
NuGet package. If your change is to a generic control or the design system, open a PR there instead,
and iterate in its Gallery. To build this app against a local checkout of that library:

```bash
dotnet build FubarDiff.slnx -p:UseLocalComponents=true
```

## How to contribute

- **Bugs & features:** please [open an issue](https://github.com/Fubar83/fubar-diff/issues) first
  (use the templates). For bugs, include your OS, the version/commit, and — most usefully — the two
  inputs that diff wrongly.
- **Small fixes** (typos, obvious bugs): a direct PR is fine.
- **Larger changes:** open an issue to discuss the approach before investing a lot of time.
- **Security issues:** do **not** open a public issue. Follow [SECURITY.md](SECURITY.md).

Several sizeable pieces are unbuilt and are good places to start — see the Roadmap in the
[README](README.md#roadmap).

### Pull request workflow

1. Fork and create a branch off `main` (e.g. `feat/folder-compare`, `fix/crlf-detection`).
2. Make your change with tests where it makes sense.
3. Ensure `dotnet build FubarDiff.slnx` is warning-clean and `dotnet test FubarDiff.slnx` is green.
4. Update `CHANGELOG.md` under **Unreleased** and any affected docs.
5. Open the PR against `main`, fill in the template, and link the issue it closes.

CI (build + test) must pass before a PR can be merged.

## Architecture & conventions

The single most important rule in this codebase:

> **Dependencies point inward, and the diff algorithm stays behind its port.** `Core` knows nothing
> but the BCL; `Application` and `Infrastructure` know only `Core`; UI ViewModels never touch
> `Fubar.Diff.Infrastructure` (`Composition.cs` is the single allowed edge); and **DiffPlex is
> confined to `Infrastructure`**, so swapping the algorithm stays a one-file change.
> `tests/Fubar.Diff.Architecture.Tests` fails the build if any of this is violated.

The subtlest rule, worth knowing before you touch the comparison path:

> **Comparison keys are not display text.** The normalizer produces a key per line for matching; the
> service projects every row back onto the real document lines before rendering. Without that,
> "ignore case" would show the user a lower-cased copy of their file. The one exception is structural
> canonicalization, whose output *is* shown — comparing canonical JSON only makes sense if you see it.

Other conventions:

- **Domain policy lives in Core, not view models** — `HunkNavigator` owns the next/previous
  wrap-around rules exactly so they can be tested without a UI.
- **MVVM** via CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`).
  View models hold logic; views stay thin. The only code-behind in the app is the scroll-into-view
  bridge, which exists because scrolling has no data representation.
- **Generic UI belongs in `Fubar.Controls`**, not here. Anything that knows what a hunk is stays
  app-side.
- **Match the surrounding code** — naming, comment density, and idioms. Comments explain *why*, not
  *what*.
- Keep the build **warning-clean**; analyzers are enabled repo-wide (`Directory.Build.props`).
- Package versions are centrally managed in `Directory.Packages.props` (Central Package Management) —
  add versions there, not in individual `.csproj` files.
- `.editorconfig` defines formatting; run `dotnet format` if in doubt.

## Tests

- `tests/Fubar.Diff.Core.Tests` — pure domain: hunk grouping, navigation wrap-around.
- `tests/Fubar.Diff.Application.Tests` — orchestration, with a faked engine and reader.
- `tests/Fubar.Diff.Infrastructure.Tests` — the DiffPlex translation, normalization, and real files.
- `tests/Fubar.Diff.Architecture.Tests` — the layering guard.

Run everything with `dotnet test FubarDiff.slnx`.

## License

By contributing, you agree that your contributions are licensed under the project's
[MIT License](LICENSE).
