# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **API Studio, Fubar Diff and `Fubar.Controls` now live in one repository.** They were briefly three,
  with `Fubar.Controls` shipped as a NuGet package; that was reversed when API Studio needed the diff
  view too, which made the sharing a mesh rather than one-way. Everything is a project reference now,
  and the solution file is `Fubar.slnx` (was `FubarAPIStudio.slnx`).

### Added

- **Comparing responses**, reusing Fubar Diff's view — semantic JSON comparison, character-level
  highlighting and change navigation included:
  - **Pin / Compare** in the response pane. Pin sets the current response aside; Compare diffs the
    next one against it. The pin is app-wide, so the two sends can be different environments or
    different requests — "same request against staging and prod", or before and after a deploy.
    In-memory only: a pinned response is a scratch comparison, not something to write to disk.
  - **Compare** next to Replay in the History tab, diffing a past response against the current one —
    the question Replay leaves unanswered.
- **Ignore rules for response comparison.** Two runs of a real endpoint differ on `requestId`,
  `generatedAt`, `traceId` and a `syncedAt` per array element, so the one field that changed is
  buried. Click **ignore** on any change in the Tree view and it stops being reported — in the text
  view, the diff map and navigation as well as the tree, since the rule is applied where differences
  are decided rather than where they are drawn. **Save to request** persists the rules to
  `request.json`, per request, so they always apply and the team shares them.
  - Ignoring a field inside an array covers every element: clicking `$.items[0].syncedAt` creates
    `$.items[*].syncedAt`, because a noisy field is noisy in every element.
  - Ignoring an object covers everything under it.
  - Rules are hand-editable in `request.json`; `$..timestamp` matches at any depth. A malformed rule
    is skipped rather than failing the comparison.
- History now records the **response body** alongside the outcome, which is what makes the above
  possible. Bodies over 256 KB are not stored (the ledger keeps 200 executions per request), and
  entries without one — too large, empty, or written before this release — show Compare disabled
  rather than opening an empty comparison.

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

[Unreleased]: https://github.com/Fubar83/fubar/commits/main
