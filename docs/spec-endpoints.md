# Spec: endpoints, cases, snapshots, batches

The design and reasoning are in [endpoints-and-oracles.md](endpoints-and-oracles.md). This is the
specification: what it does, how the pieces fit, what is on disk, and how to build it.

Status: **not implemented.** Written to be built from.

---

## 1 · Concepts

| Concept | Is | Is not | Count |
| --- | --- | --- | --- |
| **Workspace** | a folder with `fubar.json`; the root of everything | a project file | 1 |
| **Folder** | a node in the endpoint tree; carries inherited settings | a group in the UI only | 0..n, nested |
| **Endpoint** | one operation: method + URL template + its own overrides | something you can send | 0..n per folder |
| **Case** | one concrete invocation of an endpoint: params, body, overrides | a recorded response | 0..n per endpoint |
| **Snapshot** | a recorded response for (case, environment) | an input | 0..1 per case per environment |
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
          staging.json             one file per environment
          production.json
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
  "comparison": { "ignoredPaths": ["$.meta.requestId"] },
  "snapshot": {
    "normalize": [{ "path": "$.generatedAt", "as": "<timestamp>" }],
    "redact": [{ "path": "$..token", "as": "<redacted>" }]
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

### 3.4 `snapshots/<environment>.json`

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
  "oracle": { "kind": "snapshot" },
  "environments": ["staging"],
  "options": { "stopOnFailure": false, "delayMs": 0, "parallel": false },
  "overlay": {
    "comparison": { "ignoredPaths": ["$.version"] },
    "tolerances": [{ "path": "$..elapsedMs", "numeric": 500 }]
  }
}
```

- `steps` are paths relative to `collections/`, so a batch survives a rename only if the file moves
  with it — same as any other cross-file reference in this repo. A step whose endpoint or case is
  missing is reported as `Errored`, never skipped silently.
- `environments` holds one name for most oracles and two for `environment:X`.
- `overlay` is the occasion's settings (§4.2).

### 3.6 `_folder.json`

Unchanged in shape from today, plus the new sections:

```json
{
  "headers": [{ "key": "X-Tenant", "value": "{{tenant}}", "enabled": true }],
  "authProfileId": "39474e…",
  "comparison": { "matchArraysByPosition": false },
  "snapshot": { "redact": [{ "path": "$..authorization", "as": "<redacted>" }] },
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

### 4.3 Lists: the decision to make

`ComparisonSettings.IgnoredPaths` **replaces** the inherited list today, deliberately: reading one
level then tells you what applies there, and an inherited rule that is wrong for one endpoint can be
dropped. An empty non-null list means "ignore nothing here", not "inherit".

The cost is that adding one rule requires restating the inherited ones, and the UI does that silently
— `IgnorePathAsync` promotes the *resolved* list to the request level, so adding one path to a request
that inherits three copies all four onto it and ends inheritance for that setting. Nothing says so.

Two ways out. **Pick one before building anything else in §11.**

| | Replace (today) | Add / remove |
| --- | --- | --- |
| Shape | `"ignoredPaths": ["a","b"]` | `"ignoredPaths": { "add": ["b"], "remove": ["a"] }` |
| Read one level | tells you what applies | tells you what this level changes |
| Add a rule | must restate inherited | one entry |
| Drop an inherited rule | write the shorter list | one entry |
| Provenance | per list | per entry |
| Migration | none | mechanical: `[…]` ⇒ `{ "add": […] }` is **not** equivalent — a replace-list also removes |

**Recommendation: add/remove**, with the resolved view (which already exists, with `Scope` and
`SourceName`) as the answer to "what applies here". The original objection — that you cannot read one
level and know what applies — is already true of every other setting in this hierarchy; a nullable
`ignoreWhitespace` tells you nothing about the effective value either. What replace buys is not
readability but a single-file answer, and the UI's provenance display is a better one.

If replace is kept instead, the UI must stop auto-promoting: adding a rule to a level that inherits
some must say that it is taking a copy, and offer to write it to the folder instead.

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
snapshot          compare the response against snapshots/<environment>.json
environment:X     send twice (this environment and X), compare the two responses
run:<id>          compare against a stored run under .fubar/runs/<id>
```

| Oracle | Sends | Other side from | Missing other side |
| --- | --- | --- | --- |
| `none` | 1× | — | — |
| `snapshot` | 1× | disk | `NoSnapshot` — reported, not a pass |
| `environment:X` | 2× | the second send | the failing side is `Errored` |
| `run:<id>` | 1× | disk | `Errored` |

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

## 9 · Implementation

### 9.1 Layers

| Layer | Adds |
| --- | --- |
| `Studio.Core` | `Endpoint`, `Case`, `Snapshot`, `Batch`, `Tolerance`, `SnapshotPolicy`; `OracleKind`; extended `ComparisonScope`; resolution policy; ports: `IEndpointStore`, `ISnapshotStore`, `IBatchStore`, `IResponseComparer` |
| `Studio.Application` | `IOracle` + four implementations; `RunPlan` over `(endpoint, case)`; the run pipeline; snapshot record/accept orchestration |
| `Studio.Infrastructure` | file-system adapters for the three stores; `DiffResponseComparer` (§9.2); stable JSON writer |
| `Studio.UI` | endpoint/case tree, case editor, snapshot viewer + accept, batch editor, run windows; CLI selector parsing |

### 9.2 The one architectural move

The comparison engine is reachable only from `Studio.UI` today (`Studio.UI` references
`Fubar.Diff.Application`). Oracles live in `Studio.Application`, so they cannot reach it, and the CLI
would otherwise have to compare in the UI layer.

Introduce a port and an adapter:

```csharp
// Studio.Core - no diff types in the signature
public interface IResponseComparer
{
    Task<ComparisonOutcome> CompareAsync(
        string left, string right, EffectiveComparisonSettings settings, CancellationToken ct);
}

public sealed record ComparisonOutcome(int DifferenceCount, IReadOnlyList<FieldDifference> Differences);
```

`DiffResponseComparer` in `Studio.Infrastructure` implements it over `IFileComparisonService`, which
moves the `AddFubarDiffTextAndJson()` reference from `Studio.UI` to `Studio.Infrastructure` — where
adapters belong. `Fubar.Studio.Architecture.Tests` must be updated to allow that edge **and keep
banning Roslyn**, which is the reference the existing test exists to stop.

This is the change that makes every oracle work identically in the UI and in CI.

### 9.3 Build order

Each step is useful on its own and leaves the app shippable.

1. **List semantics (§4.3).** Decide, then implement in `ComparisonSettingsResolver` with per-entry
   provenance. Fixes the silent-copy behaviour in the shipped comparison window. No format change.
2. **`IResponseComparer` port + adapter (§9.2).** No behaviour change; the existing comparison window
   moves onto it. Architecture test updated.
3. **The oracle seam.** `IOracle` with `none` and `environment:X` — both already exist as behaviour,
   now behind one interface. The run pipeline stops knowing which one it has.
4. **Snapshots.** `ISnapshotStore`, the stable writer, redaction, normalisation, tolerances, the
   `snapshot` oracle, `--update-snapshots`. Still one request per file: a snapshot can key off the
   request's path until step 5 renames it.
5. **Endpoints and cases.** The format change, with a migration that turns each `request.json` into an
   endpoint directory with one `default` case. Everything it feeds already works.
6. **Batches**, then the CLI selector grammar that reaches all of it.

Steps 1–4 need no format change, which is what makes this incremental rather than a rewrite.

### 9.4 Migration

`request.json` → `endpoint.json` + `cases/default.json`, splitting on the boundary between "the
operation" (method, url, headers, auth) and "the invocation" (params, body, assertions, captures).
Run once per workspace on open, announced in the status log the way the format-floor migration already
is, and only after a `.fubar/backup/` copy.

Backwards compatibility: a workspace containing `request.json` files is read as endpoints-with-one-case
without being rewritten, until the user accepts the migration. The runner sees only the migrated model.

### 9.5 What to test

- Resolution: every level, per setting, including a batch overlay, and both list semantics.
- Selector expansion: folder order, endpoint-to-cases, batch order, missing endpoint reported.
- Oracles: each one's missing-other-side path, which is the one that must not silently pass.
- Snapshot writer: byte-stable output for the same input, sorted keys, round-trip numbers.
- Redaction: a token in a response never reaches the written file. This is a security test, not a
  formatting one.
- Tolerances: each kind, and the evaluation order in §6.3.
- Exit codes for each verdict.

---

## 10 · Deliberately not in scope

- Load testing. A batch runs each step once.
- Mocking or recording a server. Snapshots are for comparison, not playback.
- gRPC/GraphQL specifics. The model is protocol-agnostic; only `kind: "http"` is specified here.
- Cross-workspace batches.

---

## 11 · Open questions

1. **§4.3 list semantics.** Blocks step 1. Recommendation: add/remove.
2. **Snapshot per environment, or one shared?** Specified as per-environment. A shared snapshot with
   per-environment tolerances is defensible for teams whose environments hold the same data.
3. **Case-level auth.** Excluded above (auth stops at endpoint). A case that needs a different user is
   a real scenario; the alternative is an environment per user.
4. **Do batches nest?** Specified no. A batch of batches is a scheduler, and that is a different tool.
