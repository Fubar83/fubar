# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **You can create a workspace.** The command to do it existed and was bound to nothing: the only
  route in was *Open Workspace*, which asks you to pick an existing `fubar.json`. On a first run there
  is no `fubar.json` to pick, so the app could be installed and then not started - and the empty state
  said "Open a folder containing a fubar.json to get started", which is a dead end for exactly the
  person most in need of a way forward.

  **New Workspace…** now sits beside Open in the empty state, and under the `+` in the title bar. It
  takes an empty folder and lays out `fubar.json`, `collections/`, `environments/` and a `.gitignore`
  for the local-only execution history, then opens it - ready to build collections and environments
  in, or to import into with the OpenAPI, Postman and cURL importers that were already there.

  `environments/` is new to that list. Saving an environment creates the folder on demand, so it was
  never load-bearing - but a workspace whose layout is visible from the first second is what makes
  "these are ordinary files you can commit" legible before the first save rather than after it.

  What a new workspace CONSISTS OF moved out of the click handler into `IWorkspaceStore`, where it is
  a fact about the format rather than a decision made by a button - and where it can be tested, which
  it could not be before. Pointing it at a folder that is already a workspace opens it untouched: the
  commonest way to get there is browsing to the wrong folder, and rewriting someone's manifest over a
  misclick is unrecoverable in a way that opening the wrong workspace is not.

- **Comparison settings now inherit from global → folder → request, each setting overridable on its
  own.** Previously the only comparison setting that could be configured or remembered anywhere was the
  ignore-path list, and only on a request; everything else (ignore whitespace, ignore case, reformat,
  report key order, arrays by position, null-vs-missing, array identity keys) was pinned to its default
  with no UI to change it even for one session. All of them are now settings you can set at any of the
  three levels, and each resolves independently — overriding one on a request leaves the rest following
  the folder or your global preference. Every control names where its value came from, and **Save**
  offers the request, its folder, or your global defaults. Existing `request.json` files keep working:
  a pre-hierarchy `responseDiffIgnorePaths` list is read as an ignore-path override and rewritten into
  the new shape the next time that request's settings are saved.

- **The diff window has comparison options at all.** Every setting sits behind a single **Settings ▾**
  button, alongside a **Reset to inherited** — one control added to that toolbar rather than the six it
  would have taken to lay them out flat, in a dialog already carrying navigation, the ignore action,
  the detail-pane toggle and the view switch.

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
  buried. Select a difference and press **⊘ Ignore this field** in the toolbar — the responses stay
  side by side while you walk the noise out — or click **ignore** on a change in the Tree view. Either
  way it stops being reported — in the text view, the diff map and navigation as well as the tree,
  since the rule is applied where differences are decided rather than where they are drawn.
  **Save to request** persists the rules to `request.json`, per request, so they always apply and the
  team shares them.
  - Ignoring a field inside an array covers every element: clicking `$.items[0].syncedAt` creates
    `$.items[*].syncedAt`, because a noisy field is noisy in every element.
  - Ignoring an object covers everything under it.
  - An ignored difference is still drawn, as a barely-there grey band, so "these are the same" stays
    distinguishable from "this is being ignored" — but it forms no region, is not counted, and
    next/previous steps straight over it. The status line reports the ignored count separately.
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
