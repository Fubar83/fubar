# Spec: endpoints, cases, snapshots, batches

The design and reasoning are in [endpoints-and-oracles.md](endpoints-and-oracles.md). This is the
specification: what it does, how the pieces fit, what is on disk, how it is used, and how to build it.

Status: **steps 1-2 built** (§10.3); the rest written to be built from.

---

## 1 · Concepts

| Concept | Is | Is not | Count |
| --- | --- | --- | --- |
| **Workspace** | a folder with `fubar.json`; the root of everything | a project file | 1 |
| **Folder** | a node in the endpoint tree; carries inherited settings | a group in the UI only | 0..n, nested |
| **Endpoint** | one operation: method + URL template + its own overrides | something you can send | 0..n per folder |
| **Case** | one concrete invocation of an endpoint: params, body, overrides | a recorded response | 0..n per endpoint |
| **Snapshot** | a recorded response for a case, scoped to one environment or shared by all | an input | 0..1 shared + 0..1 per environment, per case |
| **Environment** | a named set of variables + transport + credentials | a deployment | 0..n per workspace |
| **Batch** | a named, ordered selection of cases + oracle + overlay | a folder | 0..n per workspace |
| **Oracle** | what judges a response | a comparison implementation | 1 per run |
| **Run** | one execution of a selection under one oracle | a batch | transient |

Two rules that decide most later questions:

1. **A case is an input; a snapshot is an output.** They are never the same file.
2. **An endpoint cannot be sent.** Sending needs a case. An endpoint with no cases has an implicit
   default case with no parameters, so the simple thing stays simple.

---

## 2 · How a run works

```
selection ──► plan ──► for each step: resolve ──► send ──► judge ──► report
                          │                        │        │
                          │                        │        └─ oracle
                          │                        └─ one or two environments
                          └─ containment chain, then batch overlay
```

**Selection** — a folder, an endpoint, a case, or a batch. Expands to an ordered list of
`(endpoint, case)` steps. A folder expands depth-first in tree order (as `RunPlan` already does);
an endpoint expands to its cases; a batch expands to its listed steps in its own order.

**Resolve** — settings for the step (§4).

**Send** — once for most oracles, twice for `environment:X` (§5).

**Judge** — the oracle produces a verdict per step (§5, §7).

**Report** — per step and per run, identical shape for UI and CLI (§7).

The pipeline is the same for one case and for a batch of two hundred. There is no separate "run a
single request" path, which is what stops the two from drifting.

---

## 3 · On disk

### 3.1 Layout

```
workspace/
  fubar.json                       manifest: id, name, variables
  auth-profiles.json
  environments/
    staging.json
    production.json
  collections/
    _folder.json                   root-level inherited settings (optional)
    orders/
      _folder.json                 (optional)
      get-order/
        endpoint.json
        cases/
          default.json
          not-found.json
        snapshots/
          _shared.json             used by every environment that has no file of its own
          staging.json             this environment only, and it wins for it
  batches/
    smoke.json
    nightly-drift.json
  .fubar/                          never committed (already git-ignored)
    history/
    runs/
```

Rules:

- A directory containing `endpoint.json` **is** an endpoint. Its subdirectories are not folders.
- A directory containing neither `endpoint.json` nor `fubar.json` is a folder.
- `cases/` and `snapshots/` are reserved names inside an endpoint directory.
- One case per file, one snapshot per file. Both are for git: a case gets its own history, and a
  snapshot diff is reviewable on its own.

### 3.2 `endpoint.json`

```json
{
  "id": "6f1c…",
  "name": "Get order",
  "kind": "http",
  "method": "GET",
  "url": "{{baseUrl}}/orders/{orderId}",
  "headers": [{ "key": "Accept", "value": "application/json", "enabled": true }],
  "auth": { "type": "inherit" },
  "suppressedInheritedHeaderKeys": ["X-Debug"],
  "defaultCase": "default",
  "comparison": { "ignoredPaths": { "add": ["$.meta.requestId"] } },
  "snapshot": {
    "normalize": { "add": [{ "path": "$.generatedAt", "as": "<timestamp>" }] },
    "redact":    { "add": [{ "path": "$..token", "as": "<redacted>" }] }
  },
  "tolerances": [{ "path": "$.total", "numeric": 0.01 }]
}
```

`url` carries `{param}` placeholders filled by the case, and `{{variable}}` placeholders filled by the
environment. Two syntaxes on purpose: one is the endpoint's shape, the other is the environment's
values, and conflating them makes it impossible to say which a missing value came from.

### 3.3 `cases/<name>.json`

```json
{
  "id": "b20a…",
  "name": "not-found",
  "description": "An id that does not exist",
  "pathParams": { "orderId": "A-0000" },
  "queryParams": [{ "key": "include", "value": "lines", "enabled": true }],
  "headers": [],
  "body": { "type": "none" },
  "assertions": [{ "kind": "status", "op": "equals", "value": "404" }],
  "captures": [],
  "comparison": null,
  "tolerances": []
}
```

A case overrides only what it needs. `null`/absent means inherit, exactly as today.

### 3.4 `snapshots/<environment>.json` and `snapshots/_shared.json`

A snapshot is scoped when it is recorded: to the environment it came from, or shared by all of them.
Both may exist for the same case, and the per-environment one wins for its environment (§5).

```json
{
  "case": "default",
  "environment": "staging",
  "recordedAt": "2026-09-08T19:04:11Z",
  "recordedBy": "fubar 0.2.0",
  "status": 200,
  "headers": { "content-type": "application/json" },
  "bodyFormat": "json",
  "body": { "…": "normalised, redacted, key-sorted" }
}
```

- `environment` is the scope, and is **null in `_shared.json`**. The file says what it is, so nothing
  has to infer scope from a file name it might have been given by a merge.
- The `_` prefix marks a reserved name, as `_folder.json` already does in this format. **No environment
  may be named starting with `_`**, which is what stops an environment called `shared` colliding with
  the shared file. Workspace validation refuses one.
- `body` is stored **parsed** when the response is JSON, so the file is a readable diff rather than an
  escaped string. Non-JSON bodies are stored as text under `bodyText`, or as a `sha256` plus a
  sidecar file when binary.
- Only headers named by `snapshot.headers` policy are stored. Storing all of them makes every snapshot
  churn on `Date` and `Set-Cookie`.
- The file is written with sorted keys and stable number formatting (§6.2).

### 3.5 `batches/<name>.json`

```json
{
  "id": "9d0e…",
  "name": "smoke",
  "steps": [
    { "endpoint": "auth/login", "case": "default" },
    { "endpoint": "orders/get-order", "case": "default" },
    { "endpoint": "orders/get-order", "case": "not-found" }
  ],
  "teardown": [
    { "endpoint": "orders/delete-order", "case": "created" }
  ],
  "oracle": { "kind": "snapshot" },
  "environments": ["staging"],
  "options": { "stopOnFailure": false, "delayMs": 0, "parallel": false },
  "overlay": {
    "comparison": { "ignoredPaths": { "add": ["$.version"] } },
    "tolerances": [{ "path": "$..elapsedMs", "numeric": 500 }]
  }
}
```

- `steps` are paths relative to `collections/`, so a batch survives a rename only if the file moves
  with it — same as any other cross-file reference in this repo. A step whose endpoint or case is
  missing is reported as `Errored`, never skipped silently.
- `environments` holds one name for most oracles and two for `environment:X`.
- `overlay` is the occasion's settings (§4.2).
- `teardown` is **cleanup, not test.** It runs after `steps` whatever happened to them, and never
  changes the verdict.

  It exists because a chain that creates something has to remove it again, and `stopOnFailure` — the
  right setting for a chain — guarantees a failure in the middle skips the delete. Every red run then
  leaves a row behind, which over a week of failing CI is a lot of rows nobody deletes.

  Three rules follow from "not test", and each one is there to stop cleanup crying wolf:

  - **Its assertions are dropped and its oracle is skipped.** A batch that reuses
    `delete-order#created` as teardown reuses a case expecting `204`, and on a run where the delete
    already happened as a step the cleanup finds `404`. That is the happy path.
  - **Only "could not be sent at all" counts** — reported as `N cleanup steps did not finish`, beside
    the verdict and never inside it. That is the actual leak signal.
  - **It does not run after a cancellation.** Sending four more requests after Ctrl-C is the opposite
    of stopping. That leaks, and it is the lesser surprise of the two.

  If deleting is one of the things you are *testing*, it belongs in `steps`, where it is judged like
  anything else. Putting it in both is reasonable and is what the worked example does: the step proves
  delete works, and the teardown cleans up the runs where the step was never reached.

### 3.6 `_folder.json`

Unchanged in shape from today, plus the new sections:

```json
{
  "headers": [{ "key": "X-Tenant", "value": "{{tenant}}", "enabled": true }],
  "authProfileId": "39474e…",
  "comparison": { "matchArraysByPosition": false },
  "snapshot": { "redact": { "add": [{ "path": "$..authorization", "as": "<redacted>" }] } },
  "tolerances": []
}
```

---

## 4 · Settings resolution

### 4.1 Containment chain

Root-most first, closest wins, **per setting**:

```
built-in default → global (app settings) → workspace (fubar.json) →
folder(s) root-to-leaf → endpoint → case
```

This is exactly `ComparisonSettingsResolver.Resolve`'s existing contract, with two levels added
(workspace, case) and the request level renamed to endpoint. Every member stays nullable; `null` means
inherit; the closest non-null wins and every other setting keeps inheriting independently.

`ComparisonScope` gains `Workspace`, `Endpoint`, `Case`, `Batch`.

### 4.2 The batch overlay

A batch is **not** a sixth containment level. It cuts across the tree, so it is applied as a final
overlay after the chain resolves:

```
effective = Overlay(Resolve(containment chain), batch.overlay)
```

Provenance for anything the overlay set reads `Batch: smoke`. Nothing else changes: a batch cannot
introduce a setting the chain does not know about.

### 4.3 Lists: add and remove — **decided**

Every list setting (`ignoredPaths`, `snapshot.normalize`, `snapshot.redact`) contributes **additions
and removals** at each level, replacing today's wholesale replacement:

```json
"ignoredPaths": { "add": ["$.orders[*].etag"], "remove": ["$.meta.requestId"] }
```

Resolution folds the chain in order: start empty, apply each level's `remove` then its `add`. Removing
something never added is not an error — a level is allowed to say "not here" about a rule an ancestor
might grow later, and failing the run over it would make the rule file order-dependent.

Every resolved entry carries the level that added it, so `Resolved<T>` moves from per-list to
per-entry provenance and a chip can read *inherited from folder: orders*.

What this gives up: reading one file no longer tells you what applies there, only what that level
changes. That was the reasoning behind wholesale replacement and it is a real loss — mitigated by the
resolved view, which already exists with `Scope` and `SourceName`, and which is a better answer to
"what applies here" than one file could ever be. Every other setting in this hierarchy already works
this way: a nullable `ignoreWhitespace` tells you nothing about the effective value either.

**Reading the old shape.** A bare array (`"ignoredPaths": ["a","b"]`) is read as
`{ "add": ["a","b"] }` — which is *not* equivalent, since the old form also removed everything
inherited. That difference only bites a workspace that relied on a shorter child list to drop an
inherited rule, and per §10.4 no existing workspace is converted, so it can only appear in a
hand-edited file. It is read, warned about in the status log, and rewritten on next save.

### 4.4 What inherits

| Setting | Levels | Merge |
| --- | --- | --- |
| Headers | workspace → folder → endpoint → case | union by key, closest wins; `suppressedInheritedHeaderKeys` removes |
| Auth | workspace → folder → endpoint | closest non-`inherit` wins |
| Variables | workspace → environment → folder → endpoint → case | closest wins per key |
| Timeout, retry | all levels | closest wins |
| Comparison | all levels + batch overlay | per setting; lists per §4.3 |
| Snapshot policy | all levels + batch overlay | `normalize`/`redact` lists per §4.3 |
| Tolerances | all levels + batch overlay | per path; closest wins for the same path |

---

## 5 · Oracles

```
none              no comparison; assertions decide the verdict
snapshot          compare against snapshots/<environment>.json, else snapshots/_shared.json
environment:X     send twice (this environment and X), compare the two responses
run:<id>          compare against a stored run under .fubar/runs/<id>
```

| Oracle | Sends | Other side from | Missing other side |
| --- | --- | --- | --- |
| `none` | 1× | — | — |
| `snapshot` | 1× | disk | `NoSnapshot` — reported, not a pass |
| `environment:X` | 2× | the second send | the failing side is `Errored` |
| `run:<id>` | 1× | disk | `Errored` |

**Which snapshot** the `snapshot` oracle uses, in order: `snapshots/<environment>.json`, then
`snapshots/_shared.json`, then `NoSnapshot`. Specific beats general, as everywhere else in this
format. Because that choice is invisible in the result otherwise, **every step reports which snapshot
it compared against** — the row, the CLI report and the JUnit output all name it. A run that quietly
switched from the shared snapshot to a per-environment one someone recorded last week is a run whose
green means something different from yesterday's.

Rules that hold for all of them:

- A missing other side is **never** a pass. A first run against no snapshot reports `NoSnapshot` and
  exits non-zero unless `--update-snapshots` was given, because "nothing to compare, therefore fine"
  is how a suite silently stops testing.
- Assertions run regardless of oracle, and a failed assertion fails the step whatever the comparison
  said.
- An HTTP status never fails a step on its own — unchanged from `RunReport`'s existing rule. The
  oracle's verdict and the assertions decide.

---

## 6 · Snapshots

### 6.1 Recording

```
response ──► parse ──► redact ──► normalise ──► sort keys ──► write
```

**Redact before write, always.** Snapshots are committed; a token that reaches the file has leaked
whatever the tracked history keeps. This is the same rule the variable-write policy already enforces
for captures.

**Normalise, do not ignore, where you can.** Normalising replaces a volatile value with a stable
placeholder at record time (`"generatedAt": "<timestamp>"`), so the stored file is stable and every
future diff means something. Ignoring leaves the real value in the file and hides the difference at
compare time, which makes the snapshot churn on every re-record.

**Scope is chosen when saving**, not configured in advance:

| | Per environment (`staging.json`) | Shared (`_shared.json`) |
| --- | --- | --- |
| Used by | that environment only | every environment with no file of its own |
| Right when | environments hold different data | environments hold the same data, or the differing parts are normalised away |
| Failure mode | redundant files, more diff noise | a data difference reads as a regression |

**Default: per environment.** Its failure mode is waste; shared's is a false alarm, and a regression
tool that cries wolf stops being run. Shared is offered beside it with one line saying what it means,
not buried.

Two moments where the tool should say something rather than let the choice rot:

- Recording for environment B when A's snapshot exists and the new body is **identical** to it: offer
  to save one shared snapshot instead. This is the moment the answer is knowable for free, and it is
  how a workspace ends up with shared snapshots without anyone having to plan for them.
- Recording per-environment for a case that already has a shared snapshot: say that this environment
  will stop using the shared one. That is the whole effect of the click and it is otherwise invisible.

**Changing scope later.** *Share this snapshot* promotes a per-environment file to `_shared.json`,
refusing when other per-environment snapshots exist that differ from it — promoting would silently
change what they compare against. *Split by environment* writes the current environment's file from
the shared one and leaves `_shared.json` for everyone else, which is the least surprising demotion:
nothing else changes behaviour.

A shared snapshot whose environments differ in a handful of fields is exactly what tolerances are for
(§6.3) — `$.environment matches ^(staging|production)$` keeps one file and keeps checking the field.

### 6.2 Stable serialisation

Non-negotiable, or every re-record is an unreviewable diff:

- object keys sorted ordinal
- two-space indent, `\n` endings
- numbers written round-trip (`R`), no exponent for integral values
- strings escaped minimally
- arrays keep source order unless the endpoint declares an identity key, in which case sorted by it

### 6.3 Tolerances

A tolerance keeps checking a field while allowing the part that legitimately moves. It is the
difference between a suite that catches regressions and one that ignores half the payload.

```json
{ "path": "$.total",        "numeric": 0.01 }
{ "path": "$.generatedAt",  "withinSeconds": 300 }
{ "path": "$.requestId",    "matches": "^[0-9a-f]{32}$" }
{ "path": "$.orders",       "lengthWithinPercent": 10 }
{ "path": "$.status",       "oneOf": ["paid", "shipped"] }
```

Evaluation order per field: `redact` → `normalise` → `tolerance` → `ignore` → report. The first rule
that settles the field wins, so an ignored field never reaches a tolerance and a normalised one is
compared as its placeholder.

### 6.4 Accepting

- CLI: `--update-snapshots` records over the existing file for every step in the selection. Refuses
  to run when the working tree has uncommitted snapshot changes unless `--force`, so an accept never
  silently buries an earlier one.
- UI: per endpoint and per field, from the same diff that reported the difference. Accepting one field
  writes that value into the snapshot and leaves the other differences standing.
- Never automatic. A snapshot that updates itself tests nothing.

---

## 7 · Verdicts, reports, exit codes

Per step:

```
Passed        sent, assertions passed, oracle found no differences
Differs       sent, assertions passed, oracle found differences
Failed        sent, an assertion failed
Errored       not sent, or the oracle could not obtain the other side
NoSnapshot    sent, no snapshot to compare against
Skipped       never reached
```

`StepReport` gains `OracleVerdict`, `DifferenceCount`, and the two bodies it compared (bounded as
`ResponseBody` already is). `RunReport.Ok` becomes: at least one step, and none `Differs`, `Failed`,
`Errored` or `NoSnapshot`, not cancelled, none skipped.

**Exit codes** (unchanged meanings): `0` all matched and passed, `1` differences or assertion
failures, `2` could not run.

**Report identity is stable**: a JUnit test name is `folder/endpoint/case`, never an index, or CI
loses the ability to say which one started failing.

---

## 8 · CLI

```
fubar run <selector> [--env NAME] [--oracle none|snapshot|env:NAME|run:ID]
                     [--case NAME] [--update-snapshots] [--force]
                     [--report PATH] [--report-format junit|json]
                     [--stop-on-failure] [--delay MS] [--parallel]
```

`<selector>` is one of:

```
                          the whole workspace
orders                    a folder, depth-first
orders/get-order          an endpoint, all its cases
orders/get-order#default  one case
@smoke                    a batch (its own oracle/environments unless overridden)
```

Examples:

```bash
fubar run --env staging --oracle snapshot                 # regression, whole workspace
fubar run orders --env staging --oracle env:production    # compare two environments
fubar run @smoke --report junit=out.xml                   # a batch, for CI
fubar run orders/get-order#not-found --env staging --oracle snapshot --update-snapshots
```

Flags on the command line override the batch's own settings; the batch's overlay still applies.
`--oracle env:NAME` and `--update-snapshots` together is an error: there is no snapshot in that run to
update.

The existing `--run` stays as an alias for `run` with `--oracle none` for one release, then goes.

---

## 9 · UI workflows

What a person actually does. Every screen below resolves the same hierarchy the runner does (§4) and
shows where each value came from — a settings tree nobody can see the provenance of is a settings tree
nobody trusts.

### 9.1 The tree

```
REQUESTS                              ⌄  +
  ├─ auth
  │   └─ ⊟ POST  Login              ●
  └─ orders
      ├─ ⊞ GET   Get order       2 ⚑
      └─   POST  Create order      ○
```

- **Folders and endpoints only.** Cases are not rows by default: an endpoint with 0 or 1 case is a
  leaf, and the common endpoint has exactly one. An endpoint with 2+ cases gets a `+`/`−` box and
  shows its cases as children. Simple stays simple; the tree does not grow a level to say "there is
  nothing more here".
- **Badges**, right-aligned, in this order: method, case count when >1, auth override, snapshot state,
  unsaved dot.
- **Snapshot state** is one glyph with three meanings: `●` recorded and current, `○` no snapshot,
  `⚑` stale — recorded before the endpoint or case was last edited. Stale matters: a green regression
  run against a snapshot recorded from a since-changed request is a lie, and this is the only place a
  person can notice it before running.
- Right-click acts on the row under the pointer, as it already does. New entries: **Add case**,
  **Record snapshot**, **Run**, **Compare across environments**, **Add to batch…**.

### 9.2 The endpoint editor

The critical split. Half of these fields belong to the operation and half to the invocation, and a
person who sets a header on a case thinking it applies to the endpoint has silently broken the other
cases. The editor says which level it is editing, everywhere.

```
┌───────────────────────────────────────────────────────────────────────┐
│ GET ▾  {{baseUrl}}/orders/{orderId}                    [Send] [Save]  │
│ Case:  ( default ▾ )  + Add case                                      │
├───────────────────────────────────────────────────────────────────────┤
│ ENDPOINT  Headers · Auth · Rules      CASE  Params · Body · Assertions │
└───────────────────────────────────────────────────────────────────────┘
```

- One tab strip, two labelled groups, a divider between them. Not two strips: the user is editing one
  thing and the level is an attribute of the tab, not a mode to be in.
- The case selector sits under the URL because changing it changes what Send sends, and that has to be
  visible from where the Send button is.
- Every inherited value shows its source inline (`Accept: application/json — folder: orders`) and is
  editable in place; editing one writes an override at the endpoint or case level and the source label
  changes to say so. This is the existing Headers "Source" column, extended to auth and rules.
- **Send sends the selected case.** An endpoint with no cases sends its implicit default (§1).

Single canvas is kept. Cases are an inner selector rather than more open editors, which strengthens
the decision in `decisions.md §A` rather than reopening it: switching case is not switching document.

### 9.3 Folder and workspace settings

`_folder.json` has no editor today; it is written by the diff window's "save to folder" and otherwise
edited by hand. That does not survive folders carrying auth, headers, variables, rules, snapshot
policy and tolerances.

Selecting a **folder** in the tree opens a folder editor on the same canvas, with the same tab
vocabulary: `Headers · Auth · Variables · Rules`. Selecting the **workspace** row opens the same
editor for `fubar.json`. Both show what they inherit and what they override, exactly as an endpoint
does — one editor shape for every level of the chain, so learning it once is enough.

### 9.4 Setting up rules

Three entry points, in order of how often they are used:

1. **From a difference.** In any comparison — snapshot, environment, drift — the current difference
   offers *Ignore this field*, *Add tolerance…*, *Redact in snapshots*, *Match this array by…*. This
   is where rules are actually written, because it is the moment you can see what the rule is for.
   Already built for ignore; the other three are the same affordance.
2. **The Rules tab** on an endpoint, folder or workspace: every rule that applies here, each with its
   source, grouped `Comparison · Snapshot policy · Tolerances`. Rules inherited from above are shown
   greyed with their origin; local ones are editable.
3. **The batch editor** (§9.7) for occasion-only rules.

**Choosing the level is the whole skill**, so the save control never guesses. A rule written from a
difference offers *this endpoint* (default), *folder: orders*, or *workspace* — the same SplitButton
the comparison window already has, with the folder named rather than called "folder".

**Removing an inherited rule** must be one gesture with a visible consequence. The chip carries its
origin; the `✕` on an inherited chip says *"stop ignoring `$.requestId` here"* and writes a removal at
the current level (§4.3). It never edits the folder — a click in an endpoint's window must not change
what forty other endpoints do.

### 9.5 Snapshots

**Recording.** *Record snapshot* on an endpoint, folder, or selection, for the active environment.
Runs the cases, shows what will be written — redacted and normalised, exactly as it will land on disk
— and asks. The preview is not decoration: it is the only chance to notice a token the redaction
rules missed before it is committed.

The same dialog carries the scope (§6.1), on the write button rather than as a separate step:

```
Save snapshot ▾     ( • Staging only        writes snapshots/staging.json
                      ○ All environments    writes snapshots/_shared.json )
```

Recording over an existing snapshot keeps its scope without asking; the dropdown is how you change it,
and changing it says what will happen to the other environments. When the body is identical to another
environment's snapshot, the dialog leads with *All environments* instead and says why.

A row being recorded shows which file it will write, because a folder-wide record can be writing both
kinds at once.

**Reviewing a failure.** A regression run's row opens the same comparison pane the environment window
uses, left = snapshot, right = the response. The difference between the two features is one label.

**Accepting.** From that pane:

- *Accept all* — rewrite the snapshot for this case.
- *Accept this field* — write one value into the snapshot and leave the other differences standing.
  This is what makes a 40-difference wall workable: accept the three that are intended, and what
  remains is the regression.
- *Add tolerance instead* — offered beside accept whenever the difference is numeric, a timestamp, or
  a string matching a well-known id shape. Nudging here rather than in docs is what keeps a suite from
  degrading into ignores.

Accepting is never automatic and never bulk across endpoints without a confirmation naming the count.

**Accepting into a shared snapshot changes every environment**, so the pane says which file it is
about to write and how many environments use it, and offers *Split by environment* (§6.1) beside
accept — because "this is right for staging but not for production" is precisely the discovery that
accepting a shared snapshot is about to bury.

### 9.6 Running

One dialog, reached from the tree, the editor, or a batch. It already exists as the Run window and the
environment-comparison window; they become one with an oracle picker:

```
Run  orders/get-order                       ( Staging ▾ )
Compare against:  ( ● Nothing  ○ Snapshot  ○ Environment ( Production ▾ )  ○ Previous run ▾ )
[ ] Stop at first failure    [ ] Record snapshots instead of comparing
```

- Picking *Environment* reveals the second environment picker; picking *Snapshot* reveals nothing
  extra, because the environment is already chosen above.
- The results list and the diff pane are the ones already shipped. The verdict column's vocabulary
  comes from §7, so the same words appear in the window, the CLI and the JUnit report.
- Running one case from the editor uses the same dialog with the selection pre-filled — there is no
  second, simpler run path to drift.

### 9.7 Batches

**Created from a run, not from an empty form.** After a run, *Save as batch…* takes the steps that
just ran, the oracle and the environments, and writes `batches/<name>.json`. That is the honest
creation path: nobody knows what belongs in a smoke test until they have run something.

The batch editor then allows reordering, adding and removing steps, changing the oracle, and editing
the overlay rules — with the overlay marked as *applies to this batch only*, so it cannot be mistaken
for a permanent rule.

*Add to batch…* on a tree row appends to an existing batch.

### 9.8 Import

OpenAPI import produces **endpoints**, and each `example` in the document becomes a **case**. Where
today it produces one request per operation and drops the examples, the model finally has somewhere to
put them. The existing import preview (which already shows what will be written before it is written)
gains a case count per endpoint.

Postman import maps a request to an endpoint with one case, and a folder to a folder.

### 9.9 The first five minutes

The path that has to work without documentation, because it is the one that decides adoption:

1. Import an OpenAPI document, or open a folder of existing requests (migrated per §10.4).
2. Pick an environment. Send one endpoint. It works or the error says which variable is missing.
3. *Record snapshots* on a folder. The preview shows what will be committed.
4. Change something in the service. Run the folder with *Compare against: Snapshot*.
5. The list shows two rows differing. Open one, accept the intended change, add a tolerance to the
   timestamp.
6. *Save as batch…* → `smoke`. Copy the CLI line the dialog offers into CI.

Step 6 matters as much as the rest: the run dialog shows the exact `fubar run` command equivalent to
what is on screen, so moving from the UI to CI is copying a line rather than reading a manual.

### 9.10 States that must be visible

Each of these is silent failure if it is not shown:

| State | Where | Says |
| --- | --- | --- |
| No snapshot yet | tree badge, run verdict | `○` / `NoSnapshot` — never a pass |
| Stale snapshot | tree badge, run verdict | `⚑` recorded before this endpoint changed |
| Which snapshot was used | run verdict, CLI, JUnit | `staging.json` or `_shared.json`, per step (§5) |
| Shared snapshot in play | tree badge, accept dialog | that accepting changes every environment using it |
| Inherited value overridden | any field | source label changes from folder name to *this endpoint* |
| Missing variable | send, and before a run | which variable, which environment |
| Batch step missing | batch editor, run | which endpoint or case, reported as `Errored` |

---

## 10 · Implementation

### 10.1 Layers

| Layer | Adds |
| --- | --- |
| `Studio.Core` | `Endpoint`, `Case`, `Snapshot`, `Batch`, `Tolerance`, `SnapshotPolicy`; `OracleKind`; extended `ComparisonScope`; resolution policy; ports: `IEndpointStore`, `ISnapshotStore`, `IBatchStore`, `IResponseComparer` |
| `Studio.Application` | `IOracle` + four implementations; `RunPlan` over `(endpoint, case)`; the run pipeline; snapshot record/accept orchestration |
| `Studio.Infrastructure` | file-system adapters for the three stores; `DiffResponseComparer` (§10.2); stable JSON writer |
| `Studio.UI` | the workflows in §9: endpoint/case tree, endpoint editor with its level split, folder and workspace editors, snapshot record and accept, batch editor, one run dialog with an oracle picker; CLI selector parsing |

### 10.2 The one architectural move

The comparison engine is reachable only from `Studio.UI` today (`Studio.UI` references
`Fubar.Diff.Application`). Oracles live in `Studio.Application`, so they cannot reach it, and the CLI
would otherwise have to compare in the UI layer.

Introduce a port and an adapter:

```csharp
// Studio.Core - no diff types in the signature
public interface IResponseComparer
{
    Task<ComparisonOutcome> CompareAsync(
        string left, string right, ResolvedComparisonSettings settings, CancellationToken ct);
}

public sealed record ComparisonOutcome(
    int DifferenceCount, bool IsSemantic, IReadOnlyList<ResponseDifference> Differences);
```

**Built, with one deviation from what this section first said.** The adapter, `DiffResponseComparer`,
lives in `Studio.UI/Services` rather than `Studio.Infrastructure`.

It needs `ComparisonSettingsMapper`, and so does the request editor's pane, which renders through
`IFileComparisonService` directly. Moving the mapper to Infrastructure would either duplicate it — and
let the pane and the verdict drift apart on exactly the settings they exist to share — or make a view
model reference Infrastructure, which CLAUDE.md forbids. Neither is worth it, because nothing is
actually gained: `Fubar.Studio.UI` is the executable, so `--run` reaches the adapter through the same
composition root a window does.

The part that mattered is the **port**, and that is in Core as specified. The runner, every oracle and
the CLI depend on the interface and stay free of the engine. `AddFubarDiffTextAndJson()` stays in
`Composition.cs` and the architecture test is unchanged — still banning Roslyn, which is what it exists
for.

This is the change that makes every oracle work identically in the UI and in CI.

### 10.3 Build order

Each step is useful on its own and leaves the app shippable.

1. ~~**List semantics (§4.3).**~~ **Done.** Add/remove in `ComparisonSettingsResolver`, with per-entry provenance,
   and the old bare-array shape read as `add`. Ends the silent copying the shipped comparison window
   does. No format change; applies to existing workspaces as well as new ones.
2. ~~**`IResponseComparer` port + adapter (§10.2).**~~ **Done.** The comparison window judges through it,
   so the row and the pane share one definition of what counts. Adapter in the UI project, not
   Infrastructure - see §10.2.
3. ~~**The oracle seam.**~~ **Done.** `IOracle` with `NoOracle`, `SnapshotOracle` and
   `EnvironmentOracle`; the run pipeline no longer knows which one it has. The environment oracle
   sends the other side through the ORDINARY runner, one step at a time, rather than rebuilding how a
   step is sent - any difference in how the two sides were sent would be reported as a difference
   between the environments.
4. ~~**Snapshots.**~~ **Done.** `ISnapshotStore` (both scopes and the resolution order in §5), the
   stable writer, redaction, normalisation, tolerances (§6.3, all five kinds), the `snapshot` oracle
   and `--update-snapshots`.

   One thing this section did not say, and which makes or breaks the feature: **the same redactions
   and normalisations have to run on the LIVE side too**. A snapshot stores
   `"generatedAt": "<timestamp>"` while the response carries the real value, so without it every
   normalised field differs on every run and the rule written to stop the churn causes it.
   `SnapshotRecorder.ForComparison` is idempotent, so both sides go through one code path.
5. ~~**Endpoints and cases.**~~ **Done.** The format change in new workspaces only (§10.4), behind
   `format` in `fubar.json`, with *Convert to endpoints…* shipping alongside it.

   One deviation: **the tree reads the directory rather than the field.** The format field decides
   what gets created and which editor opens, but the tree has to describe what is actually there - a
   half-converted workspace whose tree showed a folder of stray json files would be a tree nobody
   could act on. It costs a requests-format workspace nothing, since it has no `endpoint.json`
   anywhere.

   A second: **snapshots are keyed per case** (`snapshots/<case>/<environment>.json`), which §3.4 left
   open. Two cases of one endpoint answer differently by design - that is what makes them two cases -
   so one file between them would have each overwrite the other's recording.
6. ~~**Batches**, then the CLI selector grammar.~~ **Done.** `batches/<name>.json`, `RunSelector` and
   `TreeLookup`, and `fubar run <selector>` with `--run` kept as the older spelling. A batch's steps
   run in the batch's own order, and a step naming something that is no longer there ERRORS rather
   than being skipped.

Steps 1-4 need no format change and land in every workspace, old or new. That is what makes this
incremental rather than a rewrite, and it means the two halves of §10.4 differ only from step 5 on.

### 10.3.1 What is NOT built

Named so it is a decision rather than an omission:

- **`run:<id>` as an oracle** (§5). `.fubar/runs/` is not written, so there is nothing to compare
  against. The seam takes it whenever it is wanted - it is one more `IOracle`.
- **Accepting one field at a time from the diff** (§6.4). Recording is all-or-nothing per step, from
  the run window's *Record snapshots* or from `--update-snapshots`. Per-field accept needs the diff
  and the snapshot writer on the same screen, which the comparison window is not yet.
- **`--force` and the uncommitted-snapshot check** (§6.4). Recording does not ask git anything.
- **A batch editor.** Batches are listed and run from the left pane; the file is edited by hand.
- **Case-level auth and snapshot policy.** A case carries comparison rules and tolerances; auth stops
  at the endpoint (§4.4) and so does the snapshot policy, which §3.3's file shape already implied.
- **`ComparisonScope.Workspace`.** `fubar.json` carries no comparison section, so the value would be
  one nothing could ever produce.

### 10.4 Migration — **new workspaces only**

**Decided.** Endpoints, cases, snapshots and batches exist only in workspaces whose `fubar.json`
declares the new format. An existing workspace keeps today's request files and today's runner, and is
never converted on open.

This is the lowest-risk option and it has one cost, which is worth stating plainly rather than
discovering: **the product is in two halves, and nothing closes the split by itself.** Old workspaces
get no snapshots, no cases and no batches, and every feature after this point either has to be built
twice or has to be unavailable in half the workspaces. Two consequences to hold the line on:

- **`format` in `fubar.json` decides**, not file-sniffing. One field, read once at open, so no code
  anywhere has to guess which half it is in from what it finds on disk.
- **An explicit conversion exists** — *Convert to endpoints…* on the workspace, doing exactly what an
  automatic migration would have done (`request.json` → `endpoint.json` + `cases/default.json`,
  splitting on the boundary between the operation and the invocation) after a `.fubar/backup/` copy
  and a preview. The split then closes by choice rather than never. Without this the decision is not
  "low risk", it is permanent.

The old runner keeps working unchanged. It is not extended: a feature that lands in the new model does
not get back-ported, or there are two implementations of everything and this decision has bought
nothing.

### 10.5 What to test

- Resolution: every level, per setting, including a batch overlay, and both list semantics.
- Selector expansion: folder order, endpoint-to-cases, batch order, missing endpoint reported.
- Oracles: each one's missing-other-side path, which is the one that must not silently pass.
- Snapshot writer: byte-stable output for the same input, sorted keys, round-trip numbers.
- Redaction: a token in a response never reaches the written file. This is a security test, not a
  formatting one.
- Snapshot scope: per-environment beats shared for its environment; shared serves the rest; the step
  reports which file it used; an environment named `_anything` is refused.
- Tolerances: each kind, and the evaluation order in §6.3.
- Exit codes for each verdict.

---

## 11 · Deliberately not in scope

- Load testing. A batch runs each step once.
- Mocking or recording a server. Snapshots are for comparison, not playback.
- gRPC/GraphQL specifics. The model is protocol-agnostic; only `kind: "http"` is specified here.
- Cross-workspace batches.

---

## 12 · Open questions

Settled: list semantics (§4.3, add/remove), the word "case" (§1), migration (§10.4, new workspaces
only), tolerances in the first snapshot release (§6.3), and snapshot scope (§6.1, either, chosen when
saving, per-environment by default).

1. **Case-level auth.** Excluded above (auth stops at endpoint). A case that needs a different user is
   a real scenario; the alternative is an environment per user.
2. **Do batches nest?** Specified no. A batch of batches is a scheduler, and that is a different tool.
