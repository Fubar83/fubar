# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Security

- **An Environment-scoped capture no longer writes its value to a tracked file.** The rule deciding
  where a variable's value may live was enforced in the environment editor's Save and nowhere else;
  the capture path assigned `AppVariable.Value` directly and persisted it, so capturing
  `$.access_token` put the token in `environments/*.json` — the file the product tells you to commit.
  When the target variable was marked *Secret*, its on-disk value (documented as always null) was
  overwritten with the real secret. One `IVariableWriter` is now the only code that assigns a value,
  and both callers go through it: an existing variable keeps its kind, so a capture naming a Secret
  variable writes to the OS keyring instead of demoting it.

  A capture into a *Normal* variable whose name looks like a credential still succeeds — you may mean
  it — but says so, and points at Session scope.

- **Execution history is always ignored by Git.** `.fubar/` holds up to 200 responses per request,
  bodies included. The ignore rule was written only when creating a workspace, and only when no
  `.gitignore` already existed — so aiming *New Workspace* at a repository you already had, which is
  the documented way to use it, left all of that tracked. The rule is now appended to an existing
  `.gitignore` (never rewriting it), applied on open as well as create, and `.fubar/.gitignore`
  additionally makes the directory exclude itself, which holds even when the repository root sits
  above the workspace.

- **The status log no longer prints captured values.** It logged `Captured {{token}} = "eyJhbGci…"`.
  The JSON and JUnit reports have always omitted capture values for exactly this reason; the log
  strip is the thing that gets screenshotted into a bug report, so the two now agree. The variable's
  name and destination are logged; its value is not.

> **If you used an earlier build, check your workspaces** — see the advisory in
> [SECURITY.md](../SECURITY.md). Anything found must be **rotated at the provider**, not just
> deleted: a fix cannot un-commit a credential.

### Fixed

- **Opening a request no longer erases the workspace's active environment.** The picker is a two-way
  bound ComboBox, and reloading a workspace empties its list before refilling it — which made the
  selection model write `null` straight back through the binding, indistinguishable from someone
  choosing "no environment". That was persisted, so `activeEnvironmentId` was wiped from
  `fubar.json` on every workspace activation and every time the open request changed workspace. The
  next open then fell back to whichever environment sorted first: a workspace saved on Staging came
  back up on Production, with the picker agreeing. Restoring a selection is not a choice, and neither
  is a collection being refilled underneath one — the suppression now covers the whole reload rather
  than just the assignment at the end.

- **Running a batch whose teardown repeats one of its steps no longer takes the app down.** The Run
  window keyed its rows on endpoint + case, which is not unique: "delete it" is routinely both the
  last step (proving the delete works) and the teardown (cleaning up a run that stopped before
  reaching it) — the shape the docs recommend. The duplicate threw out of `ToDictionary`, from a
  command handler, so the process died on the button press. The command line ran the same batch
  happily, which is how it shipped. Rows are keyed on the step's order now, which every plan
  guarantees is unique, and the two rows for the same case report separately — the cleanup's 404 sits
  under the step's 204 instead of overwriting it.

- **A JUnit report no longer reports a comparison run as all-green.** The writer mapped only the step’s
  status, so a run whose responses DIFFERED from their snapshots — or from the other environment — exited
  1 while the file beside it said `failures="0"` with every test passing. The exit code and the build
  page, which is what anyone actually looks at, gave two different answers to the same run. A difference
  and a missing other side are both failed tests now, named (`3 differences from Production`,
  `No snapshot recorded for Staging.`); the count in the header comes from the same predicate that
  writes the elements, so the two cannot drift apart again. Teardown is described rather than judged,
  matching the rule everywhere else that cleanup never decides the verdict, and a match a tolerance
  forgave says so. The JSON report gained the same second axis per step — `comparison`,
  `comparedAgainst`, `differences`, `tolerated` — plus a `teardown` flag, so a reader can tell cleanup
  apart from what was being tested.

- **Saving a request or a case no longer deletes its rules.** Both editors rebuild their document from
  the screen, and neither copied the parts it does not show: a request's snapshot policy and
  tolerances, a case's comparison settings and tolerances. So a file carrying any of them lost it the
  first time anyone pressed Ctrl+S — silently, and in the rules that keep a token out of a committed
  file.

- **Workspace files no longer carry the app's own bookkeeping.** `System.Text.Json` serialises every
  public getter, so convenience properties were being written into everybody's repository: saving an
  endpoint with a snapshot policy wrote `"isEmpty": false` twice, and a tolerance would have written
  the `kind` derived from the rule beside the rule. Round-tripping never noticed — the extra members
  read back as nothing — so the test that pins this reads the file's text.

- **Running a batch from the left pane works when its name has been changed.** `@smoke` resolves
  against the `batches/` directory listing, but the Run button passed the name from *inside* the file,
  so the two disagreeing made a batch unrunnable by the name shown next to the button.

- **A case's name now takes.** Typing a new one in the case editor wrote it inside the file and left
  the file itself alone, so `get-order#the-name-you-typed` selected nothing — the same divergence as
  the batch above, and it renames the file now for the same reason.

- **A long name in the tree ellipses instead of pushing its badges off the edge.** The tree scrolled
  sideways rather than fitting, so a long request name quietly hid its own auth badge.

- **Switching request no longer discards unsaved edits without asking, and neither does quitting.**
  Only one request is open at a time, so opening another one destroys the outgoing editor's changes;
  that used to write a line to the status log - collapsed by default - and carry on. Closing the
  window did not even do that: there was no handler for it at all. Both now ask, with Save, Discard
  and Cancel. Anything that is not an explicit answer means keep, including a dismissed dialog and a
  save that failed.

- **The Status & Log strip raises itself when something goes wrong**, and carries an unread count on
  a permanent toolbar toggle. Ctrl+` used to be the only way to open it, which meant auth failures,
  capture failures and import failures all reported somewhere with no route to it. Entries now carry
  a severity, can be filtered and copied, and are written to a rolling daily file under
  `%AppData%/Fubar/logs/` kept for seven days - so "send us your log" is answerable at all.

### Added

- **Screenshots, and per-app READMEs that use them.** Nine images under `docs/images/`, every one shot
  against a throwaway petstore workspace and a local stub: a request with its assertions and its
  answer, the Rules tab showing what a case inherited from its folder, a chain finishing with its
  cleanup line, a run judged against another environment reporting one difference rather than five,
  the structural C# panel beside a text diff, two JSON documents whose properties were only shuffled,
  and the Gallery. `docs/images/README.md` says what each has to show for a replacement to still be
  that file, and what is still missing. The three per-app docs and the root README carry them — and
  the broken links in all three, `docs/LeftPane.md` from inside `docs/` plus `LICENSE`,
  `CONTRIBUTING.md` and `SECURITY.md` from a directory up, are fixed.

- **Send one request without setting anything up.** The empty state has a *New request* button that
  opens a scratch request — no workspace to create, no folder to choose, no file written. Previously
  the fastest path from launch to a response was about six deliberate steps, four of them filing
  decisions you cannot make sensibly before knowing whether the request was worth keeping. The scratch
  workspace lives with the app rather than in a folder you picked, and the pane says so.

- **An endpoint has its own batches**, in `<endpoint>/batches/`, shown in the tree beside its cases and
  tagged `case` / `batch` so the two kinds of child are told apart. The workspace's `batches/` stays
  for the occasions that cut across the tree; on the command line they are `@smoke` and
  `orders/get-order@happy`, because a batch name is unique only within one home.

- **Nothing is written until you save it.** New cases, batches, endpoints and requests are drafts: they
  appear in the tree with the unsaved dot, open in their editor, and hit disk on the first Save.
  Opening one and changing your mind now leaves nothing behind — previously every *New case* wrote a
  `new-case.json` immediately. Delete on a draft just forgets it.

- **A folder has its own editor** — *Folder settings…* on any folder, with `Headers · Auth · Rules`. A
  folder is the level almost every shared rule belongs at, and it was the last one you had to edit by
  hand. Its Rules tab tells the folder's own rules from an ancestor's, so removing one still only ever
  stops it *here*.

- **A requests-format workspace says what it is missing**, in one line in the Left Pane, with *Convert
  to endpoints…* beside it. Nothing is converted on open — that decision stands — but the way out was
  previously a context-menu item you had to already know about.

- **Move a request into another workspace.** Right-click → *Move to workspace*, listing the other open
  ones. It stops being where it was, whatever is open on it follows, and the format is checked first.

- **A Rules tab**, on an endpoint and on a case: every rule that applies there — comparison options,
  ignored paths, array identity, tolerances, snapshot redaction and normalisation — each carrying the
  level that set it. The settings hierarchy used to be legible only by opening four files and folding
  them in your head; this is that fold, shown. Inherited rules are in italics with their origin and are
  never edited in place: removing one writes a removal *here*, because a click in one endpoint's window
  must not change what forty others do. Tolerances and snapshot policy had no editor at all before this
  — they were file-only.

  Comparison options are three-state (Inherit / On / Off) and Inherit says what it is inheriting, so an
  option nobody has touched cannot be mistaken for one this level chose.

- **A batch editor.** Steps and cleanup are pickers over the endpoints and folders the workspace has,
  with the endpoint's own cases beside each, so a step is chosen rather than typed — and a step naming
  something that has since been renamed keeps its name in a red box saying "not in this workspace",
  which is what a run would report. Previously a batch was created from the left pane and then edited
  as JSON; `+` now opens the new one straight away.

  A batch's name is its file's name, and changing it renames the file — that is what `@smoke` resolves.

- **Endpoints and cases.** An endpoint is a directory holding `endpoint.json` and `cases/`. The
  endpoint states the operation — method, URL, headers, auth, true every time it is called; a case
  states one invocation — its path parameters, its query, and what it should answer. Wanting a second
  way to call the same endpoint used to mean duplicating the whole file, and the copies drifted.

  Two placeholder syntaxes, deliberately: `{param}` is the endpoint's shape and the case fills it,
  `{{variable}}` is the environment's value. Conflated, there is no way to say which of the two a
  missing value should have come from. A `{param}` with no value is left visible rather than emptied —
  `/orders/{orderId}` says what is missing; `/orders/` is a different request that gets a
  plausible-looking answer from the wrong resource.

  New workspaces are created in this format; existing ones are never converted on open. **Convert to
  endpoints…** does it explicitly — it previews, refuses a name collision rather than guessing, copies
  the originals to `.fubar/backup/` first, carries snapshots across, and stamps the format last so an
  interrupted run leaves a workspace that still opens.

- **Snapshot regression testing.** Record what every request answers, and compare later runs against
  it. `Record snapshots` in the Run window, or `fubar run --update-snapshots` for the first recording
  in CI; `--oracle snapshot` to compare.

  Recording is always a deliberate act — never something a comparison run does when it finds nothing —
  because a snapshot that writes itself on the first failing run tests nothing ever again and does it
  silently. **A missing snapshot is reported and fails the run**; "nothing to compare, therefore fine"
  is how a suite stops testing without anyone noticing.

  Snapshots are **redacted and normalised before they are written**: a token never reaches the file,
  and a timestamp is stored as `<timestamp>` so the file does not churn. The same rules are applied to
  the live response before comparing, or every normalised field would differ on every run.

  Scope is chosen when saving: per environment by default, or shared by all of them. Per-environment's
  failure mode is a redundant file; shared's is a data difference reported as a regression, and a
  regression tool that cries wolf stops being run.

- **Compare two environments in one run.** `Judge by → Compare with Production` in the Run window, or
  `--oracle env:Production`. Everything is sent twice, interleaved one step at a time, and the run
  reports where the two answers differ. The other side goes through the ordinary runner, so auth,
  variables and captures are identical on both sides — any difference in how they were sent would be
  reported as a difference between the environments.

- **Tolerances.** A field that is allowed to move, and by how much: `numeric`, `withinSeconds`,
  `matches`, `lengthWithinPercent`, `oneOf`. The difference between a suite that catches regressions
  and one that ignores half the payload — ignoring a total because it drifts by a cent stops checking
  the total, a tolerance forgives the cent. Both sides have to satisfy the rule, so
  `$.requestId matches ^[0-9a-f]{32}$` forgives a regenerated id and still fails when the field comes
  back as an error message. Forgiven differences are counted and shown, never folded into the green.

- **Batches.** A named list of calls to make together, with what should judge them —
  `batches/smoke.json`, run from the left pane or as `fubar run @smoke`. A batch is an occasion, so it
  sits beside the collection rather than inside it, and its rules are an overlay on whatever each
  endpoint already resolves to rather than another level of the hierarchy. A step naming an endpoint
  or case that is no longer there **errors rather than being skipped**: a batch that quietly shrank
  when something was renamed would keep passing while testing one thing fewer.

- **Chained integration tests, with cleanup that actually runs.** A capture on one step feeds the
  next — create a cat, read it back, rename it, delete it — through a session variable that is never
  written to a committed file. The endpoint's `{catId}` placeholder is filled from `{{catId}}`, so the
  chain reads as four ordinary endpoints rather than one special one.

  A batch's new **`teardown`** list runs after the steps whatever happened to them. `stopOnFailure` is
  the right setting for a chain and is exactly what skips the delete, so without this every red run
  left a row behind. Cleanup never changes the verdict, its assertions are dropped and it is not
  compared — a delete that finds nothing left to delete is the happy path — and what *is* reported is
  cleanup that could not be sent at all. It does not run after a cancellation.

  A capture that found nothing is now printed on the step that could not capture it. It does not fail
  that step, so previously the run blamed the step that *used* the variable, several calls later.

- **`fubar run <selector>`.**

  ```
  fubar run                            the whole workspace
  fubar run orders                     a folder, depth-first
  fubar run orders/get-order           an endpoint, all its cases
  fubar run orders/get-order#default   one case
  fubar run @smoke                     a batch
  ```

  `--run` is the older spelling of the same thing and still works. A selector that matches nothing is
  refused rather than run as an empty plan, and a JUnit test is now named `endpoint#case` rather than
  by the endpoint alone — four tests called `get-order` left CI unable to say which started failing.

- **Rules inherit at every level, and lists say what they ADD and REMOVE.** Comparison settings,
  snapshot policy and tolerances all resolve global → folders → endpoint → case, with a batch overlay
  last. `"ignoredPaths": { "add": [...], "remove": [...] }` replaces wholesale replacement, so a
  request needing one extra rule no longer has to restate its folder's — and no longer silently stops
  inheriting them when it does. A bare array is still read as `add`.

- **Run a collection from the command line, for CI.** `FubarAPIStudio --run --env Staging --report
  results.xml`. The same binary, switched into a batch tool by flags that have no meaning on screen —
  the rule Fubar Diff already uses, so starting the app normally is untouched.

  Exit codes are `diff`'s: **0** everything ran and passed, **1** something failed, **2** the run could
  not be attempted. The third is kept strictly apart from the second because a workspace that would not
  load and a collection whose assertions failed call for completely different reactions from a build,
  and collapsing them would make the first look like the second.

  **JUnit XML** because that is the format every CI system already renders: a failed assertion shows up
  as a failed test on the build page, with its message, instead of a line somewhere in a log nobody
  opens. One `<testcase>` per request rather than per assertion — a request is the thing with a name, a
  duration and a URL, and a page listing "status is 200" twenty times would name none of them. The
  folder becomes the classname, so CI groups them the way the collection does, and a transport failure
  is an `<error>` rather than a `<failure>`, which is JUnit's own distinction and exactly ours.
  `--report results.json` gets the whole report as JSON instead.

  **A captured value is never written to either format.** The headline capture is an access token, and a
  report file is precisely the thing that gets attached to a build and kept. The variable's name and
  whether it worked are recorded; its value is not.

  A run matching nothing exits **1**, not 0 — "no tests ran, so it passed" is one typo in `--filter`
  away. A named environment that does not exist is an error rather than a quiet fall back to none, which
  would leave every `{{variable}}` resolving to nothing and make the failure look like the requests'
  fault instead of a typo's. A report that cannot be written is reported without changing the verdict:
  the run already happened, and turning a passing run into exit 2 over an unwritable path would tell the
  build the wrong thing about the API.

  History is never recorded by a command-line run, and there is deliberately no flag to turn it on.

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

### Fixed

- **OpenAPI import created far too many environment variables, and one of them broke auth silently.** An
  eight-operation spec produced fourteen variables per environment, of which three were correct. It now
  produces two. Four separate causes:

  **Path parameters are no longer variables at all.** Every distinct `{name}` in the spec used to become
  one workspace-wide variable. That is wrong at scale — a mid-sized API turns into dozens of empty
  variables — and wrong in kind, because the names *collide*: `/users/{id}`, `/users/{id}/orders` and
  `/orders/{id}` all resolved to a single `id`, so filling it in for one request broke the other two. A
  path parameter belongs to the one request whose URL contains it, so that is where it now lives —
  keeping the spec's own `{id}`, or the example/default when the spec supplies one, which makes the
  request runnable straight away.

  **Security schemes nothing references no longer create profiles or credentials.** A spec declaring four
  schemes and using one got four auth profiles and five variables, including a Basic auth username and
  password for auth nobody asked for. A spec that references *no* scheme anywhere still gets all of them,
  with a warning — importing none would leave nothing to switch on.

  **Server variables are substituted into the URL and not also copied into the environments.** Being both
  made them inert: `baseUrl` already held the resolved URL, so nothing referenced them and setting
  `region` to `eu` changed nothing. They also leaked — one server's variables were copied into *every*
  environment, first value wins, including environments whose URL is literal and has no such variable.

  **A spec that declares `Authorization` as a header parameter no longer suppresses your token.** This
  one failed silently. Such a header imported as an enabled row carrying a placeholder, and the auth
  merge — correctly — refuses to overwrite a header the request already carries enabled. So `<string>`
  went out as the Authorization header, the bearer token never did, and the 401s looked like the auth
  profile was broken. It is now imported unchecked, with a warning saying why; a disabled row cannot
  suppress the auth, so ticking it back on is a deliberate act with a visible consequence. The same
  applies to an API-key-in-query scheme against a declared query parameter.

### Changed

- **The request tree shows names, not file names.** `Create order.json` is now `Create order`. Every
  request in a workspace is a `.json`, so the extension distinguished nothing while eating the width the
  names need; folders, and any other file, keep theirs. Renaming starts from the name you were looking
  at and `WorkspaceService.RenamePath` puts the extension back, so typing `Login` still lands on
  `Login.json`.

- **Folders fold again**, from a `+` / `−` box on the connector, open to begin with. A filter unfolds
  every folder holding a match, or it would find a request and leave it out of sight; clearing the
  filter leaves what it opened open, because re-folding would undo the folding you did by hand and
  nothing afterwards can tell the two apart. Folded state lives on the node, not the row container, so
  it survives the refresh that runs on every file-system change.

- **The tree has indent rails, and one indent step.** Nesting was carried by a left margin alone, which
  says nothing about which folder a request belongs to; a hairline now runs down each level.
  `WorkspaceNodeViewModel.Depth` is gone with the margin it fed — the row's own `TreeViewItem.Level` is
  the only nesting number left, rather than two that could disagree.

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
