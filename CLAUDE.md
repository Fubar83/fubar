# CLAUDE.md

Guidance for Claude Code (and contributors) working in this repository.

## What this is

A monorepo holding two Avalonia 12 / .NET 10 desktop apps and the design system they share:

| Project | What it is |
| --- | --- |
| `src/Fubar.Studio.*` | **Fubar API Studio** — a native Postman/Insomnia alternative. Binary: `FubarAPIStudio`. |
| `src/Fubar.Diff.*` | **Fubar Diff** — a diff tool. Binary: `FubarDiff`. |
| `src/Fubar.Controls` | The shared design system + control library. Sandbox: `Fubar.Controls.Gallery`. |

These were three repositories until 2026-08-25, with `Fubar.Controls` shipped as a NuGet package. The
split was reversed when API Studio needed the **diff view** too: that made the sharing a mesh rather
than one-way, and every cross-cutting change would have needed several PRs plus a package publish.
Everything is now a project reference. **Nothing here is packed** — `Directory.Build.props` sets
`IsPackable=false` repo-wide.

Per-app detail lives in [`docs/api-studio.md`](docs/api-studio.md), [`docs/diff.md`](docs/diff.md) and
[`docs/controls.md`](docs/controls.md); changelogs are `docs/changelog-*.md`.

## Build / run / test

```bash
dotnet build Fubar.slnx                # everything (must be warning-clean)
dotnet test  Fubar.slnx                # every suite

dotnet run --project src/Fubar.Studio.UI                   # API Studio
dotnet run --project src/Fubar.Studio.UI -- --run --report results.xml   # run a collection; 0 pass, 1 fail, 2 could not
dotnet run --project src/Fubar.Diff.UI -- left.json right.json
dotnet run --project src/Fubar.Diff.UI -- --check left.json right.json   # headless; 0 same, 1 differ, 2 failed
dotnet run --project src/Fubar.Diff.UI -- --functional -q a.cs b.cs      # 0 unless the C# behaviour changed
dotnet run --project src/Fubar.Controls.Gallery            # component sandbox

./build/publish-api-studio.ps1         # self-contained per-RID binaries (pwsh 7+)
./build/publish-diff.ps1

git tag diff-v0.1.0-beta.1             # release ONE app; the tag prefix picks it (studio-v… for the other)
```

**Releases are per app.** `.github/workflows/build.yml` fires on `diff-v*` and `studio-v*` only, and a
tag naming no app is rejected rather than guessed at. Whether a release is a prerelease is DERIVED from
the version (a hyphen means one, per semver) rather than set by a flag, because a beta published as the
repository's "Latest release" is the one mistake here that reaches users. See README → Releasing.

## Architecture

Both apps are clean-layered and **enforced by tests**. Dependencies point inward only:

```
Fubar.Controls          shared design system - depends on Avalonia + AvaloniaEdit + BCL, nothing else
      ▲ consumed by both apps
Presentation ── *.UI              Views + thin ViewModels + Composition root (DI)
Application  ── *.Application     Use-case / orchestration services
Core         ── *.Core            Entities + domain policy + PORTS (interfaces)
Infrastructure ── *.Infrastructure  Adapters implementing the ports
```

- `Core` depends on nothing but the BCL. `Application` and `Infrastructure` depend only on `Core`.
- `*.UI` depends on `Application` + `Core`; **UI ViewModels must NOT reference `*.Infrastructure`** —
  `Composition.cs` is the one allowed UI→Infrastructure edge in each app.
- **`Fubar.Controls` must not reference either app.** `Fubar.Controls.Tests.ArchitectureTests` holds
  an allowlist for this, and it matters MORE here than it did across repositories: with everything a
  project reference away, a stray `using Fubar.Studio.Core` in the library would compile fine and
  nothing else would object.
- **DiffPlex is confined to `Fubar.Diff.Infrastructure`**, behind `IDiffEngine`, and **Roslyn**
  (`Microsoft.CodeAnalysis.CSharp`, syntax only) likewise, behind `ICodeStructureParser`. The *language*
  scanner is not an engine and lives in `Fubar.Diff.Core/Languages` - it is hand-written, BCL-only
  domain policy (what a comment is), not an adapter over anything.

`tests/Fubar.Studio.Architecture.Tests` and `tests/Fubar.Diff.Architecture.Tests` fail the build on any
of this.

## Invariants that are easy to break

**Comparison keys are not display text** (Diff). The normalizer produces a key per line for matching;
`FileComparisonService` projects every row back onto the real document lines before rendering.
Skip it and "ignore case" shows the user a lower-cased copy of their own file. The same rule extends
to character spans, which is why they are computed *after* projection.

**Filler-line discipline** (Diff). Editor line `i` is always `DiffResult.Lines[i]`, on both sides.
Never read the editors back to save — go through `MergedDocument`, or the filler blanks get written
into the user's file. Both sides having equal line counts is also what makes scroll sync a plain
offset copy rather than a line-mapping scheme. **This invariant is deliberately NOT preserved by
`AlignedText.BuildCompact`** — the stacked Diff pane shows each side as its own compact block with no
filler at all, since a stacked layout has no row-count-parity requirement to protect. Only the
side-by-side main panes (`AlignedText.Build`) need fillers; do not "fix" `BuildCompact` to add them.

**YAML goes through the JSON pipeline, and the `Json*` names stayed** (Diff). `YamlAstParser` produces
`JsonAstNode`s, so the differ, ignore rules, array identity keys, the change tree, the spans, the
reports and `--check` all work on YAML without knowing it exists. The names describe the SHAPE - a
document of objects, arrays and scalars - which is exactly what YAML's data model is; renaming the
family (`JsonAstNode`, `JsonChange`, `JsonSemanticPass`, `JsonSemanticDiffer`, …) was judged more risk
than value, so read `Json*` as "structured" where it matters. The asymmetry to respect is in
`StructuredFormatDetector`: **JSON is detected by trying to parse, YAML only by file extension**,
because nearly all text is valid YAML and sniffing it would make every log comparison a comparison of
two one-scalar documents. The format is tracked per side, so a `.json` can be compared against a
`.yaml`. YAML scalar typing is the 1.2 core schema only - never 1.1's `yes`/`no` booleans, which is
the Norway problem - and a quoted number stays a string, because `port: 8080` differing from
`port: "8080"` is the change most likely to break something.

**Structural C# comparison ADDS an answer and changes nothing about the diff** (Diff). This is the one
rule that separates it from the JSON semantic pass, and the two look similar enough that "making them
consistent" is a live risk. `JsonSemanticPass` is allowed to decide which text rows COUNT as
differences, because two JSON documents in a different property order genuinely are the same document.
`CodeStructurePass` is not, because two C# files in a different member order are NOT the same file -
the bytes differ, a review is about those bytes, and quietly reporting them as equal would be the tool
lying about what it was shown. So it marks no rows, filters nothing and changes no count; it produces
`FileComparison.CodeChanges` and `CodeSummary` BESIDE the result. Everything else follows from that:
it is on by default (worst case is an empty panel), it runs on the ORIGINAL text rather than the
canonicalized copy (a structural answer about a document the user cannot see would name members at
lines that are not there), and `--functional` is a separate flag from `--check` rather than a change
to what `--check` means. Roslyn lives in Infrastructure behind `ICodeStructureParser`, held to the
same confinement rule as DiffPlex and for a stronger reason: the differ, the summary, the panel and
the CLI all work on a language-neutral `CodeNode`, which is what makes a second language one adapter.
Three implementation rules were each found by a failing test, not by design. A node's own TOKENS
exclude everything belonging to a child node, or every ancestor of every edit reports as changed and
the tree says "the file changed, the class changed, the method changed" where only the last is
information. A node's own TEXT additionally drops whitespace at its very start and end, or inserting a
method marks its neighbour as reformatted because the blank line above it moved - while whitespace
BETWEEN its own tokens is kept, which is where re-indentation actually lives. And the own-token walk
must not descend into excluded children (`OwnTokens`, not a filter over `DescendantTokens()`): the
filtering version enumerates the whole file once per level of nesting and measured 1.3 s on a 2 MB
file against a few ms.

**Semantic JSON is a refinement, not a second pipeline** (Diff). The text differ decides how lines
line up; `JsonSemanticPass` decides which of them matter. One `DiffResult` shape means every renderer,
the diff map, navigation and merge work in both modes. The trap here: `SemanticChanges` (from
`JsonSemanticDiffer.Compare`) is unaffected by alignment quality - it parses and diffs the AST
directly - but `Result.Modified`/`Inserted`/`Deleted` (the ROW-level counts) come from
`SemanticLineFilter` acting on the raw text alignment, so those can look strange when the two sides
are formatted very differently (a minified file against a pretty one). That is accepted, not a bug to
chase: Text mode never reformats a file to fix its own alignment (see below), and the Json view is
what handles that pairing properly.

**Text mode never reformats a file - not even JSON, not even automatically** (Diff). It used to
pretty-print both sides before alignment whenever semantic comparison applied, specifically so a
minified-vs-pretty pair still lined up; that was removed because it silently rewrote the user's
content to compensate for Text mode's own limitation. The Json view exists for exactly that pairing
and needs no reformatting to handle it (see below), so Text mode is free to just show what it was
given. The one surviving way to reformat for display is the pre-existing, explicit "Reformat" toggle
(`ComparisonOptions.NormalizeStructure`, labeled "Normalize XML" until it was made visible for JSON
too - see below) - opt-in, and `TextLineNormalizer`'s diff-aware pretty-printer (`PrettyPrintJson`,
keeping all-scalar containers on one line so an array of small objects like `{"id": 1}` does not
explode into boilerplate braces a line differ then mismatches) still backs it. Because canonicalised
output IS what gets displayed (`FileComparisonServiceTests.Canonicalisation_output_IS_displayed`), a
Take Left/Take Right + Save after turning this on saves the reformatted text - which is the point: the
user opted in, so it is fine for it to stick if they then choose to save. Do not reintroduce an
unconditional "canonicalize before alignment" step - that is precisely what was removed, and why.

**Ignore rules are applied where differences are DECIDED, not where they are drawn** (Diff).
`JsonSemanticDiffer.Compare` marks changes through `JsonIgnoreRules` before returning, so the tree, the
text view's line filter, the diff map and navigation all agree. Filtering in a view instead would make
that view disagree with the others about what changed.

**Fubar Diff has no click-to-ignore affordance - `DiffPaneViewModel.IgnorePathCommand` is left null on
purpose** (Diff). It exists for API Studio, where a comparison belongs to a request that can remember
the rule; Fubar Diff's own way to set `IgnoredPaths` is the manual list in `SettingsWindow` instead.
Before that window existed, `IgnoredPaths`, `IgnoreNullVsMissing` and `ArrayKeyOverrides` were fully
built in Core (`JsonComparisonOptions`, `ArrayKeyResolver`) and even had persistence fields waiting in
`AppSettings`, but `ComparisonViewModel.CurrentOptions()`/`ApplyDefaults`/`CaptureOptions` never
actually read or wrote any of them - the feature was completely inert from the UI's side despite every
other piece of it working. Check that a Core option is *read somewhere in `ComparisonViewModel`* before
assuming a UI gap here means "not built yet" - it may mean "built, but never wired to a control."

**The Json view has no alignment at all, on purpose** (Diff). `RawJsonPane` shows each side's raw,
unaligned text and highlights the current change's own `JsonAstNode.Span` directly - no fillers, no
line-for-line correspondence between the two sides. This is what makes it immune to the class of
problem noted above: there is no shared line numbering for a formatting or property-order difference
to break. Do not "simplify" it by routing this view through `AlignedText` - that would reintroduce
exactly the dependency it exists to avoid. There is no standalone Tree mode any
more - `Text` and `Json` are the only two `DiffViewMode` values, and `DiffPaneViewModel.Show` picks
between them itself (Json whenever semantic comparison ran) rather than leaving whatever was
previously selected.

**The Json view has its own detail pane, built from spans rather than rows** (Diff). `DiffDetailPane`
(Text mode) excerpts a hunk's aligned ROWS via `AlignedText.BuildCompact`, which only makes sense where
rows are aligned in the first place. `JsonDetailPane` is the Json-mode counterpart: it excerpts a
change's own lines directly from `LeftRawText`/`RightRawText` via `JsonSpanExcerpt.Build`, and reuses
`RawJsonPane` (not `DiffEditorPane`) for the same reason the main Json panes do - no cross-side line
correspondence to preserve. Both panes are toggled by the same "Diff pane" checkbox
(`DiffPaneViewModel.IsDetailVisible`); `JsonView.axaml.cs` collapses its row heights identically to
`DiffView.axaml.cs` (duplicated on purpose - the two views are otherwise independent, and the collapse
logic is small enough that sharing it is not worth a base class). One consequence of the highlight
being line-range-only (see above): for a minified, single-line file, the excerpt on that side is the
*entire* line, since there is no column-level highlighting to narrow it further - not a bug, just what
"the change's own lines" means when the whole document is one line.

**Two semantic change lists exist for a reason - do not collapse them** (Diff). `FileComparison`
carries `SemanticChanges` (spans into `Left`/`Right`, used by Text mode's line filter, ignore rules and
the tree) and `OriginalSemanticChanges` (spans into `OriginalLeftText`/`OriginalRightText` - each
side's text exactly as given - used by the Json view's highlighting). Now that Text mode no longer
reformats JSON automatically, the two are usually IDENTICAL text; they can still diverge when the
user explicitly turns on "Reformat" for a JSON file, and the pairing has to stay correct for that
case too. They are guaranteed to agree on path, kind and count regardless - canonicalizing never
reorders or renames anything - which is what lets `DiffPaneViewModel` pair the tree (built from the
first list) with navigation (walking the second) by matching `JsonPath` strings rather than the
`JsonChange` objects, whose spans can legitimately differ between the two. `RecompareAsync`/`Recompare`
thread the original text through explicitly from the previous result rather than recomputing it from
`Left`/`Right` - that would silently substitute the canonicalized text the moment NormalizeStructure
was toggled after the first render.

**A three-way merge REUSES the two-way aligner rather than aligning three documents** (Diff).
`ThreeWayMerger` is handed two ordinary `IDiffEngine` alignments - ancestor against each edit - and
reads only their `Unchanged` rows. Wherever both agree a line survived, all three documents are
synchronised; everything between two such points is one region to classify. This is not just less code
than a three-way alignment: it is what makes a merge agree with the two-way diff of the same files,
because every comparison option, every code rule and the slider are already baked into the keys and
rows before the merge looks at them. Two consequences that read as bugs and are not. (1) A `Modified`
row is NOT a match - the aligner paired two lines that differ, and taking that as "survived" would
merge one side's edit away silently. (2) Two edits with no surviving line between them are ONE region,
so adjacent changes from both sides become a single conflict rather than two decisions whose answers
would have to agree with each other; git resolves it the same way.

**Three-way rows produce the same `AlignedDocument` the two-way view uses, and that is the whole
budget** (Diff). `ThreeWayAlignedText.Build` emits exactly what `AlignedText.Build` does, so
`DiffEditorPane`, `CharSpanColorizer`, `SourceLineNumberMargin` and `ChangeLineBackgroundRenderer`
needed no knowledge of merging at all - a third pane cost one view and one view model. The filler
discipline extends with it: row `i` is `ThreeWayResult.Lines[i]` in ALL THREE editors, which is what
keeps scroll sync a plain offset copy and makes a region one horizontal band. The tint mapping is
deliberate and worth not "fixing": the ancestor column is tinted as removed, a side that MOVED is
tinted as added, and a side that did not move is left untinted even inside a region. Tinting all three
columns everywhere would hand the single question a merge asks - who moved? - straight back to the
reader. Character spans are computed against the ANCESTOR for both edits, never left-against-right: a
merge IS two independent sets of changes to one starting point, and in a conflict "what did each of
them do" is the question, where a left-vs-right span would show the disagreement while hiding that both
may have rewritten the line. The ancestor column carries no spans of its own - it is already tinted
whole as the text being replaced, and a third set of highlights would ask the reader to cross-reference
three things to answer one question.

**`IsConflict` is a flag on `AlignedLine`, not a fifth `ChangeKind`** (Diff). Same reasoning as
`IsIgnored`, which it sits beside: a conflicting row is an ordinary `Inserted`/`Deleted` row to every
renderer, hunk-grouper and navigator, and a fifth kind would land in every exhaustive switch over the
four that exist. `ChangeLineBackgroundRenderer` checks both flags BEFORE the by-kind lookup, for
opposite reasons - an ignored row would otherwise get no tint (its Kind is `Unchanged`), and a
conflicting row would otherwise get the SAME tint as the changes that need no decision, which is the
one thing a merge view must not do.

**A hand-edited merge result is saved as TEXT, and the decisions become vestigial the moment it is
touched** (Diff). The three-way window's Result pane is editable because the answer to a real conflict
is regularly neither side. From the first keystroke the decisions and the document disagree, and the
document is the one that is right - so `MergeViewModel` switches from `SaveThreeWayAsync` (build from
`ThreeWayMergeState`) to `SaveThreeWayTextAsync` (write these lines), which takes only the PATH and the
FILE FORMAT from the destination. Three rules hold it together and none is optional. The pane
distinguishes its own writes from the user's (`DiffEditorPane._applying`), or `RefreshOutput`'s rewrite
after every decision would be read back as a hand edit and the flag would never clear. A resolve after
a hand edit ASKS, with *Keep my edits* first so it is both the primary button and what a dismissed
dialog (-1) returns - the same "a prompt that cannot be shown is a NO" rule as everywhere else - and
with no `IConfirmationService` at all the resolve buttons decline rather than rebuilding. And the three
INPUT columns stay read-only on purpose: editing one needs a full re-merge, which renumbers the regions
every decision is keyed by. The Result pane is downstream of the decisions rather than upstream of
them, which is the whole reason it could be made editable and they could not.

**An unresolved conflict saves the ANCESTOR, and the UI must say so** (Diff).
`ThreeWayMergedDocument` has a defined answer for a region nobody decided, and `MergeService` does not
refuse to write one - stopping half way through a long merge to save what you have is legitimate, and
a service that threw would make it impossible. That makes it the UI's job: `MergeViewModel` shows a
banner before (`HasUnresolvedConflicts`) and names the count in the status line after. Do not "fix"
this by throwing in the service, and do not drop the warnings - the fallback is only acceptable while
it cannot be a surprise.

**Linked (one-folder) comparison reuses `FolderComparison` with BOTH roots the same** (Diff). That is
not a shortcut - it is what makes the entire folder window, its filtering and its "open this pair" work
unchanged for snapshot review. The two halves of a linked pair differ by FILE NAME, not by root, which
is precisely what `FolderEntry.LeftRelativePath`/`RightRelativePath` already carry (they were added for
case-insensitive pairing and turned out to be exactly the right shape for this). Do not give linked
mode its own result type. Two behaviours differ from the two-tree walk on purpose: a file no rule
matches is OMITTED rather than reported as one-sided - with one folder there is no "other side", and an
ordinary source file beside some snapshots is not a difference - and a folder containing no pairs at all
is dropped rather than shown empty.

**A folder comparison's leniency stops at file CONTENT** (Diff). Every listing in
`FileSystemFolderScanner` swallows its exceptions and returns empty, because a tree of any size holds
something the current user cannot open and refusing to compare two checkouts over one locked folder is
a worse answer than comparing the rest. `ContentsEqual` does the opposite: an unreadable file is
reported as a DIFFERENCE, never as a match, because "these are identical" about a file that could not
be opened is the one answer a comparison must never give. Do not make these consistent with each other
- they are deliberately opposite.

**Each side of a folder comparison keeps its OWN relative path** (Diff). Names pair case-insensitively
by default, so `README.md` on one side is the same entry as `readme.md` on the other - and building
both absolute paths from one spelling works on a case-insensitive filesystem and fails to open the file
on a case-sensitive one. `FolderEntry.LeftRelativePath`/`RightRelativePath` exist for that, and are
what the UI must use when opening a pair; `RelativePath` is for display and identity only.

**Auto-refresh must never discard a merge decision** (Diff). Decisions are keyed by hunk INDEX and a
fresh comparison renumbers the hunks, so reloading over unsaved ones would either drop them or apply
them to different changes - silently, and not noticed until the save. `ComparisonViewModel` therefore
refuses to auto-reload while `HasUnsavedMerge`, raising `FilesChangedOnDisk` for a banner with a manual
Reload instead. Two implementation details are load-bearing rather than incidental: the watcher watches
the containing DIRECTORY, not the file, because editors save by writing a temporary file and renaming
it over the target and a file-bound watcher goes deaf at exactly that moment; and our own writes are
recognised by TIMESTAMP rather than by a flag held across the save, because the watcher only speaks
after a quiet period, by which time a flag cleared in a `finally` is long gone and our own save arrives
looking external.

**A user-supplied regex is hostile input, and `LinePatternMask` treats it that way** (Diff). Two
failure modes, both handled and neither optional. A MALFORMED pattern is dropped rather than thrown -
these come from a settings file a user can hand-edit, and refusing to compare anything because one rule
has a stray bracket is not an acceptable answer (`Create` reports which were rejected so the UI can
say). A PATHOLOGICAL one - `(a+)+$` and friends - cannot be allowed to hang the window, so patterns
compile on `RegexOptions.NonBacktracking`, which is linear in the input; only a pattern needing
lookaround or backreferences falls back to the ordinary engine, and that one carries a match timeout.
Masking replaces the match with a marker character rather than with nothing, deliberately: blanking to empty
would make `ab` and `a` compare equal under the rule `b`, hiding a difference nobody asked to hide.
And it is applied BEFORE the normalizer, so a rule written against what the user can see matches what
they see rather than a trimmed, case-folded copy.

**The unified view is the ONE place "editor line i is `DiffResult.Lines[i]`" does not hold, and it pays
for that itself** (Diff). A modified row becomes two lines there and a filler becomes none, so the
mapping stops being the identity. Rather than weaken the invariant everywhere - which would cost the
side-by-side view its offset-copy scroll sync and make every renderer's row arithmetic conditional -
`UnifiedText` builds its own document and carries the translation back explicitly: `UnifiedDocument.Hunks`
in ITS row indices (same hunks, same order, different numbers) and `SourceRows` mapping each of its rows
to the comparison's. Anything addressing the unified view must go through those; `DiffPaneViewModel`
keeps `UnifiedScrollToRow` and `UnifiedFolds` separate from their side-by-side counterparts for exactly
this reason, and computing either from the other's coordinates is wrong the moment a row splits.

**Json is not a VIEW mode** (Diff). `DiffViewMode` has two members - side by side and unified - and both
are layouts of a TEXT comparison. Whether the Json view shows is decided by whether the semantic pass
ran, which the Auto/Text/Json Compare selector controls. Having it in both places meant two controls
answering the same question, and picking Text in one and Json in the other was a contradiction the app
resolved behind the user's back (`OnIsSemanticChanged` used to quietly reset `ViewMode`). Do not add it
back: to see JSON as two columns of text, compare it as text. A consequence worth keeping: `Show` no
longer resets `ViewMode`, so a preference for unified survives the next comparison.

**A change's span is the whole `"name": value` pair when the pair APPEARED or WENT AWAY** (Diff).
`JsonChange.LeftSpan`/`RightSpan` union the name span with the value's for `Inserted`, `Deleted` and
`IsReorder`, and return the value alone for an ordinary `Modified`. The parser has always recorded
`JsonAstProperty.NameSpan` and the change has always carried it, but the view highlighted
`Left?.Span` - the value - so an added field showed a coloured value beside an untouched-looking key.
Do not extend the union to `Modified`: the key is still there and still spelled the same, and
colouring it claims an edit nobody made.

**Reformatting for display re-derives the change spans, and the two travel together** (Diff). A
`JsonChange` carries offsets into ONE specific string, so `FormatJsonForDisplay` returns the text and
the changes as a single `JsonDisplay` - reformatting a side without re-deriving them leaves every
highlight pointing at the line a value used to be on, which reads as the comparison having broken.
That is also why it lives on the service rather than in the view model: re-deriving needs the parser.
`JsonFormatter` works from the AST and writes every scalar back as its own `RawText`, so `1.0` stays
`1.0` and `1e3` stays `1e3` - a formatter that re-derived values would edit the file's numbers while
claiming to have changed only whitespace.

**An array can be compared three ways, and "unordered" is the only one that works without a field**
(Diff). `ArrayMatchMode` is Position, Unordered or Key, and `JsonSemanticDiffer.ModeFor` is the single
place the precedence lives - public precisely so the context menu's check mark and the comparison cannot
drift into different answers. **Every instruction about ONE array beats every setting about all of them**, and that ordering was got
WRONG first: the global `MatchArraysByPosition` sat above the per-path lists, so with that switch on,
choosing "Ignore order" on a single array did nothing at all - the menu recorded the choice, the check
mark moved, and the comparison ignored it. Reported from a real file, and it contradicted the rule
`ArrayKeyResolver` already stated for keys: an explicit override wins "including when everything else is
set to positional". Order now: a named `ArrayKeyOverrides` entry, an explicit `PositionalArrays` path,
an explicit `UnorderedArrays` path - then, and only then, the global `MatchArraysByPosition`, an
auto-detected key, the global `IgnoreArrayOrder`, and position. Two rankings among the rest are
deliberate. An explicit positional path beats an explicit unordered one because
that pair is a contradiction only the user can have written, and positional is its conservative half -
reporting a reorder nobody minds is a smaller failure than hiding one that matters. And the GLOBAL
unordered switch sits BELOW automatic key detection, because where a key exists it already ignores order
and additionally says which field of which element changed, which whole-value matching cannot.

Unordered matching exists because identity keys only answer "which element is this?" for objects
carrying an id. An array of STRINGS - tags, roles, feature flags, enabled locales - has no field to key
on, so it always fell through to positional and `["A","B"]` against `["B","A"]` reported two
modifications for a document nobody had edited. `JsonValueSignature` matches elements on their whole
value instead, which needs no field and works for scalars, objects and nested arrays alike. Three rules
inside it are load-bearing. It is a MULTISET, not a set: `["A","A","B"]` against `["A","B"]` has
genuinely lost an element, and set semantics would call them equal - the one answer a comparison must
never give. Property order inside an element does not change its signature (JSON objects are unordered
by definition) but NESTED ARRAY order does, because opting one array out of ordering says nothing about
the arrays inside it and a nested one that should also be unordered gets its own rule. And what is left
over after the exact matches is compared PAIRWISE rather than reported as a pile of deletions and
insertions - that is what keeps a field-level diff for an element that changed in one field, and what
lets ignore rules reach inside it at all. Matching purely by value would report a whole element as
replaced because a timestamp inside it moved, and the rule covering that timestamp would never speak.

**Array matching is per-array, and only fields that WOULD work are offered** (Diff).
`JsonComparisonOptions.PositionalArrays` is the per-path counterpart of the global
`MatchArraysByPosition`, because one document can hold a list of users where order means nothing
beside a list of steps where order is the whole content. `ArrayKeyScanner` finds every array and the
fields that could identify its elements, applying the same bar `ArrayKeyResolver` does - present on
every element of BOTH sides, scalar, distinct - so a field on the menu always matches; one that
silently failed would produce a diff that looks like data loss. An explicit override beats positional,
including the global switch, because naming a key for one array is the more specific instruction. Keys
may be dotted paths (`meta.id`), resolved by `ArrayKeyResolver.ValueFor`, which is also what
`JsonSemanticDiffer.KeyOf` goes through.

**A prompt that cannot be shown is a NO, never a yes** (Diff). `IConfirmationService.ChooseAsync`
returns -1 for "none of these", and every caller treats it as the safe answer: closing a tab is
refused, a disk conflict keeps the user's changes. `ConfirmationService` returns -1 when there is no
window to be modal to, and `ComparisonViewModel` refuses to close when no confirmation service was
injected at all. Do not "simplify" any of these to a default of the first choice - the choices are
things like *discard* and *overwrite*, and treating a dismissed dialog as agreement to one of them is
the exact bug the prompt exists to prevent. `UnsavedPromptTests` and `ShellCloseTests` pin the
refusals specifically.

**Unsaved state is tracked PER SIDE** (Diff). Both panes are editable, so a session can leave two
files to write and saving one of them is not "saved". `HasUnsavedLeft`/`HasUnsavedRight` are the
truth; `HasUnsavedEdits` and the legacy `HasUnsavedMerge` are derived. Two consequences worth keeping:
Ctrl+S writes only the sides that changed (rewriting an untouched file moves its timestamp, which is
enough to make a build think it is stale), and **Save As does NOT clear the dirty flag** - it writes a
copy somewhere else and leaves the compared file exactly as unsaved as it was.

**A file changing on disk under unsaved edits is a CONFLICT, and only the user can settle it** (Diff).
`OnFilesChangedOnDisk` has three paths and they are deliberately different: clean plus auto-refresh
reloads silently (a diff kept open beside an editor should stay current), clean with auto-refresh off
raises the banner (it used to do nothing at all, leaving the user reading a stale comparison with no
sign of it), and dirty prompts - keep mine / save mine over it / reload and discard. The banner is
raised as well as the prompt, so dismissing the dialog does not leave the situation unmarked, and
`_promptingConflict` stops a second dialog stacking on the first: editors save by writing a temporary
file and renaming it, which can produce several events in a row.

**An editable pane keeps the filler invariant rather than weakening it** (Diff). The roadmap said this
needed a bidirectional editor↔source offset map. It does not, and the reason is worth knowing before
anyone "fixes" it: the document stays the file-with-fillers, each filler carries a `TextAnchor`, and
`AlignedEdit.ToFileLines` takes it back apart with one rule - *a line belongs to the file unless it is
empty AND still a filler*. Every renderer, the diff map, the folds and the offset-copy scroll sync are
untouched. Do not reach for "just remove the fillers from the editable document" - that is the same
invariant-weakening the unified view had to pay for itself.

**Re-aligning after an edit is a PATCH, not a new document** (Diff). `FillerPatch` computes the blank
lines to move; replacing the text would throw away the caret, selection and undo history the user is
mid-sentence in. Four things around it are load-bearing and each was got wrong first. The caret is
restored by FILE position, never by raw offset - the text moves around the offset and the caret
silently lands on a different line. The continued undo group must be the OUTERMOST thing:
`document.BeginUpdate()` starts an undo group of its own, which un-continues ours and makes Ctrl+Z
take two presses for one change. Loading a document calls `UndoStack.ClearAll()`, because otherwise one
Ctrl+Z in a fresh comparison walks back past the load and empties the pane. And `FillerPatch` REFUSES
when the two alignments differ by more than fillers, which is a different comparison arriving - the
caller replaces the document instead.

**Anchors survive the user's undo but not the app's re-anchoring** (Diff). An anchor made before an
edit is put back by undoing that edit, because an undo is just another text change - so anchors need
no help there. What breaks them is re-anchoring mid-history, which re-aligning after every edit does:
undo past a re-alignment and the anchors describe a layout the document no longer has, a filler row
reads as a blank line the user typed, and the file quietly grows one. `DiffEditorPane` therefore
remembers the layouts it has shown, keyed by exact text, and answers from those when the document is
one of them. Bounded on purpose - the alternative is holding every revision of a large file for the
life of the tab.

**Taking a side is an EDIT, and `MergeState` is vestigial in the two-way path** (Diff). `Take left`
rewrites the target document through `DiffEditorPane.ReplaceRows` and lets the ordinary
edit → re-diff cycle follow, so it is visible immediately, lands on the editor's undo stack, and
cannot be renumbered by the next comparison - which is what the old pending-decision model was
vulnerable to (`RemapTo` existed purely to cope with it). `MergeState` is now always empty in
`ComparisonViewModel`, which is exactly what makes `MergedDocument.Build` round-trip the base side and
therefore save what the pane holds. The THREE-WAY merge still uses the old model and must keep it: it
resolves regions across three documents and has a defined answer for regions nobody decided.
`HunkEditTests` asserts the new path agrees with `MergedDocument` for the same choices.

**Folder copying copies and NEVER deletes, and the confirmation is not optional** (Diff). This is the
only thing in the app that writes a file the user did not name, so every decision about it is
deliberate. `FileCopyPlanner` (Core, no disk) makes every choice about WHICH file, because that is
where all the mistakes would be: the destination uses the spelling the destination side already has
(names pair case-insensitively, so writing the source's spelling would leave `README.md` beside
`readme.md` on a case-sensitive filesystem instead of replacing it), a direction with no source is not
offered, and identical files plan nothing. `IFileCopier` holds no policy at all and refuses only one
thing - copying a file over itself, which is reachable in one-folder mode and which `File.Copy`
answers on some platforms by truncating the file. `FolderViewModel` offers copying only when it has
BOTH a copier and an `IConfirmationService`, so a host that wires up one without the other gets no
copy buttons rather than silent overwrites. Deletion and "make this side match" are still not built,
on purpose: that is where a mistake becomes lost work. One ordering detail is load-bearing - the
re-walk happens BEFORE the status and error are set, because `CompareAsync` clears both for its own
run and reporting first means the failure message is wiped by the refresh that follows.

**A binary comparison is shown as an ordinary `DiffResult` of HEX rows, and that is why it cost so
little - but it is also the trap** (Diff). `HexDiff.Build` turns a `BinaryComparison` into the same
shape everything else consumes, so the side-by-side editors, scroll sync, tints, the diff map, F7/F8
and the collapse folds all work on bytes without knowing they are bytes. The cost is that the MERGE
also thinks it can work on them: a binary `FileComparison` carries EMPTY `TextDocument`s (the bytes
live on `Binary`), so a save would build a document of no lines and write it over the user's PNG.
Three things stop that and all three are deliberate - `ComparisonViewModel.SaveToAsync` returns early
when `IsBinaryComparison`, `ShowsMergeControls` hides the take-left/take-right group even though
`Pane.HasCurrentHunk` is perfectly true, and `HasPatch` is false because it reads
`_comparison.Result` (empty) rather than what the pane is showing. `BinaryComparisonTabTests` pins the
save guard specifically. Do not "simplify" any of them by trusting the ones above it.

**A binary result must never be re-run through the text path** (Diff). `Recompare`/`RecompareAsync`
branch on `IsBinary` and only swap the options. Falling through would SUCCEED - the empty text
documents compare equal - producing an empty diff and dropping `FileComparison.Binary`, so the tab
would quietly turn from a picture into "the files are identical" the moment anyone ticked "ignore
whitespace". Pinned by `BinaryFallbackTests`.

**"Is this binary" has exactly one answer, and it lives in Core** (Diff). `BinaryContent.LooksBinary`
is used by `TextFileReader` to refuse a file and by the comparison to decide it should take over
instead; two implementations that could disagree would give a file refused by one path and diffed as
text by the other. The hand-off is by `TextFileReadException.IsBinary`, a FLAG rather than a caller
matching on `Reason` - that string is written to be shown to a person and will be reworded, and
binary comparison silently switching itself off over a copy edit is not a break anything would catch.
Image formats are detected from the CONTENT signature, unlike languages, which are detected from the
extension: a renamed `.png` that is really a JPEG is ordinary, and being wrong here is immediately
visible because the picture either appears or it does not.

**The location map aggregates per PIXEL, and what it draws is decided in Core** (Diff).
`DiffMapModel.Build` turns rows and hunks into bands; `DiffMap` only paints them. The obvious
implementation - one rectangle per hunk with a minimum height so it cannot vanish - is what was here
before, and it fails in exactly the case a map exists for: on a 60,000-line file drawn 600px tall one
pixel is a hundred rows, every hunk clamps to the same minimum, and forty changes in a rewritten region
look identical to one stray edit beside it. Counting the changed rows behind each pixel and reporting
that as `MapBand.Density` makes "how much changed here" legible again. Density is drawn as WIDTH from
each side inwards rather than as opacity, because a faint mark on a dark strip is easy to miss entirely
while a short one is unmistakably present; the 0.15 floor in the model is what keeps a single-line change
visible on a huge file, and losing those would make the map worse than none, since an empty strip reads
as "nothing here".

Marks are per SIDE - a deletion paints only the left half, an insertion only the right, a modification
both - and the two sides accumulate separately, so a pixel holding a deletion and an insertion shows one
of each facing rather than merging into "modified". That per-side split costs nothing precisely because
the panes are row-aligned, which is also why this needs none of WinMerge's connecting lines between its
columns: those exist to tie together two columns at independent scales, and ours are the same scale by
construction. The one place a connecting line carries information here is a MOVE, whose two ends sit at
different rows by definition - hence `MapMoveLink`, capped and skipped for short travels so the links
stay information rather than hatching. The map also marks IGNORED rows, which form no hunk and so drew
nothing at all before, leaving the reader unable to tell "identical" from "a rule is hiding this"; and it
counts hunks wholly above and below the viewport, which is the question people scroll a diff they have
already read in order to answer. `DiffMapModel.Build` degrades to hunk-shaped bands when handed no rows,
because a blank strip reads as "no changes" - the one wrong answer a diff tool must never give.

**Scroll sync copies BOTH axes, and horizontal was a reversal** (Diff). `DiffView.SyncScroll` and
`ThreeWayView.SyncScroll` copy vertical AND horizontal offsets. Horizontal was deliberately left
independent for a long time, on the argument that dragging one pane sideways because the other has a
long line is disorienting - which is true only of a pane nobody is reading. The rows are ALIGNED, so
row `i` is the same change on both sides, and scrolling right to reach the end of a long line pushed
its counterpart off screen at exactly the moment it was the thing being compared. Two columns that
have to be dragged sideways separately to read one difference is the worse of the two problems, and
the three-way window makes it worse again by having three of them. Two things this rests on and one it
must not break. It is safe because the side-by-side panes never wrap (see below), so a horizontal
offset means the same thing in both; and the write clamps to the target's own extent, so a short line
simply stops at its end rather than the pair jamming. **The two axes are written through different
objects and that is not tidiness debt**: vertical goes through `TextEditor.ScrollToVerticalOffset`,
horizontal through `EditorScroll.ScrollHorizontallyTo`, because `TextEditor.ScrollToHorizontalOffset`
looks like the obvious counterpart and silently does nothing - AvaloniaEdit's `TextView` is an
`ILogicalScrollable` that scrolls itself, so the ScrollViewer in the editor's template never moves
(its `Offset.X` reads 0.0 on a pane visibly scrolled to 809.8) and writing to it changes nothing
visible. Found with temporary logging, not by reading docs, and the first guess - that the target's
extent was too narrow - was wrong: it measured 1270 against a 450 viewport, so the offset was always
reachable. The thing it can break is the
re-entry guard: syncing a second axis doubles the ways pane A can move pane B which moves A back
forever, and `_syncingScroll` is ONE bool covering both axes for that reason. A ping-pong there hangs
the UI thread rather than producing a wrong value, so nothing else would catch it - `ScrollSyncTests`
exists mainly to prove the guard still holds.

**Word wrap belongs to the unified view and CANNOT be given to the side-by-side one** (Diff). The two
columns are aligned by having the same number of visual lines, which is what makes scroll sync a plain
offset copy; a line long enough to wrap on one side and not the other pulls them apart by a line for
every wrap above the viewport, silently and with nothing to throw. `DiffEditorPane.WordWrap` exists as
a property but is bound only from `UnifiedView`, and the toolbar Wrap toggle is hidden outside that
view rather than disabled, per the hide-don't-disable rule. `WordWrapTests` pins that the side-by-side panes
stay unwrapped whatever the setting says. Do not "finish the feature" by binding it in `DiffView`.

**`EditorScroll.CenterOnLine` must ask the editor where a line IS, not multiply by line height** (Diff).
It used to compute `(line - 1) * DefaultLineHeight`, which is only right when every document line is
exactly one visual line tall - and neither view it serves is in that state: collapsing is on by default
(a fold above the target removes its rows from the visual height) and the unified view can wrap. It
now uses `TextView.GetVisualTopByDocumentLine`. The failure is silent - the pane scrolls somewhere
plausible and simply does not centre the difference - so it will not announce itself if reintroduced.
The `ScrollToLine` call before it is separate and still required; see the gotcha below.

**Collapsing is a VIEW state, and folding must never remove a row** (Diff). `CollapsedRegions` returns
ROW ranges and `DiffEditorPane` turns them into AvaloniaEdit folds, so the document still contains
every line and editor line `i` is still `DiffResult.Lines[i]`. Filtering rows out of the document
instead would look equivalent and would break the diff map, navigation, the gutter and the merge at
once. Both panes are handed the SAME list, which is what keeps them aligned - identical folds over
documents that already have identical row counts means identical visual lines, so scroll sync stays an
offset copy. Two smaller rules: an ignored row is not collapsible (its faint band is the only evidence
an ignore rule is doing anything, and folding it hides exactly what the user added the rule to check),
and folds are applied AFTER the document text, because a fold is a pair of offsets and the previous
comparison's offsets mean nothing in this one.

**"Take both" is the one merge resolution decided per REGION, not per row** (Diff). Every other choice
picks a side, which is a per-row question; both has to emit one side's whole block and then the
other's, so `ThreeWayMergedDocument.Build` skips the row walk past that region. Resolving it row-wise
would interleave the two blocks - `void L() { void R() { l(); r(); ...` - which is never what anyone
means by keeping both.

**Sliding a change group is a PRESENTATION pass, and its safety comes from one rule** (Diff).
`ChangeGroupSlider` moves a run of added or removed lines to the placement that reads best, and it is
allowed to because the diff is genuinely AMBIGUOUS there: when a group is bounded by lines identical to
the ones just inside it, several placements describe the same two documents and every one is equally
minimal, so the aligner had no grounds to prefer one. It only ever moves a group across a line
IDENTICAL (under the comparison keys) to the one leaving it, which is what makes both documents, the
counts and the hunk count provably unchanged - only the pairing of equal lines moves. Two things follow.
It runs BEFORE projection, so it compares keys rather than display text (with "ignore case" on, two
lines the user can see differ were matched as equal, and the slider has to agree with the diff that
already made that call) - but it SCORES on the display lines, because indentation is the whole signal
and trimming it is exactly what a key may have done. And an ignored row is deliberately not slideable
context: it is drawn faintly precisely so the reader can see where it is.

**A move mark is PER SIDE, and that is not a detail** (Diff). `DiffLine` carries `LeftMoveId` and
`RightMoveId`, not one `MoveId`, because the obvious case is only half of what people do. A block that
travels far enough to have no counterpart gives a deleted run and an inserted run, and matching whole
ROWS finds it. Two methods of similar shape SWAPPING gives neither: the aligner pairs `void Helper()`
against `void Run()` and calls the row modified, which is what it is to a line differ - so that row's
left text moved down and its right text moved up, two different blocks on one row, and a single flag
could only describe one of them. Everything downstream asks per side (`DiffLine.IsMovedOn(side)`,
`AlignedLine.IsMoved`), including `UnifiedText`, which is the one place both halves become separate
lines. `MoveDetector` was first written whole-row and the swap case - the one users hit most - was
silently invisible; the end-to-end test that caught it is `MoveComparisonTests`.

**Move detection only ADDS a mark - kinds, counts, hunks and the patch are untouched** (Diff). Same
reasoning as `IsIgnored` and `IsConflict`, and the reverse of the trap: a moved row is genuinely
deleted or modified on disk, so promoting it to a `ChangeKind` or deducting it from the counts would
make the patch, the merge and F7/F8 disagree with what is actually in the files. `DiffResult.Moved`
counts BLOCKS alongside the row counts rather than instead of them. Three rules in `MoveDetector` are
load-bearing and were each found by a failing realistic test, not by design: runs break on a change of
KIND as well as on unchanged context (an ordinary edit sitting against a moved block otherwise fuses
into one run that matches nothing); blank lines at a run's ENDS are trimmed before matching (a method
takes its neighbouring blank line with it, and ends up with it below in the file it left and above in
the one it arrived in - interior blanks are kept, they are part of the block's shape); and a pairing is
made only when the text occurs EXACTLY ONCE on each side, so a run of `}` is never matched with an
unrelated one. That last rule is the whole reason the feature is usable: a mark that tells the reader
"you can skip this" is worse than nothing when it is wrong. `FileComparisonService` also skips inline
spans on a moved row - the aligner's pairing was positional, the two lines are not counterparts, and
highlighting the letters between them invites reading a change nobody made.

**Do not reach for a cleverer alignment algorithm before measuring** (Diff). Patience diffing was built
here, behind `IDiffEngine`, decorating `DiffPlexDiffEngine` - and then removed, because on measurement
it produced an answer identical to DiffPlex's on every realistic C#/TS/JSON case tried (a method
inserted between two others, a nested block added, a switch case added, an appended arrow function, a
JSON object appended), and on the one case where it differed - a moved method - it was not better,
merely differently shredded. DiffPlex is not a naive LCS and does not have the brace-matching failure
patience is famous for fixing. The thing that DID fix the moved-method case is the slider above, which
is a post-pass over ANY aligner's output. If a diff reads badly, check whether the alignment is wrong
or merely badly PLACED before replacing the engine - they need different fixes, and only one of them
was actually the problem here.

**Comment stripping produces a KEY, and keys are not display text** (Diff). The same rule as the
normalizer, and the one most likely to be broken by a "helpful" change: with "ignore comments" on,
`CodeLines.ComparisonLines` is the document with its comments removed, and `FileComparisonService`
projects the real lines back over every row before anyone sees them. The stripping also takes the
whitespace immediately BEFORE a comment - without that, `foo(); // note` reduces to `foo(); ` and still
fails to match the same line written without the comment, which is the entire point of the option.

**Scanning a line for comments needs the whole document** (Diff). `SourceScanner.Scan` threads state
across lines because a line cannot be classified on its own: the middle of a `/* … */`, of a C#
verbatim string or of a JS template literal reads as ordinary code in isolation. `ScanLine`'s
single-line overload exists ONLY for the inline differ, which is handed two already-matched lines with
no document around them and where being wrong costs a slightly worse highlight rather than a wrong
answer. Anything deciding what a line MEANS must use `Scan`.

**Highlighting is keyed by file EXTENSION, comparison by `SourceLanguage`, and they know different
amounts** (Diff). The scanner claims a language only where it has real rules (C#, JS, TS); TextMate
colours anything it ships a grammar for. A Python file gets nothing from the code rules and is still
far easier to read coloured, so `DiffEditorPane.SyntaxExtension` takes an extension rather than a
language. Tying them together would mean either colouring nothing outside the short list or claiming to
compare languages we cannot scan. `DiffEditorPane` installs TextMate on FIRST USE, not in its
constructor, and swallows grammar failures - highlighting is a reading aid layered over the thing the
user actually opened the app for, so it must degrade to plain text rather than take the pane down. That
silence is why `Fubar.Diff.Controls.Tests` asserts the grammars resolve at all: a missing one would
look exactly like a `.log` file, forever.

**An ignored row is `Unchanged` + `IsIgnored`, never its own `ChangeKind`** (Diff). That is what keeps
it out of `IsChange`, and therefore out of hunks, counts, the diff map and F7/F8 — while still letting
a renderer draw a faint band. Promoting it to a `ChangeKind` would silently put every ignored row back
into the hunk list and make navigation stop on the fields the user asked not to see.
`IgnoredRowNavigationTests` pins this.

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

**A format-only difference is a REAL difference, and the lines cannot show it** (Diff). The reader
consumes the BOM as a preamble and splits on every terminator, so a UTF-8-with-BOM file and its
BOM-less twin - or CRLF vs LF, or UTF-16 vs UTF-8 - produce byte-identical `Lines` and an empty
`DiffResult`. `TextFormat` captured all of this from the start but nothing ever COMPARED two of them,
so the tool said "the files are identical" about files that were not, which is the worst possible
answer right after someone's version control told them otherwise. `TextFormatComparer` (Core) decides
it, `FileComparison.FormatDifference` carries it, and the UI reports it in both the status line and a
banner - the banner matters because when it is the ONLY difference there is nothing else on screen to
notice. Do not fold this into `DiffResult.AreIdentical`: that is about content, and conflating the two
would make every hunk-counting consumer wrong.

**The comparison pipeline is fast; measure before "optimising" it** (Diff). A 60,000-line source
comparison takes about 90 ms end to end, of which ~65 ms is DiffPlex's own aligner and ~15 ms is
everything this codebase adds (scanner, code rules, slider, projection). The JSON path costs ~2 ms on
a file that is not JSON, which is where the obvious-looking waste is - four whole-document
`string.Join`s and two parse attempts - and it is not worth removing. `PipelineScaleTests` guards the
thing that WOULD matter: the budgets there are absurdly generous on purpose, because they exist to
catch an accidentally quadratic scan (60 ms becomes minutes) rather than a 20% regression, and a
timing assertion tight enough to catch the latter fails on a loaded CI agent instead.

**Ports live in Core, adapters in Infrastructure**, wired in each app's
`Infrastructure/ServiceCollectionExtensions.cs` and `UI/Composition.cs`.

## Conventions

- **Domain policy lives in Core, not ViewModels** — e.g. `HunkNavigator`, `MergedDocument`,
  `AuthApplier` — so the rules are testable without a UI.
- **MVVM** via CommunityToolkit.Mvvm source generators; `ViewModelBase : ObservableObject`.
- **Style classes bind as `Classes.name="{Binding Flag}"`** — Avalonia's `Classes` is not bindable, so
  view models expose a bool per class rather than a class-name string.
- **Generic UI belongs in `Fubar.Controls`** (with a Gallery page). Anything that knows a domain
  concept — what a hunk is, what a request is — stays app-side.
- **Central Package Management**: versions live in `Directory.Packages.props`; reference packages
  without a `Version`.
- **Keep it warning-clean**; analyzers are on repo-wide and CI builds + tests every push/PR.

## Gotchas

- **`Application` name collision**: the `Fubar.Studio.Application` / `Fubar.Diff.Application`
  namespaces shadow Avalonia's `Application` type inside `Fubar.*` code (a namespace member outranks a
  using-alias, so an alias cannot fix it). Qualify Avalonia's as **`Avalonia.Application`**.
- **Build fails with locked DLLs while an app is running** → `taskkill //F //IM FubarDiff.exe` (or
  `FubarAPIStudio.exe`) first.
- **A style not merged into `Themes/Fubar.Controls.axaml` does nothing** — the usual cause of "my
  control renders unstyled".
- **Viewport size must come from `TextView.DefaultLineHeight`, not `VisualLines.Count`** — a document
  shorter than the pane reports only the lines it drew, which collapses the diff map's scale.
- **Background renderers paint in registration order** (Diff). `CurrentHunkRenderer` is added *after*
  `ChangeLineBackgroundRenderer` on the same layer so the current-difference marker lands on top of
  the change tint. Swap the order and it disappears under it.
- **`DiffLineColors`' `DiffEmphasis` parameter is what makes the current difference read as CURRENT**
  (Diff). Three levels, not a bool: `Faded` for a real change that is not the one just navigated to
  (main panes only - a hunk outside `ChangeLineBackgroundRenderer`/`CharSpanColorizer`'s
  `SetCurrentRange`), `Normal` for the current hunk in the main panes, `Emphasized` for the two "Diff
  pane" close-ups (DiffDetailPane, JsonDetailPane - `DiffEditorPane.Emphasized` / `RawJsonPane.Emphasized`,
  `false` by default, `True` only in those two close-ups' own XAML). `LineBackground` is never called
  with `Emphasized` - `ChangeLineBackgroundRenderer.Draw` skips itself entirely when emphasized (see
  below) - so only `SpanBackground` actually has three meaningfully different levels; `LineBackground`
  only ever sees `Faded` or `Normal`.
- **With NOTHING selected, every change draws `Faded`** (Diff). Both `Emphasis` helpers
  (`ChangeLineBackgroundRenderer`, `CharSpanColorizer`) treat "outside the current range" and "there is
  no current range" the same way. It used to be the opposite - a negative range meant everything drew
  at `Normal` - which was survivable when only inserted/deleted rows were tinted, and became a wall of
  colour once every changed row got a background. A document nobody has navigated yet should read as
  one even wash saying "the changes are here", with nothing pretending to be the current one.
- **Every changed row gets a line tint, and a MODIFIED row takes the colour of its own side** (Diff).
  `LineBackground` returned null for `Modified` for a long time, on the argument that the row's
  character spans are more precise than a full-row wash - true, but it left the commonest kind of
  change with no row-level mark at all, so scanning for "which lines changed" worked for insertions
  and not for edits. Both now: the row says where (`LineOpacity`, 0.12/0.28), the span says what
  (0.30/0.55), and the gap between them is what keeps the span the louder of the two -
  `ChangeTintTests` pins that ordering. Which colour a modified row takes comes from
  `DiffEditorPane.Side` (removal colour on the left, addition colour on the right), NOT from the row's
  own spans: deriving it from `Spans[0].Kind` was tried and is wrong, because a line that only had text
  added to it has no deleted spans on the left, so half the modified rows in an ordinary diff fell
  through to the neutral fallback and came out a third colour. A pane that is neither side (the
  unified view, a three-way base column) leaves `Side` null and gets that fallback.
- **The two Diff pane close-ups have NO full-line tint at all, in either mode** (Diff). Text mode:
  `ChangeLineBackgroundRenderer.Draw` returns immediately when `_emphasized`, so `DiffLineColors.LineBackground`
  is dead code there regardless of `ChangeKind` - a close-up is a pane full of nothing BUT the current
  difference, where a band across its whole width says nothing the pane's own border does not.
  (The MAIN panes are the opposite case and tint every changed row - see above.) Since a whole
  inserted/deleted row normally
  carries NO character spans at all (the full-line tint used to say "this whole row is the diff" on its
  own - see `FileComparisonServiceTests.Only_modified_rows_get_inline_spans`), `CharSpanColorizer`
  synthesizes one covering the row's entire text when `_emphasized` and `Spans.Count == 0`, so the
  close-up still shows something. Json mode: `RawJsonPane.Emphasized` swaps `CurrentHunkRenderer` (a
  full-width band, still used by the MAIN Json panes) for `SpanTextColorizer`, which highlights only the
  exact characters a `SourceSpan` covers using its `StartColumn`/`EndColumn` - the first renderer to
  actually use those columns; every other consumer of `SourceSpan` in this codebase only reads the line
  range. Do not restore a full-line/full-width wash to either close-up "to make it easier to scan" -
  that is precisely what both changes were replacing with something more precise.
- **The Json panes mark EVERY change, not just the current one** (Diff). `JsonChangeSpanColorizer`
  paints each change's own `SourceSpan` faintly and the current one at full strength;
  `CurrentHunkRenderer` still bands and brackets the current change's lines on top. Character spans
  rather than full-width bands, unlike the aligned views: a Json document is unaligned and one line
  routinely holds several properties, so banding the line would claim the whole of
  `{"a": 1, "b": 2}` changed when only `b` did. It must be fed `DiffPaneViewModel.SemanticChanges` -
  the list whose spans address each side's RAW text - never the canonicalized list, which is a line or
  two out as soon as "Reformat for display" is on. The close-up (`JsonDetailPane`) passes no changes at
  all: it shows an excerpt renumbered from line 1, so whole-document spans would land on whatever text
  happened to sit at those numbers.
- **The same executable is a window AND a batch tool, and only unambiguous flags choose the second**
  (Diff). `CommandLine.IsHeadless` is checked in `Program.Main` before Avalonia is configured, because
  a run that must exit with a status code cannot also be showing a window. The list is deliberately
  short - `--check`, `--quiet`/`-q`, `--report`, `--report-format`, `--help`, `--version` - and two
  bare file names or `--merge` are NOT on it: those are what `git difftool` and `git mergetool` pass,
  and turning one into a silent batch job would break every git integration with no error to go on.
  Exit codes are `diff`'s (0 same, 1 different, 2 could not tell) and a format-only difference counts
  as different. On Windows a GUI executable has no console at all until `ParentConsole.Attach` runs.
- **`.fubardiff.json` is for facts about FILES, not preferences about reading** (Diff). Ignored paths,
  array keys, ignored patterns, the comparison mode - things that are true for the whole team and
  every checkout. The theme, auto-reload and the Pretty button's layout stay in `AppSettings`, which
  is per machine. It is applied in two places (`ComparisonViewModel.CurrentOptions` and `CliRunner`)
  rather than inside `FileComparisonService`, because the service is also entered by paths that carry
  options captured earlier (a re-diff after an edit) and applying it there would make the rules come
  and go. Composition rule: single values are overridden by the later rule, lists ADD - including to
  whatever the session already has. A broken config is reported and ignored, never fatal.
- **A user's alignment anchor is an instruction, not a hint** (Diff). `ComparisonOptions.Alignments`
  is honoured absolutely by `DiffPlexDiffEngine`, at any size, by splitting the documents there and
  aligning each region independently (`SegmentedLineAligner.AlignAround` - the same machinery as the
  large-file path, which finds its anchors instead of being given them). Two rules that look like
  details and are not: the anchored row is `Modified` unless the two lines are genuinely equal,
  because "these correspond" is not "these match" and marking a rewritten line unchanged would hide
  the difference the user was lining up to read; and anchors are dropped when a PATH changes, because
  they describe two particular files. `AlignmentAnchors.Add` resolves conflicts by dropping what the
  new anchor crosses - refusing it would leave the user hunting for a forgotten decision.
- **The cost of a big comparison is the ALIGNMENT, not the rendering** (Diff). Measured before
  guessing, and the guess was wrong: on a 1,000,000-line pair the pipeline took 15.8 s, of which 15.5
  was one call into the diff engine - reading, normalising, inline spans, building both aligned
  documents and computing folds came to under 800 ms between them. `SegmentedLineAligner` is the fix
  (trim the identical head and tail, split the rest at lines unique to both sides, align each piece),
  used only above `DiffPlexDiffEngine.SegmentedFrom` so ordinary comparisons keep byte-identical
  output. Before optimising anything here, measure - the scratch benchmark shape is in the commit that
  added this.
- **Never ask `SideBySideDiffBuilder` for an alignment** (Diff). It runs a WORD-level diff for every
  modified line to fill in sub-pieces this codebase does not read (character spans come from
  `DiffPlexInlineDiffEngine`, computed on display text rather than comparison keys). Two 1.8 MB
  minified documents took 68 seconds, essentially all of it inside a word diff whose output was
  discarded; going straight to `IDiffer.CreateDiffs` with a `LineChunker` and pairing the blocks up
  by hand is 13 ms. The pairing rule - first min(deleted, inserted) lines of a block become modified
  rows, the remainder one-sided - is the builder's own, and must stay that way.
- **Anything that walks one document's properties against the other's must not use `Find` naively**
  (Diff). It looks like an O(1) lookup and was a linear scan; every caller is inside a loop over the
  other side, so a 120,000-property minified document spent 45 SECONDS in `ArrayKeyScanner` alone,
  looking for arrays it never found. `JsonAstObject.Find` now indexes itself above
  `JsonAstObject.IndexFrom` properties (lazily, first-wins so duplicate names keep their documented
  meaning). `JsonSemanticDiffer.CompareObjects` builds its own dictionary and is fine.
- **`TextEditor.ScrollToVerticalOffset` silently clamps to the CURRENTLY KNOWN extent, not the whole
  document** (Diff). Calling it for a line AvaloniaEdit has never scrolled towards is a no-op - the
  ScrollViewer only learns the document is that tall once something (`ScrollToLine`) asks it to make
  that position visible first. `EditorScroll.CenterOnLine` calls `ScrollToLine` before
  `ScrollToVerticalOffset` for exactly this reason; dropping the first call silently breaks centering
  for any line far from wherever the pane last scrolled, with no exception and no warning - it just
  quietly stays put. Confirmed by adding temporary logging, not by reading docs; do the same before
  "simplifying" this away again.
- **Collapsing a `Grid` row needs its `RowDefinition` height zeroed**, not just `IsVisible=false` on
  the child — `DiffView`'s detail pane would otherwise leave a 190px blank band.
- **`git mergetool` passes `$BASE $LOCAL $REMOTE`, and LOCAL is the RIGHT-hand side** (Diff). LOCAL is
  "mine" - the file being merged into - which is the right-hand column by the convention the two-way
  window already set; REMOTE is "theirs" and goes left. `StartupFiles.FromArgs` therefore does NOT
  pass its arguments through in order, and the swap is invisible to any test whose left and right
  files are interchangeable — it shipped wrong once and a smoke test with a symmetric argument order
  did not notice. `StartupFilesTests` pins it with three distinguishable names.
- **An owned window cannot be shown before its owner is** (Diff). `Window.Show(owner)` throws "Cannot
  show window with non-visible owner" from `OnFrameworkInitializationCompleted`, where `MainWindow` has
  been constructed but not yet displayed — which is exactly where opening `--merge`'s window belongs.
  `App` defers it to the main window's `Opened` event for this reason; the exception is immediate and
  fatal, not a silent misbehaviour, so it will find you.
- **Settings never throw**: `Load` returns defaults, `SaveAsync` returns false. Losing a preference is
  a nuisance; refusing to start over a corrupt settings file is not acceptable.
- **`ExecutionSnapshot.ResponseBody` is optional and must stay that way** — null for an empty body, one
  over `HistoryBodyPolicy`'s cap, and every ledger written before the field existed. Anything reading
  it needs the disabled path, not a `!`. The cap is why: 200 entries per request times an unbounded
  response would turn a workspace into a cache nobody asked for.
- **The pinned response (`IResponseBaselineService`) is in-memory only.** It is a scratch comparison;
  persisting response bodies outside the workspace's own history would put whatever they contain
  somewhere the user did not choose. It is a singleton so it survives switching request — which is
  also why panes must unsubscribe from it on dispose.
- **Avalonia 12 renamed drag-drop types**: `DragEventArgs.Data` is now `DataTransfer`, typed
  `IDataTransfer`, with files via `TryGetFiles()`.
- **Test both theme variants.** A token defined only in Dark throws at runtime in Light.
- **A control that cannot do anything right now should be HIDDEN, not disabled** (both apps). The
  codebase argued this for one control ("Recent is hidden rather than disabled when empty: an
  always-greyed control on first run is just clutter") and it is now the general rule: Fubar Diff's
  merge group binds `IsVisible` to `Pane.HasCurrentHunk` and its save group to `HasUnsavedMerge`, so
  neither occupies the toolbar during the many sessions that are only ever a read. `MergeWindow`'s
  three file pickers collapse to a one-line summary after a successful merge
  (`MergeViewModel.IsFileRowExpanded`) for the same reason - the comparison window went further and
  removed its picker row outright (see below). Do not "restore" these to
  always-visible-but-disabled - the row they cost is a row of diff, which is the thing the app exists
  to show.
- **The "Reformat" checkbox (`NormalizeStructure`) used to be labeled "Normalize XML" and hidden
  whenever `Pane.IsSemantic` was true** - i.e. hidden exactly for JSON, the one format users most want
  to reformat. It backs both XML and JSON already (`TextLineNormalizer.Canonicalize`); the bug was
  purely the toolbar's `IsVisible` binding. It is now unconditionally visible, like "Ignore whitespace"
  and "Ignore case" - it is a no-op on content that is neither, which is fine. It lives in
  `SettingsWindow` rather than the toolbar now; the toolbar keeps only the options reached for
  mid-comparison.
- **An Avalonia type selector matches the EXACT type, so `Button.foo` does not style a `ToggleButton`**
  (Controls). `ToggleButton` derives from `Button`, and `Classes="toolbar-btn"` on one looked right in
  the XAML and rendered as a stock Fluent button on screen - which is how the Json view's Pretty toggle
  came to look unlike everything around it. `ButtonStyles.axaml` therefore spells out
  `ToggleButton.toolbar-btn` separately rather than reaching for `:is(Button)`, because the checked
  state needs somewhere to live anyway. Same trap for any future `RadioButton`/`SplitButton` class.
- **One `ControlHeight` for every button class** (Controls). `.toolbar-btn` / `.primary-btn` /
  `.secondary-btn` / `ToggleButton.toolbar-btn` all set `MinHeight` from it, with vertical padding
  deliberately smaller so the height decides the box. If a button in a row looks wrong, fix
  `ButtonStyles.axaml` - do NOT put `Height` on the instance, which is what the Gallery's blue button
  used to carry and what made the mismatch invisible in every diff.
- **Do not set `VerticalAlignment` in the shared button styles** (Controls). It was tried and reverted:
  API Studio's Send button is a `Panel` child that stretches to match the URL bar beside it, and
  `Center` shrank it to `MinHeight`. `MinHeight` alone gives an even toolbar row without taking that
  away.
- **F5 means two different things, and picking wrong loses work** (Diff).
  `ComparisonViewModel.RefreshDiffAsync` re-diffs what the PANES hold when there are unsaved edits, and
  re-reads both files from disk only when there are none. Reloading over typed text discards the only
  copy of it. `IsDiffStale` is the other half: set the moment a pane is edited, cleared when the
  re-diff lands, and shown in the status bar - do not "tidy it away" because it usually clears itself
  within a few hundred ms, since `LiveDiff` off is a supported mode where it stays up until F5.
- **`JsonView` brings its own Prev/Next strip, and Fubar Diff turns it off** (Diff).
  `JsonView.ShowToolbar` defaults to TRUE for API Studio, which embeds the view where there is no
  toolbar to put buttons in; the diff window sets it False and drives navigation from its own toolbar
  through `DiffPaneViewModel.NextDifferenceCommand`, which walks semantic changes in the Json view and
  hunks everywhere else. Do not "simplify" by deleting the strip - one host still needs it - and do
  not point a toolbar's Prev/Next at `NextChangeCommand` again: in the Json view that walks hunks
  nobody is looking at. The caption the strip carried lives in the status bar now, fed by
  `ComparisonViewModel` watching `JsonCaption` (guarded on `CurrentSemanticChange is not null`, or the
  "none selected" form raised by every load would overwrite the summary just written there).
- **Radio/check `MenuItem`s need their binding mode spelled out** (Diff). The View menu's
  `IsChecked` bindings read computed properties (`IsModeAuto`, `Pane.IsSideBySideViewVisible`), and a
  toggled MenuItem writes back to whatever it is bound to - so those are `Mode=OneWay` and the state
  is changed by the item's `Command` instead. The two genuine two-way ones (Diff pane, Wrap) say
  `Mode=TwoWay` for the opposite reason: do not rely on the default either way.
- **A settings row's explanation is a `Description`, not a tooltip** (both apps). `fc:SettingRow`
  exists for this: a header, a plain sentence under it, the control on the right. The Diff settings
  window was a column of terse labels whose meaning lived entirely in `ToolTip.Tip`, which is where an
  explanation goes to be missed. Tooltips are for the second-order detail, not for what the option
  does. Keep new rows in that shape, and keep the sentence short and in the user's words.
- **`ExtendClientAreaToDecorationsHint` means `Window.Title` must be empty** (both apps). Both main
  windows draw their own tab strip into the native title-bar row; a non-empty Title has the OS paint
  its own text over the first tab. Both also snap `WindowState.FullScreen` back to `Maximized` in
  `OnPropertyChanged`, because this Avalonia version draws a full-screen caption button that cannot be
  removed or hidden (it lives outside the window's visual tree).

## Workflow notes

- Commit/push only when asked; branch off `main` first if needed.
- The design docs in `docs/` (LeftPane / RequestEditorPane / ResponsePane) are the canonical behaviour
  spec for API Studio's panes.

**A hand-written `InitializeComponent` silently breaks every `x:Name` in the file** (both apps). The
XAML compiler generates one that assigns the named fields; writing
`private void InitializeComponent() => AvaloniaXamlLoader.Load(this);` overrides it, the fields stay
null, and the failure is a NullReferenceException in the constructor. `OpenComparisonWindow` did this
and its drop targets were null - and because the caller was an `async void` click handler, the
exception took the whole PROCESS down rather than showing an error. Every other window in this
codebase relies on the generated one; new ones must too. A headless test that merely CONSTRUCTS a
window catches it, which is why `OpenComparisonTests` has one that does nothing else.

**"Built but never wired" is a recurring failure here, and it has now happened twice** (both apps).
`WorkspaceExplorerViewModel.NewWorkspaceAsync` existed, worked, and was bound to NOTHING - so API
Studio could be installed and then not started, because the only route in demanded an existing
`fubar.json`. That is the same shape as the Diff options that were fully built in Core with
persistence fields waiting while `ComparisonViewModel` never read them. Before concluding a feature is
missing, grep for it; before concluding one is DONE, grep for a binding to it. A command with no
`Command="{Binding …}"` anywhere in a `.axaml` is dead code that looks alive.
