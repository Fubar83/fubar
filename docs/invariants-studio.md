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


**Applying a template MERGES; only loading a saved profile replaces** (Studio). `Seed` used to
overwrite the URL, every header, every body field and every capture rule, so the natural order of
setting OAuth up - Discover your endpoints, fill in your client id, then change your mind about the
grant - threw all of it away, and pressing Apply a second time to fix one field threw away the fix.
`TemplateSeedMerge` (Core, pure, tested) holds the rule: **a template seeds STRUCTURE and never
overwrites an answer only the user has.** A whole-value `{{placeholder}}` is the template SAYING it
does not know, so anything already there wins; a real value - a named provider's endpoint, a literal
`grant_type` - does replace, because that is what applying a template is for. `{{BaseUrl}}/oauth/token`
is deliberately NOT a placeholder: whole-value only, or the merge would delete exactly the composed
URLs people work hardest to get right. Capture rules are keyed on the variable they write, and an
existing rule is kept WHOLE so a corrected JSONPath survives. `Seed(template, replace: true)` is the
one exception, used by `LoadFrom`, where there is no user work to protect and merging would blend two
unrelated configurations.

**Providers are entries in the ONE template list, not a second picker** (Studio). There used to be a
provider ComboBox with its own Apply button, inside a box that only appeared once the
authorization-code template had already been applied - so "sign in with Google" was four interactions,
the first two of which required knowing that Google's sign-in IS an authorization-code grant, which is
the exact knowledge the presets exist to not require. `TemplateOptions` is now the provider templates
plus the non-sign-in catalog entries, and it is CACHED: these are records holding lists, so two
separately constructed copies are not equal and a ComboBox whose SelectedItem is not one of its own
items shows its placeholder instead. `ApplyTemplate` rebuilds the provider template for the CURRENT
tenant rather than using the listed copy, which was built with the provider's default - otherwise
applying after typing a tenant id quietly sets Entra's URLs back to `/common`. The catalog keeps its
generic authorization-code entry, out of the list, so profiles saved before this still resolve by
grant.

**Say that the merge happened** (Studio). `ApplyStatus` reports what was filled in and that what was
entered was kept. The merge is invisible otherwise, and someone burned once by a template wiping their
client id will not press the button again to discover it now behaves.


**Ctrl+Enter and Ctrl+S live in MainWindow's KeyBindings, and the buttons carry no HotKey** (Studio).
They used to be `Button.HotKey` on Send and Save, which is why they worked on a request and existed on
neither the environment nor the auth-profile editor. `Button.HotKey` registers into the WINDOW's own
KeyBindings collection, so adding a window-level binding beside one fires the command twice - and
sending a request twice is not a harmless duplicate. One binding each, dispatching through
`SendActiveCommand` / `SaveActiveCommand`; `ISaveableEditor` is what lets Ctrl+S reach all three
canvas surfaces without the shell knowing which is open.

**The response pane's ROW is collapsed, not just its content hidden** (Studio). Before the first send
it was three stacked empty states for one message - a strip saying "No response yet", four view tabs
that could do nothing, and an empty editor showing line number 1. `IsVisible` alone would have left
the editor exactly as cramped, because a Grid keeps a hidden child's row at full size; CLAUDE.md
records the same trap costing Fubar Diff a 190px band. `MainWindow.ShowResponsePane` zeroes
`CanvasSplit.RowDefinitions[1]` and `[2]` instead, from code-behind, because a `RowDefinition` is not
in the visual tree and inherits no DataContext for a binding to resolve against.

**The two toolbar rows were NOT merged, and `Window.WindowDecorationMargin` is why** (Studio). Putting
the control bar's contents into the title row would save a row and a rule, and that row has ~700px of
dead space. It needs the caption buttons reserved - right on Windows, left on macOS - and
`WindowDecorationMargin` looks like the answer but is a TOP inset describing the title-bar height: it
pushed the whole row down 30px, clipped the workspace tabs out of the 38px row, and left the
environment selector under the close button. Verified by screenshot and reverted. Anyone trying again
needs a real per-platform caption-button width, not that property.

**A request that sends NO auth is badged "No auth", not "Auth"** (Studio). `HasAuthOverride` is
`Auth.Type != Inherit`, which is true for `AuthType.None` too - so the one request in a collection that
must go out unauthenticated rendered exactly like the ones carrying a token, in the same blue pill
reading "Auth". `RequestSummary.SendsNoAuth` separates them, and `BadgeAuthNone` - a palette token that
had been sitting there referenced by nothing - is what it is drawn in.


**Right-clicking a tree row SELECTS it first** (Studio). Every command in the explorer's context
flyout reads `SelectedNode`, and the flyout is attached to the TREE rather than to a row - so
right-clicking one request and choosing Delete deleted whichever one happened to be selected. That is
the worst version of this bug, because the menu appears next to the row you aimed at.
`TreeView_OnPointerPressed` sets the selection on a right press, which is what Explorer, Finder and VS
Code all do; right-clicking empty space clears it, so New Request there creates at the workspace root.
The handler is TUNNELLING - the flyout opens on the bubbling pass, so selecting afterwards would be
selecting after the menu had already decided what it applied to - and it declines to act while a rename
is in progress, since stealing the selection would commit that rename by side effect.

**A switched-off key/value row is FADED, not merely unticked** (Controls). The tick box was the only
signal that a header or parameter was not being sent, and an empty box is not a signal - it is a
control, and a reader scanning a list of headers has no reason to read it as state. The row's Grid
takes its Opacity from `!Enabled`. Opacity and NOT `IsEnabled`: a switched-off row must still be
editable, because fixing a value before turning it back on is the whole reason to switch one off.

**The token-response button names the variable it writes** (Studio). It said "Capture", which is this
codebase's word rather than anyone else's and says nothing about where the value goes. It says "Save as
{{oauth2_access_token}}" now - the same variable the `Authorization: Bearer` line at the top of the
screen shows, so the connection between the two is on the button rather than in a paragraph above it.
A field already captured says so instead of offering a button that silently does nothing on the second
click. `CapturableField` exists for this; `CaptureFromResponseTests` covers the wiring, which was the
half that had no tests - `TokenResponseFields` (reading the payload) always did.


**Folding lives on the node, not on the container** (Studio). The request tree folds again, from a `+`/`-`
box on each folder, and `TreeViewItem.IsExpanded` is two-way bound to
`WorkspaceNodeViewModel.IsExpanded` rather than left on the container. `SyncChildren` reconciles the
tree against the file system on every change, and a fold remembered by the container would spring open
each time someone saved a request. Folders start open: a workspace is a few dozen requests, and opening
one to a wall of folded folders hides the only thing the pane is for.

A filter unfolds every folder with a matching descendant, or it would find a request and leave it out of
sight. That rule was written once before, against an `IsExpanded` bound to no `TreeViewItem` at all, so
it had never once reached the screen; it is live now, and `TreeFilterTests` asserts it. Clearing the
filter leaves what it opened open - re-folding would undo the folding a person did by hand, and nothing
afterwards can tell the two apart.

**Both title bars draw the app icon themselves** (both apps). `ExtendClientAreaToDecorationsHint` means
the OS paints no icon in that row, so without an `Image` there the window carried no mark of its own
anywhere and the taskbar entry, the alt-tab card and the title bar disagreed about what the application
looked like. It is the 256px PNG, not the `.ico`: an `Image` picks one frame of an `.ico`
unpredictably, while the PNG scales down cleanly. In both apps it replaced the product name in text -
which named the application you are already inside while pushing the tabs off the left edge.


**The app mark is one transparent glyph - no tile, no theme switch** (both apps). Every icon these
apps ship is the bare red glyph on transparency, so it sits ON the surface behind it rather than on a
card laid over it: the title bar in either theme, and the taskbar, dock and alt-tab card, where a
plaque would have put a visible edge around the one icon in the row that has one.

The glyph used to sit on an off-white rounded tile, which made it the brightest thing in a near-black
title bar and needed a second dark-tiled PNG plus a `ThemeDictionaries` switch to fix. With the plaque
gone those two files were the same image, so each app's `App.axaml` declares a single `AppMark`
ImageBrush and the title bar paints a Border with it. An ImageBrush and not an `Image.Source`, because
a `DynamicResource` has to resolve to an object and a brush is the one image-shaped thing that already
is one. The glyph stays the same red in both themes: it is legible on either ground, and a mark that
changed colour with the theme would read as two different applications.

With no tile edge to keep clear of, the geometry in `tools/IconGen` runs about a fifth wider and taller
than it did - that margin was the plaque's, and at 16px it is legibility.

Asset paths there are RELATIVE, not `avares://`. The authority in an avares URI is the ASSEMBLY name -
`FubarAPIStudio`, not the project name `Fubar.Studio.UI` - and getting it wrong builds cleanly and then
throws `FileNotFoundException` at startup, on whichever theme happens to be selected first.


**A missing other side is never a pass** (Studio). Every oracle - a snapshot, another environment -
answers with an `OtherSide` that is `From`, `Missing` or `NotApplicable`, and `Missing` reports
`ComparisonVerdict.Unavailable`, which `RunReport.Ok` counts as a failure. "Nothing to compare,
therefore fine" is how a suite stops testing without anyone noticing, and it is the failure mode this
whole feature exists to refuse: a first run against no snapshot exits non-zero unless
`--update-snapshots` was given. `NotApplicable` is the ONE exception and means the step never
answered - reporting "no snapshot" there would blame the wrong thing.

The row's verdict is a SECOND AXIS, not a `StepStatus` value. A request can pass every assertion it
has and still differ from its snapshot; one enum would have to pick a winner and lose the other. Both
appear on a run row and in the CLI's line, and the comparison verdict beats the status when they
disagree - reading only the status printed "ok" against every row of a run whose summary then said
"1 differ".

**Recording is a separate act, never something a comparison run does when it finds nothing** (Studio).
`ISnapshotRecordingService` is its own service, `--update-snapshots` its own flag, refused when
combined with `--oracle snapshot` or `--oracle env:`; the run window has its own button for it. A
snapshot that writes itself on the first failing run tests nothing ever again and does it silently. A
step that could not be sent is reported rather than written as an empty snapshot every later run would
then agree with.

**The same redactions and normalisations run on BOTH sides** (Studio). A snapshot is written with
`"generatedAt": "<timestamp>"`; the live response carries the real value. Comparing them as they are
reports a difference on that field on every single run - so the rule written to stop the churn causes
it instead, which is worse than having no rule. `SnapshotRecorder.ForComparison` applies the resolved
policy to both sides in `CollectionRunService.JudgeAsync` and is idempotent, so re-applying it to the
already-normalised side changes nothing. Do not "optimise" it away on the stored side: one code path
for both is what stops the two drifting.

**Tolerances need both sides to satisfy the rule, and only apply to a SEMANTIC comparison** (Studio).
`$.requestId matches ^[0-9a-f]{32}$` forgives a regenerated id and still fails when the field comes
back as an error message - that is the whole difference between a tolerance and an ignore. A rule
stating no allowance, or several, is REPORTED rather than guessed at, and a text comparison has no
fields to name, so the rules are reported as unapplied rather than forgiving whole hunks by accident.
Forgiven differences are counted and shown ("3 within tolerance"), because a rule that turned out to
be too generous is otherwise invisible until it hides a real regression.

**`format` in `fubar.json` decides which shape a workspace is in - except in the tree** (Studio). One
field, read when the workspace opens, so nothing has to work the answer out from what it finds on
disk; new workspaces are created in the endpoints format and existing ones are never converted on
open. The one deliberate exception is `WorkspaceService.ScanDirectory`, which recognises an endpoint
by the presence of `endpoint.json`: the tree has to describe what is actually there, and a
half-converted workspace whose tree showed a folder of stray json files would be a tree nobody could
act on. A requests-format workspace has no `endpoint.json` anywhere, so it costs nothing.

`WorkspaceFiles.KindOf` had to learn the same distinctions. "Any `.json` under `collections/` is a
request" stopped being true the moment an endpoint directory held cases and snapshots too, and
without the extra cases every recorded snapshot was validated against the request schema and reported
as a malformed request.

**Several steps of a run can share one file, so nothing may be keyed on the path alone** (Studio).
Every case of an endpoint runs the same `endpoint.json`. `RunPlan.From`'s de-duplication, the run
window's row lookup and anything else that indexes steps must key on the case as well - keying on the
file threw on the duplicate in one place and silently ran the first case only in another. What a step
was SENT is `RunStep.SubjectPath` (the case file when there is one), which is what a snapshot is keyed
by; what identifies it to a reader is `QualifiedName` (`endpoint#case`), which is the JUnit test name.

**A batch is an occasion, not a level of the hierarchy** (Studio). It cuts across the tree - the same
endpoint appears in a smoke batch and a nightly one - so its rules are an OVERLAY applied after the
containment chain resolves. Folded into the chain, an endpoint's effective settings would depend on
which list happened to name it. Its steps run in the batch's own order, never sorted: a batch that
starts with a login is stating a dependency. A step naming an endpoint or case that is not there is
carried as a step with `RunStep.Unresolved` set and reported as `Errored`, never dropped - a batch
that quietly shrank when something was renamed would keep passing while testing one thing fewer.

**A selector resolves against the TREE, not the file system** (Studio). `TreeLookup` walks the nodes
by name, so a selector can only name something the tree shows - which is also what stops
`../../etc/passwd` naming a file. A selector that matches nothing is REFUSED rather than run as an
empty plan, for the same reason an empty filtered run exits 1: a typo in a CI script must not pass.
`#` and `@` rather than more path segments, because `orders/get-order/default` cannot be told from a
folder called `default`.


**Teardown is cleanup, not test, and every rule about it follows from that** (Studio). A chain that
creates something has to remove it again, and `stopOnFailure` - the right setting for a chain -
guarantees a failure in the middle skips the delete, so every red run leaks a row. `Batch.Teardown`
steps are held back by `CollectionRunService` and run after everything else.

They are excluded from the verdict (`RunReport.Judged` is what every count is over), their case's
assertions are DROPPED and the oracle skips them. That last pair is not tidiness: a batch reusing
`delete-cat#created` as teardown reuses a case expecting 204, and on a successful run - where the
delete already happened as a step - the cleanup finds 404. Judged, that would report a failed cleanup
on every green run, which is precisely the crying wolf teardown exists to avoid. What still counts is
whether it could be SENT (`RunReport.CleanupFailed`), which is the real leak signal, and it is always
said in the summary because a leak nobody hears about is the whole problem.

Cleanup does NOT run after a cancellation. Sending four more requests after Ctrl-C is the opposite of
stopping; that leaks, and it is the lesser surprise of the two.

Nothing about a cleanup row may render as a failure - `CliRunner` prints `WARN`, not `FAIL`, and
`RunStepRowViewModel.IsFailed`/`IsErrored` are false for one so the row is amber rather than red. A
red row inside a run reported as passed is a report arguing with itself.

**A capture that found nothing is printed on the step that could not capture it** (Studio). It
deliberately does not fail that step - the request answered, and whether a missing field matters is
what an assertion is for - so the line is the only thing that can point back at it. The failure lands
several steps later as a `{{variable}}` that never resolved, by which point nothing else does. The run
window has always shown "N captures failed" on the row; the CLI, which is where CI reads this, did
not, so a chained run blamed the step that USED the variable rather than the one that failed to set
it.

**A batch's name is its FILE's name** (Studio). `@smoke` on the command line resolves through
`IBatchStore.FindBatchAsync`, which matches against the `batches/` directory listing - never against
the `name` inside the files. So a batch whose two names disagree is one that nothing can run by the
name it displays, and the left pane had exactly that bug: `BatchRowViewModel.Name` was the document's
name and `MainViewModel` passed it to the planner, which looked for a file by it.

The row now carries the file name, and the batch editor's name box renames the FILE - written first,
renamed second, so a rename that fails leaves the batch where it was with its new contents rather than
a saved document nobody can find. `IBatchStore.RenameBatch` refuses a name that is not a file name
(both separators rejected explicitly: `Path.GetInvalidFileNameChars` reports only NUL and `/` on Unix,
so a backslash would pass there and produce one file on Windows and another on Linux out of one
workspace) and refuses to replace an existing batch.

**A step whose target is gone keeps its name, and says so** (Studio). `BatchPlanner` turns an
unresolvable step into one that ERRORS rather than skipping it, because a batch that quietly shrank
keeps passing while testing one thing fewer. The batch editor says the same thing earlier - a red
border and "not in this workspace" - and `ToModel` keeps the step. The row builds its OWN target list
containing whatever it names, because a `ComboBox` renders nothing when its selection is not among its
items: the first build of this shipped the one row worth looking at as the one blank box on the screen.

**Provenance is carried per rule, not per level, all the way to the UI** (Studio). `ResolvedPath`,
`ResolvedTolerance` and `ResolvedSnapshotRule` each hold the `ComparisonScope` and source name of the
level that contributed them, so the Rules tab can say "inherited from Folder: orders" beside a rule the
endpoint did not write, and its `✕` knows whether removing it means deleting a local addition or
writing a removal. `ResolvedSnapshotPolicy` documented this and did not do it; the resolver had the
layer in hand and dropped it.

**An inherited rule is stopped HERE, never edited where it was written** (Studio). Removing one from
the Rules tab appends the path to this level's `remove` list (§4.3); removing a local one deletes it,
and does NOT write a removal of something nothing above ever added. Re-adding what this level had
stopped drops the removal first, so a file never says both `remove $.id` and `add $.id`. A click in
one endpoint's window must not change what every other endpoint under that folder does.

**A comparison option in the Rules tab is three-state** (Studio). Inherit / On / Off, and Inherit shows
what it resolves to and who decided. A checkbox would make every option this level never mentioned look
deliberately set, and toggling one off and on again would leave a local override behind that keeps
overriding forever. Going back to Inherit drops the level's whole `comparison` section when nothing
else in it is set - and an `ignoredPaths` that adds and removes nothing counts as nothing, or the file
records a level that deliberately said something when it said nothing.

**A computed property on a model is written to everybody's repository** (Studio).
`System.Text.Json` serialises every public getter, and these files are committed and read in diffs. It
had already happened - saving an endpoint with a snapshot policy wrote `"isEmpty": false` twice, and a
tolerance would have written the `kind` derived from it beside the rule itself, where a hand edit would
leave it stale. Every one of them is now `[JsonIgnore]`, pinned by
`ComputedPropertiesTests`. Round-tripping does not catch this: the extra members deserialise back to
nothing and every existing test passes. The file's TEXT is what is wrong.

**An editor that rebuilds its model must carry every field it does not show** (Studio).
`RequestEditorViewModel.BuildRequestModel` never copied `Snapshot` or `Tolerances` and
`CaseEditorViewModel.ToModel` never copied `Comparison` or `Tolerances`, so a file carrying any of them
lost it the first time anyone pressed Ctrl+S - silently, and in the rules that keep a token out of a
committed file. Both build a fresh model rather than mutating the loaded one, which is the right shape
and is exactly why a new field has to be added in two places. `_original.LocalVariables` and
`_original.Settings` were already being carried for this reason; the rules were simply forgotten.

**An endpoint's Children are what a run of it SENDS - nothing else may live there** (Studio).
`RunPlan.Walk` expands an endpoint into its children and treats an endpoint with NONE as one to send
as it stands. Two things therefore stay out: an endpoint's own batches, which hang off
`WorkspaceTreeNode.Batches`, and drafts, which `ToTreeNode` filters. Either in `Children` produces the
same silent failure - an endpoint whose only child was a batch would send nothing at all, and a case
that has not been saved would go into a run as a step whose file does not exist. An empty run reported
as a pass is what this whole area exists to refuse. `EndpointBatchPlanTests` and `DraftNodeTests` pin
both.

**A batch has two homes, and a name is unique only within one** (Studio). The workspace's `batches/`
holds the occasions that cut across the tree; an endpoint's holds the ways of running that endpoint.
So the selector grammar has `@smoke` and `orders/get-order@happy`, and a bare name NEVER searches the
endpoints: two endpoints may each have a `happy`, and resolving a bare name across both would make it
mean whichever was scanned first. `BatchPlanner.OwnerDirectory` refuses an owner that is not an
endpoint, because only an endpoint and the workspace hold batches.

**A draft is the one thing in the tree that disk does not account for** (Studio). New cases, batches,
endpoints and requests are held in memory until the first Save, so opening one and changing your mind
leaves nothing behind. The tree is reconciled against a fresh scan on every watcher event, so
`SyncChildren` exempts a draft twice: never removed for being absent from a scan, and no longer a
draft the moment the scan does report it. They sort last, because reconciliation moves the real rows
into scan order around whatever position a draft holds. Deleting one only forgets it, and renaming one
moves no file - it points the reservation at a different name and tells the open editor to follow, or
Save writes the name that was just replaced.

**A case, a batch and a request are addressed by their FILE name** (Studio). `get-order#not-found`,
`@smoke` and `orders/get-order` all resolve against a directory listing, never against a `name` field
inside a file. So an editor with a name box has to rename the FILE - `IEndpointStore.RenameCase` and
`IBatchStore.RenameBatch`, written first and renamed second so a failed rename leaves the contents
saved rather than a document nobody can find. The request editor has no name box, and renaming a
request stays the tree's inline rename. One `DocumentName.IsValid` for all of them: the rule is about
file names, and the second copy of it started life as a batch-shaped predicate being asked about cases.

**Resolving rules forgives a file that is not there, and nothing else** (Studio).
`RequestComparisonSettings` opens on drafted requests and cases whose files do not exist yet; those
levels simply contribute nothing. A file that EXISTS and cannot be read still throws, because judging
with a fraction of the rules is precisely the failure that type exists to prevent.

**The tree row has no width for a second count chip** (Studio). The pane is 260px and every row
already carries a method badge and an auth badge. Adding a batch count beside the case count pushed
the auth badge off the right edge; disabling the tree's horizontal scrolling then ellipsed the NAME to
"ge..." instead, which is the worse trade. The counts take turns (`ContentsText`) - cases when there
are several, batches when there is no case count - and the horizontal scrollbar stays off, which is
also what finally made a long request name ellipse instead of pushing its badges out of sight.

**A bound collection must be ONE instance, mutated - never a fresh projection** (Studio).
`WorkspaceNodeViewModel.DisplayChildren` merges an endpoint's cases and batches, and it was written as
a computed property returning a new list on every read. Every notification then handed the TreeView a
different collection, so the child containers were rebuilt and any selected case or batch lost its
selection: opening one DESELECTED the very row being edited, and `New Case`, `New Batch` and `Move to
workspace` - all of which key off the selection - stopped being offered the moment you opened the
thing you wanted to add to. It is now one collection reconciled in place, and the node subscribes to
its own `Children`/`Batches` so a draft added directly by the explorer is picked up too. The tests
assert the instance is the SAME after a rescan, which is the part that matters.

**Only the two surfaces that are not in the tree clear its selection** (Studio). An environment and an
auth profile have no row, so opening one deselects the tree; a case and a batch DO have rows, and
clearing for them was throwing away the highlight and the selection-gated commands together.

**A list built by awaiting per item must be published in one step** (Studio).
`BatchesSectionViewModel.ReloadAsync` cleared `Rows` and then refilled it one `await` at a time, so two
overlapping reloads - which switching workspace and re-opening an editor do within milliseconds - both
cleared and both added, and every batch appeared twice. It builds a local list, checks a generation
counter, then publishes; a stale read cannot win, and the list no longer blinks empty on the way.

**Everything derived from the workspace FORMAT is re-raised in one place** (Studio). Two things change
it - switching workspace tabs and converting one - and they had drifted: the conversion re-raised
three of the derived properties and not the rest, so after converting, the pane still said "REQUESTS"
and still offered to convert a workspace that already had. `RaiseFormatChanged` is called by both, so
the next derived property cannot be added to only one of them.

**A folder's chain is anchored on its own `_folder.json`** (Studio). `GetInheritanceChainAsync` walks
up from a file's PARENT, so naming the file inside the folder is what makes the folder itself the
innermost level rather than the one above it. And a `RuleLevel` at a folder must carry the source NAME
as well as the scope: every folder in the chain is `ComparisonScope.Folder`, so matching on scope alone
made a grandparent's rules look like this folder's own - and then offered to delete them from here,
which is what "an inherited rule is never edited in place" exists to prevent.

**A test that waits on `Progress<T>` with a sleep is a flake** (Studio).
`EnvironmentPairRunServiceTests` slept 50ms for the synchronization context to drain and failed about
once in six runs on a loaded machine. What those tests assert is the ORDER the service reports in, so
they use an inline `IProgress<T>` that records on the calling thread and wait for nothing. A test that
fails intermittently teaches people to re-run rather than to look.

**A bound collection being refilled is not a user making a choice** (Studio). The active-environment
picker is a two-way bound `ComboBox`, and `EnvironmentManagerViewModel.LoadForWorkspaceAsync` empties
`Environments` before refilling it - so the selection model writes `null` back through the binding
mid-reload, and that arrived at `OnActiveEnvironmentChanged` indistinguishable from someone picking
"no environment". It was persisted, so `activeEnvironmentId` was erased from `fubar.json` on every
workspace activation and every time the open request changed workspace. The next open then fell back
to `Environments.FirstOrDefault()`: a workspace saved on Staging came back up on Production with the
picker agreeing, which is a run sent somewhere nobody chose. `_suppressPersist` therefore covers the
WHOLE reload in a `try/finally`, not just the assignment at the end - and `ClearWorkspace` raises it
before touching the collection for the same reason, even though nulling `ActiveWorkspace` first
happens to make its own write-back harmless today. Any other two-way bound collection in this app has
the same trap waiting in it.

**Run rows are keyed on `RunStep.Order`, because nothing else about a step is unique** (Studio).
`CollectionRunViewModel.Begin` used endpoint + case, which two steps of an ordinary batch routinely
share: a teardown step is usually the SAME case as one of the steps above it, since "delete it" both
proves the delete works and cleans up a run that stopped before reaching it - the shape
`docs/integration-tests.md` recommends. `ToDictionary` threw on the duplicate from inside an async
command handler and took the process down on the button press, while `fubar run` ran the same batch
happily. Last-wins would stop the throw and be wrong: the two rows are the same case and each has to
show what IT did, so the cleanup's 404 sits under the step's 204 instead of overwriting it. `RunPlan`
renumbers every plan 1..n including teardown, which is what makes `Order` safe to key on.

**Anything a batch STATES has to reach both entry points** (Studio). `CliRunner` honoured a batch's
`stopOnFailure` and `delayMs` from the day batches existed; `CollectionRunViewModel` read its oracle
and its environment and stopped there. So the same batch ran differently depending on whether it was
started from the button or from `fubar run`, and the window's checkbox showed the wrong state while
doing it - worse than showing no checkbox at all, because it looks like an answer. The window opens on
what the batch asked for and stays editable, so changing your mind for one run does not mean editing
the file. When a field is added to `BatchOptions`, both places need it.

**The window reads its arguments too, and opens what they name BESIDE the session** (Studio).
`FubarAPIStudio path/to/workspace` did nothing at all for several releases: everything with a meaning
on the command line is handled by `CommandLine.IsHeadless` before Avalonia is configured, and the
window that started afterwards restored its last session and ignored `args`. `StartupWorkspace` is the
counterpart of Fubar Diff's `StartupFiles`, injected for the same reason - so the shell receives it
like any other dependency instead of reading `Environment.GetCommandLineArgs()` somewhere untestable.
Only the first argument counts, and only when it is not a flag: by that point an argument is a path or
a mistake, and guessing among several is how a stray argument becomes a tab. What it names opens
alongside the restored tabs rather than instead of them - closing someone's tabs because they typed a
path is a second, unasked-for action - and it accepts `fubar.json` as readily as the directory,
because that is what a file manager passes.

**A report file must not contradict the exit code beside it** (Studio). `RunReport.Ok` counts
`Differing` and `Uncomparable`; `JUnitRunReport` mapped only `StepStatus`, so a run that found real
drift between two environments exited 1 while its report said `failures="0"` with every testcase
green - and the build page, which is the thing anyone actually looks at, went green with it. The
`failures` attribute is now counted with the same predicate that decides whether to write a
`<failure>`, so the header and the body cannot drift apart. Teardown is described in a `system-out`
and never judged, matching the rule everywhere else; a match a tolerance forgave says so.

**What may name a file is a fact about the FORMAT, not about the host** (Studio).
`DocumentName.IsValid` and `DocumentName.Sanitize` spell the invalid set out - `/ \ : * ? " < > |`,
the control characters, a trailing dot or space, `.`/`..`, and the reserved device names - rather than
calling `Path.GetInvalidFileNameChars()`, which returns those on Windows and only `/` and NUL on Unix.
A workspace is committed and shared, so a name has to survive every platform it can be checked out on:
a case named `smoke?` on Linux is a repository nobody on Windows can clone, and the machine that
created it is the one machine where nothing looks wrong. This shipped the wrong way round and the
Linux CI runner is what caught it - the suite agreed with itself on Windows because Windows happened
to reject what the code had forgotten. Every place that derives a file name from text somebody else
wrote (both importers, the snapshot store, new workspace files) goes through `Sanitize` for the same
reason; `IsValid` is for names a person types, who can be told.
