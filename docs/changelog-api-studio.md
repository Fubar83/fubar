# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Run a whole collection.** Right-click a folder — or the workspace — and pick **Run**. Every request
  under it is sent in the order the left pane shows, each one's captures and assertions applied as it
  goes, and the window reports what happened.

  This is what makes captures worth having. A capture writes a variable; a variable is only useful to a
  *later* request; and until now there was no way to run a later request except by clicking it yourself.
  A login that captures `{{token}}` now feeds the nineteen requests after it in one press.

  The run window lists the whole plan before it starts rather than growing a row at a time, because the
  usual reason to watch a running collection is to decide whether to wait for it, and a list that only
  shows what has finished can answer that only by finishing. A request in flight is named while it is in
  flight — the one that hangs is the one you most want identified.

  Options: stop at the first failure (worth turning on for a chain, where carrying on past a failed
  login produces nineteen more failures that all say the same thing and bury the one that matters), a
  delay between requests for rate-limited APIs, a name filter, and history recording — which is OFF by
  default, unlike a single send, because history is capped per request and a run on a schedule would
  otherwise evict the sends you made by hand.

  **A status code never fails a run on its own; only an assertion or a transport error does.** Not the
  obvious choice, so: this app lets you assert `StatusCode Equals 404` deliberately, and a runner that
  also treated 4xx as failure would make the same response both the expected result and a failure, with
  one of those two answers winning silently. Deciding which statuses are bad is the job assertions exist
  to do explicitly. The cost — a collection with no assertions can return 500s and still pass — is paid
  for by flagging every non-2xx nobody asserted on, beside the verdict rather than inside it: the run
  does not fail, and you are still told. A cancelled run is never green either, and neither is an empty
  one, since "no tests ran, so it passed" is reachable by a filter with a typo in it.

  Sequential, never parallel, and that is correctness rather than an implementation shortcut: captures
  write variables later requests read, so two requests in flight at once is a race whose outcome depends
  on which response came back first. A "run faster" switch would break exactly the collections that are
  worth running.

  Each step goes through the same pipeline a single send does, so auth acquisition, the 401 retry,
  captures, assertions and history behave identically whether you press Send or Run — anything that
  works in the editor works in a run. Two things are contained rather than fatal: a request file that
  will not parse errors that one step and the run carries on, and a capture that could not be applied is
  reported without failing the request that answered fine.

  Requests are read from disk when their turn comes, so a run sends what is **saved** — the honest
  behaviour for something whose purpose is to be repeatable.

- **Sign in as a person: Authorization Code + PKCE.** The grant most people expect was missing, and it
  is not a template — it needs a browser, a loopback listener, PKCE and a code-for-token exchange. Pick
  the template, press **Sign in with browser**, approve at your provider, then **Test / Get token**
  exchanges the code.

  Two steps on screen because they genuinely are two: a browser round trip, then an ordinary request.
  Keeping the exchange an editable request is what lets a provider needing one extra field be handled
  by adding it, rather than by waiting for this app to grow a setting. The redirect URI is shown to
  copy *before* the flow runs, because it has to be registered with your provider exactly as written —
  and a sign-in that fails for that reason is the most opaque failure in the grant: the browser shows
  the provider's error page and the app hears nothing at all.

  Always S256; the verifier never appears in the authorize URL; a callback whose `state` does not match
  is refused. The system browser is used rather than an embedded webview, per RFC 8252 — it already
  holds your session, and an embedded view asking for corporate credentials is indistinguishable from
  a phishing page. The code and verifier live in session variables: in memory, never on disk.

- **Discover a provider's endpoints instead of copying them from its docs.** Paste the issuer and press
  **Discover**: the token and authorize endpoints are filled from `/.well-known/openid-configuration`,
  and the provider's own scopes become buttons that append to the scope field. The issuer, the issuer
  with a trailing slash, a bare host and the well-known URL itself all work.

- **The token response is shown, and any field is one click from becoming a capture.** A capture rule
  is a JSONPath like `$.access_token`, and the response it addresses was never shown — so the one step
  needing exact knowledge of the payload was the one step with nothing to look at. After Test you now
  get the status, every capturable path with its value, and the raw body. The response appears on
  *failure* too, which is where `invalid_client` and its description live. Token values are never
  printed in full: finding the field is the job, and a pane that spills a live credential into a
  screenshot is a bad trade for information nobody needed.

### Changed

- **One OAuth engine instead of two behind an invisible switch.** `AuthConfig` carried both a
  fixed-form shape and a token-request shape, and `TokenRequest == null` silently chose which
  implementation ran — which is how a guard ended up on the branch nobody was on. Legacy configs are
  now upgraded on the way in and run down the single path, and the preview goes through the same
  upgrade so it cannot disagree with what is actually sent. A config asking for HTTP Basic client
  authentication still sends Basic.

- **The OAuth editor now says what it is for, and what it needs, before you press Test.** Two
  additions, both aimed at the same thing: the editor was a request builder with no stated
  relationship to the requests it serves.

  A line at the top states the outcome permanently — *"Requests using this profile send:
  `Authorization: Bearer {{oauth2_access_token}}`"*. The provider already produced that sentence, but
  only inside the **Verify request** preview, behind a button nobody presses before they are already
  lost. Without it the captures grid reads as a set of unrelated scratch values rather than the thing
  that feeds the header.

  A line above **Test / Get token** lists the `{{variables}}` the token request reads and which are
  undefined — *"Not defined: `{{token_url}}`, `{{client_id}}`, `{{client_secret}}`"*. The per-field
  tooltip already tints the box under the pointer, which answers for the box you are hovering; the
  variable nobody defined is usually in a field you are not looking at.

### Fixed

- **`{{variables}}` now resolve when testing OAuth from an auth profile.** Test and Verify in the auth
  profile editor passed `activeEnvironment: null`, so `{{...}}` resolved against workspace variables
  only - never the environment. That is precisely backwards for OAuth, where the token URL, client id
  and client secret are the things that DIFFER between dev, staging and production, and are therefore
  exactly what people put in an environment. Testing the same profile from a request's Auth tab worked,
  because that path passed the real environment; the two disagreed with no explanation, which is the
  worst shape a bug can have.

  A profile genuinely has no environment of its own - that was the original reasoning - but it is only
  ever *used* from a request, and a request runs under an environment. Testing without one tested
  something that never happens. The environment is now read at test time rather than captured, so
  switching environment with the editor open does what it looks like it does.

- **An unresolved variable is now named, instead of being sent.** Substitution leaves what it cannot
  resolve exactly as it found it, so a token URL of `{{authHost}}/oauth/token` travelled onward as that
  literal string and came back as an invalid-URI error - or worse, a 404 from a real server. The cause
  and the symptom were in different places and the symptom named the wrong thing. The token request now
  stops before it is sent and says which variables are undefined, all of them at once rather than one
  trip round the loop each.

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
