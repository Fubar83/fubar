# Fubar API Studio — invariants that are easy to break

Behaviour that looks like a detail, is not, and has usually already been broken once. Read the block
before changing the thing it names.

Fubar Diff's are in [`invariants-diff.md`](invariants-diff.md); the two apps no longer share a
dependency graph, so they no longer share this list.

**The same executable is a window AND a batch tool here too, and the CLI's progress is written the
opposite way round from the GUI's** (Studio). `CommandLine.IsHeadless` is checked in `Program.Main`
before Avalonia is configured, exactly as in Fubar Diff, and for the same reason: a run that must exit
with a status code cannot also be showing a window. The list is deliberately short - `--run`, `--help`,
`-h`, `--version` - and nothing else counts, so starting the app normally is untouched. Exit codes are
`diff`'s (0 passed, 1 failed, 2 could not tell), and 2 is kept strictly apart from 1 because a workspace
that would not load and a collection whose assertions failed call for different reactions from a build.
On Windows a GUI executable has no console until `ParentConsole.Attach` runs, which is why `dotnet run`
shows nothing while the built exe does. The asymmetry worth knowing: `CliRunner` reports progress
through a plain synchronous `IProgress<T>` and `CollectionRunViewModel` uses `Progress<T>`, and neither
may adopt the other's choice. `Progress<T>` marshals to the captured synchronization context - which is
what makes the view model safe to touch rows from, and what makes a console process (which has none)
print its lines from the thread pool, out of order and possibly after the summary meant to conclude
them. Both were found by a failing test.


**An OpenAPI import creates as few environment variables as it can, and PATH PARAMETERS ARE NEVER ONE
OF THEM** (Studio). An eight-operation spec used to materialise fourteen variables per environment, of
which three were right. The rule now: the only inferred variables are `baseUrl` and the credentials for
security schemes the document actually references. Four separate reasons, each worth keeping.

*Path parameters go inline in the URL.* One workspace-wide variable per distinct `{name}` is wrong at
scale - a mid-sized API becomes dozens of empty variables - and wrong in kind, because the names COLLIDE:
`/users/{id}`, `/users/{id}/orders` and `/orders/{id}` all resolved to a single `id`, so filling it in for
one request broke the other two. A path parameter belongs to the one request whose URL contains it, and
this app has no request-scoped variables by design (`RequestModel.LocalVariables` is retired), so the URL
is where it lives. It keeps the spec's own `{name}` - single braces, inert to `VariableResolver`, so it
reads as a placeholder rather than an undefined variable - or the example/default when the spec supplies
one, which makes the request runnable. Not `<string>`: `/users/<string>/orders/<string>` throws away which
parameter is which, and the name is the only thing telling the reader what to put there.

*Only referenced security schemes.* `BuildAuthProfiles` used to walk every scheme in
`components.securitySchemes`; a spec that declares four and uses one got four profiles and five
variables, including a Basic username and password for auth nobody asked for. `ReferencedSchemes` collects
what the global and per-operation `security` blocks name. The one exception: a document referencing
NOTHING keeps them all, with a warning, because importing no auth at all would leave nothing to switch on.

*Server variables are substituted, never also copied.* Being both made them inert - `baseUrl` already held
the resolved URL, so nothing referenced them and setting `region` to `eu` changed nothing - and made them
wrong across environments, since one server's variables were copied into every environment, first value
wins, including servers whose URL is literal. Making them LIVE instead would need recursive resolution
(a `baseUrl` containing `{{region}}`), and `VariableResolver.Substitute` is deliberately a single pass.

*A declared parameter that collides with the auth is imported UNCHECKED.* The silent one. Specs routinely
declare `Authorization` as an ordinary header parameter as well as declaring a security scheme; imported
enabled it carried a placeholder, and `AuthRequestMerge` - correctly - refuses to overwrite a header the
request already carries enabled, so `<string>` went out as the Authorization header and the real token
never did. 401s that look like the auth profile is broken. Disabled rather than dropped: the spec said the
parameter exists, and a disabled row cannot suppress the auth, so ticking it back on is a deliberate act.
The same applies to an apiKey-in-query scheme against a declared query parameter.


**An HTTP status never fails a collection run - only an assertion or a transport error does**
(Studio). The load-bearing decision in the runner, and not the obvious one. This app lets you assert
`StatusCode Equals 404` deliberately, so a runner that ALSO treated 4xx/5xx as failure would make the
same response both the expected result and a failure, and one of the two answers would have to win
silently. Deciding which statuses are bad is exactly what assertions exist to do explicitly, so
`RunReport` does not also do it implicitly. The cost is real - a collection with no assertions can
return 500s and still pass - which is why `StepReport.IsUnexpectedStatus` and
`RunReport.UnexpectedStatuses` exist and are surfaced BESIDE the verdict rather than folded into it:
the run does not fail, and the reader is still told. Do not "fix" this by failing on non-2xx. Two
further refusals in `RunReport.Ok` are the same instinct: a CANCELLED run is never green (it did not
answer the question that was asked), and an EMPTY one is not either - "no tests ran, so it passed" is
reachable here by a name filter with a typo in it.


**A collection run is SEQUENTIAL, and that is correctness rather than laziness** (Studio). Captures
write variables that later requests read - the headline case being a login whose token every subsequent
request depends on - so two requests in flight at once is a race on the session store whose outcome
depends on which response came back first. A "run faster" option would silently break exactly the
collections that are worth running. The chaining itself is free, and stays free only because every step
runs against the SAME workspace and environment instances: session variables are scoped per (workspace,
environment) via `SessionScope`, so a token captured by request 1 becomes invisible to request 2 the
moment anything re-resolves either. `CollectionRunServiceTests` pins it.


**The run order is the left pane's order, exactly** (Studio). `RunPlan.From` walks the tree depth-first
in the order the scan produced, and `WorkspaceNodeViewModel.ToTreeNode()` projects the VIEW MODEL tree
rather than re-scanning the directory - so what the user sees is what runs. Ordering is not cosmetic
when captures chain: request 3 routinely depends on request 1, and the tree is the only place that
dependency is written down. A run also addresses requests by PATH and reads each from disk when its turn
comes, so it sends what is SAVED rather than what is open in an editor - the honest behaviour for
something whose whole purpose is to be repeatable, and what will happen when it runs in CI.


**A run reuses `IRequestExecutionService` rather than reimplementing the send** (Studio).
`CollectionRunService`'s own job is only the walking, the stopping and the reporting; auth acquisition,
the 401 retry, captures, assertions and history all behave identically whether a request is sent by hand
or by a run. Anything that works in the editor works in a run, and any difference is a real one rather
than a second implementation drifting from the first - which is why its tests fake at that seam and not
below it. Two failures are deliberately contained rather than fatal: a request file that will not parse
errors THAT STEP and the run continues (throwing would abandon nineteen other requests over one bad file
and hand back an exception instead of the answers already earned), and a capture that could not be
applied is reported on the step without failing it (the request answered; whether a missing field
matters is what an assertion is for). History is OFF by default for runs, the opposite of a single send:
history is capped per request, so a scheduled run would evict the sends people actually go back for.


**Comparison settings inherit PER SETTING, not per level** (Studio). `ComparisonSettings` has every
member nullable precisely so a request overriding one option keeps inheriting the rest;
`ComparisonSettingsResolver.Resolve` folds global → folder(s) → request and reports, for each setting,
both the value and which level it came from. Layers are ordered root-most first (the same order
`GetInheritanceChainAsync` already produces for headers), so "last one wins" means "closest wins". Two
traps: (1) lists REPLACE rather than union - an empty non-null list is a real override meaning "ignore
nothing here", which is what keeps a request's rules readable as the complete truth about that request;
(2) `Studio.Core` must NOT reference `Fubar.Diff.*` (the architecture tests enforce it), which is why
`ComparisonSettings` is a parallel shape rather than a reuse of `ComparisonOptions` -
`ComparisonSettingsMapper` in `Studio.UI` is the single place the two vocabularies meet, and adding a
setting to one side should break its compile until the other side has it too.




**`AppSettings` is grouped, and the flat names survive as READ-ONLY shims** (Studio). It put a theme,
a list of open folders and the root of the comparison hierarchy at one level, which is three
different KINDS of thing: how the app looks, where the user was, and how their work behaves.
`Appearance` / `Requests` / `History` / `Comparison` / `Session` now, with `LegacyTheme`,
`LegacyOpenWorkspacePaths` and `LegacyActiveWorkspacePath` reading the old spellings and returning
null on write, so `FubarJson`'s `WhenWritingNull` keeps them out of new files. `Comparison` stays
nullable and is written as absent when empty: an unticked box means "no global opinion", which is
what lets a folder or request decide instead, and is not the same as globally false.

**Three send/history limits are settings, not constants, and each one has to reach the code that acts
on it** (Studio). `Requests.DefaultTimeoutSeconds` (request's own timeout wins, then the setting, then
a 100 s fallback), `Requests.MaxResponseMegabytes` (a response over it keeps its status, headers and
timing and reports the body was not loaded), and the `History` group. `History.Enabled` off means
nothing is written at all - not a shorter ledger - because history keeps whole response bodies on
disk and a login response body IS a token. `History.MaxResponseBodyKilobytes` of **zero is a real
setting** and must never be clamped up: it keeps the timing and status of every execution and no
payloads, which is exactly what someone who does not want response bodies on disk is asking for.
`History.MaxEntriesPerRequest` IS clamped to at least one, because "record history but keep none of
it" is a slip - `Enabled` is the setting for wanting nothing kept. `RequestSettingsTests` and
`HistoryServiceTests` assert the behaviour rather than the value in the file, which is the only kind
of test that catches "built but never wired".


**The browser half of a sign-in is persisted, and every part of it matters** (Studio). The authorize
URL, the extra authorize parameters, the pinned redirect port, the provider and its tenant live on
`AuthConfig` because none of them fit in `TokenRequest` - the browser round trip is not a request.
They were not saved at all for a while, and the symptom was that a profile reopened with an empty
authorize URL: every session began by rediscovering the provider before the sign-in button did
anything, and the pinned port the provider had been told about came back as ephemeral. Two ordering
rules keep it working. `LoadFrom` loads the sign-in AFTER the token-request branches, because two of
those go through `Seed`, which sets the authorize URL from a template - loading first meant a config
with no token request had its URL wiped by the default template's empty one. And `Seed` sets
`SelectedTemplate` to a CATALOG entry, never to the provider template it was handed: a ComboBox shows
its placeholder for a selection absent from its own items, so "Set up" filled the screen in correctly
and blanked the template box above it.

**The redirect port is pinnable, and that is not a preference** (Studio). It was ephemeral with no
alternative - a different port every attempt - while the editor told the user to register the redirect
URI with their provider, which was impossible for any provider that matches it exactly. Google and
Entra ignore the port on loopback; GitHub, Okta, Auth0 and Keycloak do not, so the feature worked only
for the two lenient providers and printed an unfollowable instruction everywhere else. The URI is now
derived from the port (`AuthorizationCodeFlow.RedirectUriFor`) so it can be shown BEFORE the first
attempt: deriving it from a failure is the worst way to learn it, because the browser shows the
provider's own error page and this app is never told anything at all. An ephemeral port renders as
`<a free port>` rather than a number, so nobody registers a URI that was never going to come back.

**Provider presets are data, and their point is the parts that fail late** (Studio).
`SignInProviderCatalog` holds facts about somebody else's service; `SignInProviderTemplate` is the one
decision - given those facts, what the editor gets filled with. The entries that look like trivia are
the ones worth testing: Google returns a refresh token only with `access_type=offline` and re-issues
one only with `prompt=consent`, so without both the SECOND sign-in for an account silently has no
refresh token and fails an hour later; Google's desktop clients are issued a client secret and the
exchange needs it, while an Entra public client must NOT send one; GitHub's token endpoint answers
form-encoded unless asked for JSON, which would defeat the JSONPath captures and report a 200 with no
token. `SignInProviderTests` pins each of these. Presets are also COPIED into the editor rather than
shared - the catalog's lists are static and the editor's rows are edited in place.

**`AuthorizationCodeFlow.Build` lets an extra parameter override a protocol one, deliberately**
(Studio). Extras are applied last and nothing is reserved. Guessing which of somebody else's
parameters are sacred is how an allowlist ends up blocking exactly the provider it was meant to
support, and a user who needs a different `response_mode` has no other way to say so. What is NOT
negotiable is the state check in `ReadCallback` - that is a security control, not a convenience.

**The loopback listener serves connections until one carries a query** (Studio). It used to accept
exactly one. A browser opens more sockets than it sends requests on - Chrome speculatively
pre-connects - and taking one of those as THE redirect ended the sign-in before the redirect arrived,
reporting "the redirect carried neither a code nor an error" for one still in flight. For the same
class of reason `ReadRequestLineAsync` reads until the line ends rather than taking whatever one
`ReadAsync` returned: a request line carrying a provider-sized code and state can span TCP segments,
and a truncated one is rejected as a state mismatch - the error that means "somebody forged this".
The read is bounded because this socket is reachable by anything on the machine.


**A collapsible `fc:Section`'s header replaces Fluent's ToggleButton theme outright** (Controls). The
header is a `ToggleButton` bound straight to `IsExpanded`, and an expanded section's toggle is
therefore `:checked` - which Fluent paints in the system accent, so the one section open by default
rendered as a solid blue bar across the sidebar. Overriding `Background` on the base style does not
reach it, and neither does adding `:checked`, `:pressed` and `:checked:pointerover` styles nested in
the Section's own ControlTheme: Fluent's ControlTheme wins. `SectionToggleTheme` supplies a Border and
a ContentPresenter instead, the same reasoning as `SeamlessTab.axaml` replacing Fluent's TabItem
template. If a header ever goes blue again, that is what regressed.

**Environments and Auth Profiles are folded by default, and the fold is remembered** (Studio). They
are set up once and then chosen from the toolbar; the request tree is what the pane is for, and those
two groups were taking roughly two hundred pixels off it in every session - one of them spending a
whole row on "No auth profiles yet." The state lives in `SessionState` (`EnvironmentsExpanded` /
`AuthProfilesExpanded`) rather than being session-only, because a group that refolds itself every
launch is more annoying than one that never folded, which would have made the change a net loss.

**The theme switcher is in Settings only, so saving settings must RE-APPLY it** (Studio). It used to
sit in a bordered strip pinned across the bottom of the sidebar, where changing it went through
`ThemeManagerViewModel.CurrentTheme` and applied itself. Now `MainViewModel.CreateSettings` hooks the
settings window's `Saved` event to `LeftPane.Theme.Initialize()`, which re-reads the file that was
just written and applies without re-persisting. Drop that hook and the theme is saved correctly and
does not appear until the next launch - which reads as the setting not working at all.
