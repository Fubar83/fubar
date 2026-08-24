# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **`Fubar.Controls` moved to its own repository** ([fubar-components](https://github.com/Fubar83/fubar-components))
  and is now consumed as a NuGet package rather than a project reference. It is shared with
  [Fubar Diff](https://github.com/Fubar83/fubar-diff); keeping it inside this repo would have forced
  that app to depend on an unrelated product. `src/Fubar.Controls`, `src/Fubar.Controls.Gallery` and
  `tests/Fubar.Controls.Tests` are gone from this solution — their history moved with them.
  Build with `-p:UseLocalComponents=true` to compile against a local checkout of the library instead
  of the package.
- The solution file is now `FubarApiStudio.slnx` (was `Fubar.slnx`), since three Fubar solutions now
  exist.

### Added

- **Variable types** (Normal / Secret / Session) on environment variables, replacing the plain
  "secret" flag. Secret values live in the OS keyring, Session values in an in-memory store, and
  **neither is ever written to disk**.
- **OAuth 2.0 as an editable request**: the auth editor now builds the token request like a normal
  request (method / URL / headers / body) seeded from a **template** (Client Credentials, Refresh
  Token, or a custom login), with **capture rules** (JSONPath → variable) that extract tokens from
  the response and **clear on failure**. Existing OAuth2 profiles upgrade on open.
- Auth now supports **HTTP Basic** and **API key in the query string** end-to-end (previously not
  applied at send), alongside Bearer, header API key, and OAuth 2.0.
- The request view shows read-only **auth placeholder rows** (in Headers, and Params for query-key
  auth) so you can see the credential that will be sent.
- Response **assertions**: declarative checks (status code, response time, JSONPath value, header
  presence) evaluated after each send and shown pass/fail in the Response pane's Tests tab.
- **Capture** response values into variables: extract a JSONPath match, header, or status into a
  session-only or environment variable (e.g. a login token) for later requests to use as `{{name}}`.
- Per-request **timeout** and an in-flight **Cancel** button; a **per-environment session cookie jar**
  so `Set-Cookie` from one request is replayed on the next (login-then-call flows) without leaking
  cookies across environments.
- **Import from curl** (paste a command) and **import a Postman Collection v2.1** export (folders,
  requests, and collection variables → an environment). The workspace Import button is now a menu
  covering OpenAPI / Swagger, Postman, and curl.
- OpenAPI / Swagger import (JSON or YAML, file or URL) into a workspace: requests,
  environments, variables, and auth profiles, with `$ref` / `allOf` resolution.
- Import reconciliation view: per-request and per-variable **add / update / unchanged /
  remove** diff so manual edits survive a re-import — you choose what to apply.
- OpenAPI import refinements: required params/headers arrive **enabled** and optional ones
  **disabled**; **deprecated** params arrive disabled and labelled; a param with an `enum` but no
  example is seeded with its first allowed value; and an **`Accept`** header is added from the
  operation's declared response media types (preferring JSON).
- Imported requests now come with a ready-made **status-code assertion** (from the spec's success
  response) and carry their success **response schema**, so the Response pane shows a ✓/⚠
  **schema-validation badge** comparing the actual body to what the spec promised.
- **Ctrl+Enter** sends the current request.
- **Copy as cURL** (request editor overflow menu): render the current request as a runnable curl
  command with `{{variables}}` resolved and all enabled headers (including auth) included — the mirror
  of curl import.
- OAuth 2.0 (Client Credentials + Refresh Token): configurable scopes, client-auth method,
  a **Test / Get token** run and a **Verify request** preview, with access token / expiry
  stored as session-only (never persisted) variables and automatic refresh on expiry.
- JSON body schema intelligence: validation, inline autocomplete, and a readable schema view
  (driven by the schema stashed at import time).
- Header / parameter name suggestions in the Params and Headers editors when a schema is
  available (schema-declared names plus common HTTP headers).
- Chrome-style workspace tab strip with drag-to-reorder, move-between-windows, and
  tear-off-to-new-window.
- Cross-platform publish pipeline (`build/publish.ps1`) and GitHub Actions release workflow
  producing self-contained binaries for Windows, Linux, and macOS (incl. a macOS `.app`).

### Changed

- **Auth is now a per-environment "prestep" that actually applies the credential.** Previously the
  Authorization header was only shown/exported, not sent; the send pipeline now runs an acquire→apply
  prestep and **injects** the resolved headers/query into the outgoing request. OAuth tokens, session
  captures, and Session-kind variables are scoped **per (workspace, environment)** — a DEV token or
  cookie never reaches PROD — and an expired token that still 401s triggers **one re-acquire + retry**.
- Domain auth policy moved to `AuthApplier` / `AuthRequestMerge` in Core (superseding the old
  header-only `AuthHeaderResolver`).
- **Redirects are followed with cross-origin credential stripping**: injected auth headers (including
  custom API-key headers, which .NET's built-in handler does not strip) are dropped on a redirect to a
  different origin, so a token / API key is never replayed to a host on the other side of a redirect.
- **Clean-architecture refactor.** Introduced a distinct **Application** layer
  (`Fubar.Studio.Application`) of cohesive use-case services — `RequestExecutionService` now owns the
  send pipeline (auth → execute → captures/assertions → history) that previously lived inline in the
  request-editor view model. Pushed domain policy down into Core (`AuthApplier`,
  `EffectiveAuthResolver`, `QueryStringSync`, `HttpHeaderNames`, `AuthDefaults`), inverted the
  Presentation→Infrastructure leaks behind Core ports (`IJsonSchemaValidator`, `IJsonPathEvaluator`),
  and replaced hand-wired editor construction with an `IEditorViewModelFactory`. Split the wide
  `IWorkspaceService` into focused role interfaces (`IWorkspaceStore`, `IRequestStore`,
  `IEnvironmentStore`, `IAuthProfileStore`, `IFolderConfigStore`, `IInheritanceResolver`) so each
  consumer depends only on what it uses (ISP), keeping the aggregate for the broad importers. Layer
  boundaries are now enforced by architecture tests (Core→nothing, Application→Core,
  Infrastructure→Core, ViewModels∌Infrastructure, `Fubar.Controls` isolated).
- Projects renamed: app assemblies are now `Fubar.Studio.*`; the reusable, app-agnostic UI
  library stays `Fubar.Controls` (its sandbox is `Fubar.Controls.Gallery`). The desktop app
  ships as `FubarAPIStudio`.

[Unreleased]: https://github.com/Fubar83/Fubar-API-Studio/commits/main
