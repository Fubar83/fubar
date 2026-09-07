# Decisions

Choices that shape the code and are not derivable from it. Recorded so the reasoning survives the
conversation it was made in — the codebase already explains *what* it does everywhere; this is the
short list of *why it was allowed to*.

## A · One request open at a time — kept, revisit later

**Decided 2026-09-04.** The single canvas stays. `RequestEditorPane.md §1` argues for it throughout,
and the thing that made it unsafe — replacing a dirty editor destroyed the edits — is fixed: both
switching request and closing the window now ask (`UnsavedChangesPrompt`).

Deliberately *not* settled permanently. Tabs would make the prompt rare rather than merely safe, and
whether the interruption is constant enough to justify them is a question a few weeks of real use
answers better than either judgement did. Revisit in Phase 7 with that evidence, not before.

The prompt is needed either way, which is why it did not wait for this.

## B · `fubar.json` variables — resolved, at the bottom of the chain

**Decided 2026-09-04.** `AppManifest.Variables` was documented as "public workspace variables …
committed to Git" and read by nothing: `VariableResolver` consulted the active environment, then the
session store, then gave up. Built and never wired — the failure CLAUDE.md's closing section warns
about, and the third instance of it in this repository.

It becomes a real resolution source at the **lowest** precedence, below the environment. That is the
shape that makes it useful rather than merely present: a freshly imported workspace is runnable
before anyone picks an environment, and the OpenAPI import already produces a `baseUrl` that belongs
there. Non-secret values that do not vary by environment finally have a home.

Deleting it was the alternative and would also have been defensible. Resolving it wins because the
gap it fills is real, not because the field already existed.

## C · File-format floor — the first tagged beta

**Decided 2026-09-04.** Three legacy shapes were kept alive by migration code:
`RequestModel.LocalVariables` (retired), `ResponseDiffIgnorePaths` (superseded by `Comparison`), and
the pre-template `AuthConfig` behind `OAuth2LegacyTemplate` / `AuthProvider.Upgraded`.

All three go. The first tagged beta is the floor: below it, a workspace must be opened by an older
build first.

The reasoning is entirely about timing. Pre-1.0 with no public release means nobody has files that
would have to be supported, so the cost of an aggressive floor is close to zero — and it rises the
moment a release exists. This will never be cheaper.

Migration runs once on open, rewrites each `request.json` into the new shape, and **logs what it
changed**: it is editing files the user is about to see in a diff, and a silent rewrite of committed
content is not something to spring on someone.

`AppVariable.IsSecret` keeps its back-compat shim. One property, no branching cost, and it fails
safe.

## D · Folder level of comparison settings — kept for now

**Open.** Eight settings inheriting independently across three levels, with provenance tooltips and a
three-way save, is more machinery than the request builder has — for deciding how two responses are
diffed.

Not removed, because nobody has evidence yet. The test is cheap and specific: after a few months of
real use, look for a `_folder.json` carrying a `comparison` section. If none exists, collapse to
global + request, which removes a layer from the resolver, the mapper and the UI while leaving the
file format forward-compatible either way.

Revisit in Phase 7.
