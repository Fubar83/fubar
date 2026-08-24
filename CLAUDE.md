# CLAUDE.md

Guidance for Claude Code (and contributors) working in this repository.

## What this is

**Fubar API Studio** — a cross-platform desktop **API client** (a native, open-source
Postman/Insomnia alternative) built on **Avalonia 12 + .NET 10**, C#, MVVM. The shipped binary is
`FubarAPIStudio`; the on-screen title is "Fubar API Studio".

Core capabilities: a request builder (method/URL bar with live `{{variable}}` highlighting; Params /
Headers / Body / Auth / Tests / History tabs); environments + secret & session-only variables; OAuth 2.0
(client-credentials + refresh) with test/verify; reusable, inheritable auth profiles; OpenAPI/Swagger
import with a reconciliation **diff** (and auto status assertion + response-schema validation); Postman
v2.1 import; curl import **and** "Copy as cURL" export; response viewer (Pretty/Tree/Raw/Headers/Tests/
Preview, JSONPath filter); declarative **assertions** + response **captures** into variables; per-request
timeout, cancel, and a session cookie jar; Chrome-style workspace tabs with drag/tear-off.

## Architecture (read before changing structure)

Clean, layered, and **enforced by tests** (`tests/Fubar.Studio.Architecture.Tests`). Dependencies point
inward only:

```
Fubar.Controls  (shared design system + component library — a NuGet package from its own repo:
                 https://github.com/Fubar83/fubar-components)
      ▲ consumed by
Presentation ── Fubar.Studio.UI          Views + thin ViewModels + Composition root (DI)
      │  → depends on
Application ── Fubar.Studio.Application   Cohesive use-case/orchestration services (e.g. RequestExecutionService)
      │  → depends on
Core / Domain ── Fubar.Studio.Core        Entities + domain services/policy + PORTS (interfaces)
      ▲ implements ports
Infrastructure ── Fubar.Studio.Infrastructure   Adapters: HTTP exec, storage, importers, OAuth, JSON
```

**Dependency rules (the arch tests will fail the build if violated):**
- `Core` depends on nothing but the BCL (+ CommunityToolkit.Mvvm).
- `Application` depends only on `Core`.
- `Infrastructure` depends only on `Core`.
- `Fubar.Studio.UI` depends on `Application` + `Core`; **UI ViewModels must NOT reference
  `Fubar.Studio.Infrastructure`** — `Composition.cs` is the one allowed UI→Infrastructure edge.
- `Fubar.Controls` is an external package and cannot see any app layer — the boundary that used to
  need enforcing here is now structural.

**Conventions that keep this honest:**
- **Ports live in Core**, implementations in Infrastructure, wired in
  `Infrastructure/ServiceCollectionExtensions.cs` (`AddFubarInfrastructure`). Application services are
  registered in `UI/Composition.cs`.
- **Domain policy lives in Core, not ViewModels**: e.g. `AuthApplier`, `AuthRequestMerge`, `EffectiveAuthResolver`,
  `QueryStringSync`, `HttpHeaderNames`, `AuthDefaults`. Put new business rules there, not in a VM.
- **Orchestration lives in Application services** (cohesive, feature-grouped), not inline in VMs. The
  send pipeline (auth→execute→captures/assertions→history) is `RequestExecutionService`.
- **Editor VMs are built via `IEditorViewModelFactory`** (ActivatorUtilities) — do NOT hand-thread
  services through `MainViewModel` to `new` up an editor. Adding a dependency to an editor VM is free:
  add the ctor param, ensure it's registered.
- **ISP**: workspace storage is split into role interfaces (`IWorkspaceStore`, `IRequestStore`,
  `IEnvironmentStore`, `IAuthProfileStore`, `IFolderConfigStore`, `IInheritanceResolver`). Depend on the
  narrowest role you need; `IWorkspaceService` is a thin aggregate for the broad importers only.
- **Small, composable components**: build larger views/controls from small single-purpose ones. Generic,
  reusable UI pieces belong in the shared `Fubar.Controls` package (https://github.com/Fubar83/fubar-components,
  with a Gallery page); anything bound to a domain concept (e.g. the `VariableTooltip`/`VariableIntellisense`
  behaviors) stays app-side.
  behaviors) stays app-side.
- **Variables** resolve from the **active `WorkspaceEnvironment`** via `IVariableResolver.Substitute`,
  with an in-memory `ISessionVariableStore` fallback (OAuth tokens/expiry — never persisted to disk).
- **MVVM** via CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`);
  `ViewModelBase : ObservableObject`.

## Where things live

| Area | Location |
| --- | --- |
| Domain models / ports / policy | `src/Fubar.Studio.Core` |
| Use-case services | `src/Fubar.Studio.Application` |
| HTTP execution, OAuth, variable resolver, secrets, storage, JSON adapters, importers | `src/Fubar.Studio.Infrastructure` |
| Views + ViewModels + DI (`Composition.cs`) | `src/Fubar.Studio.UI` |
| Reusable controls + theme/design system | External: the `Fubar.Controls` package ([fubar-components](https://github.com/Fubar83/fubar-components)) |
| Design notes | `docs/` (LeftPane / RequestEditorPane / ResponsePane) |
| Packaging | `build/publish.ps1`; icon generator: `tools/IconGen` |

## Build / run / test

```bash
dotnet build FubarApiStudio.slnx               # whole solution (must be warning-clean)
dotnet test  FubarApiStudio.slnx               # all tests (see below)
dotnet run   --project src/Fubar.Studio.UI     # run the app
./build/publish.ps1                            # self-contained per-RID binaries (pwsh 7+)

# Changing a shared control and this app together (step-into debugging, no pack/restore):
dotnet build FubarApiStudio.slnx -p:UseLocalComponents=true
```

Tests (xUnit): `Fubar.Studio.Core.Tests`, `Fubar.Studio.Application.Tests`,
`Fubar.Studio.Infrastructure.Tests` (v2), and
`Fubar.Studio.Architecture.Tests` (NetArchTest — the boundary guard). Keep the suite green; a refactor
must not change behavior. Add unit tests next to the layer you change (pure domain services and
Application services are trivially testable with fakes).

## Gotchas

- **Build fails with locked DLLs while the app is running** → `taskkill //F //IM FubarAPIStudio.exe`
  first (Bash tool). Smoke-test by launching the built exe, sleeping a few seconds, then killing it.
- **`Application` name collision**: the `Fubar.Studio.Application` namespace shadows Avalonia's
  `Application` type inside `Fubar.Studio.*` code (a namespace member outranks a using-alias, so an alias
  can't fix it). Qualify Avalonia's type as **`Avalonia.Application`** (e.g. `Avalonia.Application.Current`).
- **Central Package Management**: versions live in `Directory.Packages.props`; reference packages without
  a `Version` in the `.csproj`. Common build settings are in `Directory.Build.props`.
- **Keep it warning-clean**: analyzers are on repo-wide; the CI (`.github/workflows/ci.yml`) builds +
  tests on every push/PR, and a `v*` tag triggers `build.yml` to publish cross-platform binaries.
- **The shared components live in another repo now.** `Fubar.Controls` comes from
  https://github.com/Fubar83/fubar-components as a NuGet package. A generic, reusable UI piece belongs
  there (with a Gallery page); anything that knows a domain concept belongs here. To change both at
  once, build with `-p:UseLocalComponents=true` — see `Directory.Build.props`.

## Workflow notes

- Commit/push only when asked; branch off `main` first if needed.
- The design docs in `docs/` (e.g. RequestEditorPane.md §-references sprinkled through the code comments)
  are the canonical behavior spec for the panes.
