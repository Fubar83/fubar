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


