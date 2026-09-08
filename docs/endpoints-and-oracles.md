# Endpoints, cases, snapshots, batches

A design for the model API Studio should grow into. Nothing here is built yet; this is the shape to
build toward and, more importantly, the reasoning that decides the awkward cases later.

The requirement it answers, in one sentence: **run the same endpoints against a snapshot, against
another environment, or against nothing, from the UI or from CI, with settings that inherit down a
folder tree and can be overridden for one run.**

---

## 1 · Three things, currently one

Today a request file is the operation, the invocation, and the unit of running, all at once. Every
awkward question below comes from that. Separate them and the rest of the design mostly falls out:

| | What it answers | Today |
| --- | --- | --- |
| **Subject** | What do we send? | the request file |
| **Oracle** | What do we compare the answer against? | nothing, or an ad-hoc pick |
| **Occasion** | What are we running, and under what rules this time? | a right-click |

The **oracle** is the load-bearing idea. "Regression against a snapshot" and "staging versus
production" look like two features and are one: a run produces a response, and something judges it.
Once that is one concept, the CLI, the UI, the reports and the settings each have one shape instead
of two that drift.

---

## 2 · The model

**Endpoint** — an operation. Method, URL template, and whatever it sets or overrides of the inherited
auth, headers and variables. `GET /orders/{id}`. Lives in the folder tree. Sends nothing by itself.

**Case** — a concrete invocation of an endpoint: path and query values, a body, per-case header or
variable overrides. 0..n per endpoint, one marked default. "The happy path", "a 404", "a tenant with
2,000 orders". These are what OpenAPI examples become on import, and what today forces a second
request file that no longer looks like the same operation.

**Snapshot** — a recorded response for a case, keyed by environment. The golden.

**Batch** — a named, ordered selection of cases, with an oracle, an environment (or a pair), and a
settings overlay. `smoke`, `nightly-drift`, `pre-release`. Committed, named, and runnable from CI.

A single request today becomes an endpoint with one case, which is what the migration writes.

### Why a case is an input, not a recorded output

A case is what we *send*. A snapshot is what came *back*. Keeping them separate is what allows one
case to have several snapshots (one per environment), and allows an endpoint's snapshots to be
re-recorded without touching the inputs — which is the whole regression workflow.

---

## 3 · On disk

Directory per endpoint, one file per case. Git is the reason: a case is then its own file with its
own history and its own merge conflicts, rather than a hunk inside a growing array.

```
collections/
  orders/
    _folder.json                 auth, headers, variables, rules for everything below
    get-order/
      endpoint.json              method, url template, own overrides
      cases/
        default.json
        not-found.json
      snapshots/
        staging.json
        production.json
batches/
  smoke.json
  nightly-drift.json
```

Snapshots are one file per environment (`snapshots/staging.json`), beside the endpoint, not in a parallel tree: reviewing a change to an endpoint
should put the recorded answers in the same diff, and a reviewer should not have to find them.

---

## 4 · Inheritance

### Two axes, not one chain

It is tempting to write `workspace → folder → endpoint → case → batch` as a single chain. That is
wrong, and the wrongness shows up as soon as a batch spans two folders. The first four answer *what is
true about this thing*; a batch answers *what is true about this occasion*, and it cuts across the
tree rather than sitting under it.

So resolution has two stages:

```
containment   global → workspace → folder(s) → endpoint → case
occasion      → then the batch's overlay, applied last
```

The batch wins because it is the most specific occasion. That makes the useful case expressible
without polluting anything: *"for tonight's drift run, also ignore `$.version`"* lives on the batch and
disappears with it. Flattened into one chain, that rule would have to be written onto every endpoint
it touched, and then removed from all of them.

### What inherits

Auth, headers, variables, timeouts and retry, comparison rules, snapshot policy (normalisation and
redaction), and the array-identity keys. Per **setting**, not per level — the closest level that says
anything about *that* setting wins and everything else keeps inheriting, which is what
`ComparisonSettingsResolver` already does and the reason its members are nullable.

### Lists: replacement, and what it costs

`ComparisonSettingsResolver.PickReference` is last-one-wins **wholesale**: a request's `ignoredPaths`
*replaces* the folder's list. That is deliberate, and `ComparisonSettings` records why — union
semantics were considered and rejected so that reading one level tells you what applies there, and so
an inherited rule that is wrong for one endpoint can be dropped. An empty non-null list means "ignore
nothing here", not "inherit".

The cost is real and is already visible in the shipped environment-comparison window. Adding one rule
means restating the inherited ones, and the UI does it silently: `IgnorePathAsync` adds to the
*resolved* list and saves the whole thing, so adding one path to a request that inherits three copies
all four onto the request. Inheritance for that setting is then dead — edit the folder afterwards and
that request ignores you. Nothing says so.

So this is a trade to settle, not a bug to fix. [spec-endpoints.md §4.3](spec-endpoints.md) lays both
options out; the recommendation there is that each level contributes **additions and removals**:

```json
"ignoredPaths": { "add": ["$.orders[*].etag"], "remove": ["$.requestId"] }
```

Three things fall out of that, all of which are the point:

- Every entry carries provenance, so a chip can read *inherited from folder: orders*.
  `Resolved<T>` already carries `Scope` and `SourceName`; it moves from per-list to per-entry.
- Removing an inherited rule writes a `remove` at your level instead of silently copying the rest down.
- A removal is legible in a diff and in review. "The list got shorter" is not.

Scalars need nothing: nullable-means-inherit is already right.

---

## 5 · Oracles

```
none            assertions only - today's collection run
snapshot        compare against the stored response for this case + environment
environment:X   run twice, compare the two answers - the shipped comparison window
run:<id>        compare against a previous run's stored results (drift)
```

All four share one comparison engine, one settings hierarchy and one difference report. A new oracle
is a new way of *obtaining the other side*, never a second comparison implementation — that is the
test for whether this design is being held to.

`environment:X` is the only one that sends twice. Everything else sends once and reads the other side
from disk, which is why regression is cheap enough to run per commit and comparison is not.

---

## 6 · Snapshots

### Normalise on write, ignore on read

Both are driven from the same rules, and they are not the same operation.

**Normalising** happens when a snapshot is recorded: volatile values are replaced by a stable
placeholder, object keys are sorted, numbers and dates are written canonically. A snapshot full of
real timestamps produces a diff on every re-record and makes the file useless in review.

**Ignoring** happens when a snapshot is compared: the field is present but not judged.

Prefer normalising. A normalised snapshot is reviewable, and its diffs mean something. Ignoring is for
fields you cannot predict but still want to see.

### Redaction is not optional

Snapshots are committed. Responses contain tokens, emails, and whatever else the API returns. Snapshot
policy must be able to redact by path *and* by pattern, and redaction must run before the file is
written, not before it is compared. This is the same principle the variable-write policy already
enforces for captures — a value that must not be committed must never reach a tracked file, rather
than being cleaned up afterwards.

### Tolerances, not only ignores

The single biggest quality difference between a toy and a contract-testing tool. Ignoring a field
stops checking it entirely; a tolerance keeps checking its shape while allowing the part that legitimately
moves:

```
$.total            ±0.01
$.generatedAt      within 5 minutes of now
$.requestId        matches ^[0-9a-f]{32}$
$.orders           length within 10% of the snapshot
```

Most fields people currently ignore are fields they would rather constrain. Ignoring them is a
concession to the tool, and every one is a regression that will not be caught.

### Accepting a new snapshot

Regression testing without a good accept flow is abandoned within a week. Two routes, and both are
needed:

- `--update-snapshots` in the CLI, for when the change is intended and already reviewed.
- In the UI, per-endpoint and per-field accept from the same diff that reported it. Accepting one
  field writes the new value into the snapshot and leaves the rest of the differences standing.

Never auto-accept on failure. A snapshot that updates itself tests nothing.

---

## 7 · CLI

One verb, orthogonal flags — the payoff of the oracle model:

```bash
fubar run <selector> --env staging
fubar run <selector> --env staging --oracle snapshot
fubar run <selector> --env staging --oracle env:production
fubar run smoke --oracle snapshot --update-snapshots
fubar run orders/get-order --env staging --case not-found --report junit=out.xml
```

`<selector>` is a path in the tree (folder, endpoint, or endpoint + case) or a batch name. Individual
and batch are the same command with a different selector, which is what stops them drifting.

**Exit codes** keep the existing meaning: `0` everything matched and every assertion passed, `1`
differences or assertion failures, `2` could not run. A status code still never fails a run on its own
— that decision is in `RunReport` and holds here.

**Report identity must be stable.** A JUnit test name is `folder/endpoint/case`, never an index or a
timestamp, or CI loses the ability to say "this one started failing".

---

## 8 · What this buys that today's model cannot

- One endpoint, several cases, one place. The 404 case stops being a stranger to the happy path.
- Snapshots per environment, so recording against staging does not make production fail on data.
- A batch is a committed, named artifact, so "the smoke run" means the same thing on a laptop and in CI.
- A rule can be scoped to an occasion, so a one-off run does not need permanent rules written for it.
- Expected differences between two environments (`$.environment` will always differ) live on the
  batch that compares them, not on every endpoint.
- Parallelism becomes safe to offer: a batch that declares its cases independent can run them
  concurrently. Today the runner is strictly sequential because captures chain, and it cannot know
  when they do not. Opt-in per batch, never inferred.

---

## 9 · Sequencing

The model change touches the file format, the tree, the runner, import and the comparison chain at
once. It should not be one commit, and it should not start with the format.

1. **Settle the list semantics** in §4, then implement them in `ComparisonSettingsResolver` with
   per-entry provenance. Contained, ends the silent copying the shipped window does, and every later
   level then inherits the decided behaviour instead of being retrofitted onto it.
2. **The oracle seam.** Reshape the runner so "what judges the response" is a parameter. The
   environment oracle already exists; `none` is today's behaviour. No format change yet.
3. **Snapshots** against the seam from step 2: record, normalise, redact, compare, accept.
4. **Endpoints and cases**, with a migration that turns each request file into an endpoint with one
   default case. The format change lands last, when everything it feeds already works.
5. **Batches**, then the CLI selector that reaches all of it.

Steps 1-3 are useful on their own with the current file format, which is what makes this safe to do
incrementally rather than as a rewrite.
