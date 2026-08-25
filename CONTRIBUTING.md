# Contributing to Fubar

Thanks for your interest in improving Fubar! This guide covers how to get set up, the
conventions the codebase follows, and how to get a change merged.

By participating in this project you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Any editor works; Visual Studio,
JetBrains Rider, and VS Code (with the C# Dev Kit) all understand the `.slnx` solution.

```bash
git clone https://github.com/Fubar83/fubar.git
cd fubar
dotnet build Fubar.slnx
dotnet test  Fubar.slnx

dotnet run --project src/Fubar.Studio.UI          # API Studio
dotnet run --project src/Fubar.Diff.UI            # Diff
dotnet run --project src/Fubar.Controls.Gallery   # the component sandbox
```

If your change is to a generic control or the design system, make it in `src/Fubar.Controls` and
iterate in the **Gallery** rather than inside an app - the Gallery references only the library, which
is what keeps the design system from quietly growing a dependency on one of the apps.

## How to contribute

- **Bugs & features:** please [open an issue](https://github.com/Fubar83/fubar/issues)
  first (use the templates). For bugs, include your OS, the version/commit, and repro steps.
- **Small fixes** (typos, obvious bugs): a direct PR is fine.
- **Larger changes:** open an issue to discuss the approach before investing a lot of time — it saves
  everyone rework.
- **Security issues:** do **not** open a public issue. Follow [SECURITY.md](SECURITY.md).

### Pull request workflow

1. Fork and create a branch off `main` (e.g. `feat/oauth-pkce`, `fix/tab-drag-cursor`).
2. Make your change with tests where it makes sense.
3. Ensure `dotnet build Fubar.slnx` is warning-clean and `dotnet test Fubar.slnx` is green.
4. Update `CHANGELOG.md` under **Unreleased** and any affected docs.
5. Open the PR against `main`, fill in the template, and link the issue it closes.

CI (build + test) must pass before a PR can be merged.

## Architecture & conventions

The single most important rule in this codebase:

> **Keep the layers pointing inward, and keep generic UI out of the app.** `Core` knows nothing but
> the BCL; `Application` and `Infrastructure` know only `Core`; UI ViewModels never touch
> `Fubar.Studio.Infrastructure` (`Composition.cs` is the single allowed edge). And anything generic
> enough to be reusable belongs in the `Fubar.Controls` package, not in a view here — app-specific
> panes (Request/Response/Left pane) live in `Fubar.Studio.UI`, their generic building blocks do not.
> `tests/Fubar.Studio.Architecture.Tests` fails the build if any of this is violated.

Other conventions:

- **MVVM** via CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`).
  View models hold logic; views stay thin.
- **Variables** resolve from the **active environment** (plus the in-memory session store for things
  like OAuth tokens), never directly from files.
- **Match the surrounding code** — naming, comment density, and idioms. Comments explain *why*, not
  *what*.
- Keep the build **warning-clean**; analyzers are enabled repo-wide (`Directory.Build.props`).
- Package versions are centrally managed in `Directory.Packages.props` (Central Package Management) —
  add versions there, not in individual `.csproj` files.
- `.editorconfig` defines formatting; run `dotnet format` if in doubt.

## Tests

- `tests/Fubar.Studio.Core.Tests` and `tests/Fubar.Studio.Infrastructure.Tests` — xUnit unit tests.
- `tests/Fubar.Studio.Application.Tests` — use-case service tests.
- `tests/Fubar.Studio.Architecture.Tests` — NetArchTest layering guard.
- `tests/Fubar.Studio.EndToEnd.Tests` — live HTTP auth tests; skipped unless `FUBAR_E2E=1`.

Run everything with `dotnet test Fubar.slnx`.

## License

By contributing, you agree that your contributions are licensed under the project's
[MIT License](LICENSE).
