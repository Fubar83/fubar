# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **A location map worth reading, where there was a strip of ticks.** The map between the panes now
  shows *how much* changed at each point, not just where. It aggregates per pixel rather than per hunk,
  which matters exactly where a map earns its place: on a 60,000-line file drawn 600px tall, one pixel is
  a hundred rows, and the old drawing clamped every hunk to the same minimum height — so forty changes in
  a rewritten region looked identical to one stray edit beside it.

  Marks are now per side, so a deletion shows on the left and an insertion on the right without relying
  on colour alone. **Ignored rows are marked** — they form no hunk, so they used to draw nothing at all,
  leaving you unable to tell "these are identical" from "a rule is hiding this", which is exactly what
  you want to check after adding a rule. Small triangles at the top and bottom say when changes lie off
  screen that way, and hovering names what is under the pointer — "line 4,120 of 60,000 · change 12 of
  40 · 11 above, 28 below the view".

  It deliberately does **not** copy WinMerge's connecting lines between its two columns. Those exist
  because its columns are at independent scales; ours are row-aligned by construction, so a line would
  join a point to itself. The one case where the two ends genuinely sit at different heights is a
  **move**, and that is the one case a line is drawn for.

- **The location map is easier to hit, easier to read, and says which difference you are on.** Wider
  (32px), so it reads as the strip *between* the two panes rather than a border on one of them. Marks are
  drawn 3px tall instead of 1px, so a single changed line on a long file is visible rather than a hair —
  density is still carried by width, so nothing about "how much changed here" is lost.

  **Clicking near a change snaps to it.** On a 60,000-line file one pixel is a hundred rows, so hitting a
  one-line change used to be luck — and missing by a pixel scrolled a hundred lines away from the thing
  you aimed at. A click within a few pixels of a mark now goes to the start of that change. Further away
  it still goes exactly where you pointed, so dragging the strip keeps scrubbing smoothly.

  **The current difference is washed across the full width** with bars down both edges, in the same
  orange the editors use — so the map and the panes agree about which one you are on. The wash is drawn
  *under* the marks, which stay the brightest thing on their own row.

- **Navigating to a difference now scrolls sideways to it.** A change beyond the right edge of a long
  line used to leave you looking at a row that appeared unchanged. Each pane is scrolled by the minimum
  needed to bring *its own* changed characters into view, with a margin so they do not sit flush against
  an edge — the minimum, because horizontal position carries meaning and a pane yanked sideways on every
  step loses indentation as a cue. A whole inserted or deleted line has no columns to point at, so it
  scrolls back to the left margin, which is where such a change starts.

- **Ignored differences are visible now, and there are more of them.** Two changes, one principle: a
  difference you told the tool to ignore is still *shown*, faintly — because told nothing at all you
  cannot tell "these lines agree" from "these lines disagree and I asked not to be told", and the second
  is worth a glance before trusting the diff. It is also the only way to check that a rule you just added
  is doing what you thought.

  **Whitespace, case, comments and line-pattern rules now leave a mark.** Turning one on used to make the
  affected lines vanish into ordinary unchanged rows. They now carry the same faint neutral band an
  ignored JSON path already had, and stay out of the counts, the hunks and next/previous exactly as
  before. One implementation covers every option that equalises a line, because it compares the two raw
  lines after projection rather than knowing which rule ran.

  **And the band is no longer nearly invisible** — raised from 7% to 14%, with a stronger 30% for
  character spans in the Json view, where the same opacity over a few characters reads as far less than
  over a whole row. It stays below an ordinary change row and stays neutral grey against their red and
  green, so it is told apart by hue rather than only by weight.

- **Ignored differences are shown, faintly, and an ignored REORDER is now one of them.** Picking *Ignore
  order* on a list used to make the moved elements vanish outright. That is the wrong kind of silence:
  you asked for the order to be ignored, not for the fact that something moved to be erased, and told
  nothing at all you cannot tell "these agree here" from "these disagree here and I said not to mention
  it" — which is worth a glance before trusting the diff. A moved element now leaves the same faint 7%
  wash an ignored path already leaves: visible, not counted, skipped by next/previous, and labelled
  *moved* in the change tree. Only the elements that actually moved are marked; marking every element of
  a reordered list would turn a hint into a wash.

- **Lists whose order does not matter.** Right-click a list in the change tree → *Compare this list* →
  **Ignore order**, or add its path under Settings → JSON, or commit it to `.fubardiff.json` as
  `unorderedArrays`.

  This is the shape the array menu had no answer for. Matching by an identity field only works for
  objects carrying an id; an array of **strings** — a set of tags, roles, feature flags, enabled
  locales — has no field to be keyed by, so it always fell through to positional comparison and
  `["A","B"]` against `["B","A"]` reported two modifications for a document nobody had edited. Elements
  are now matched on their whole value instead, which needs no field and works for scalars, objects with
  nothing to key on, and nested arrays alike.

  It is a **multiset**, not a set: `["A","A","B"]` against `["A","B"]` has genuinely lost an element, and
  calling those equal is the one answer a comparison must never give. Elements left over after the exact
  matches are paired up rather than reported as a pile of deletions and insertions, so one element that
  changed in one field is still **one** change and still says which field — and your ignore rules still
  reach inside it. Opting a list out of ordering says nothing about lists nested inside it; those get
  their own rule.

### Changed

- **The panes now scroll in lockstep horizontally as well as vertically**, in both the side-by-side
  comparison and all three columns of a three-way merge. Horizontal was deliberately left independent
  before, on the argument that dragging one pane sideways because the other has a long line is
  disorienting - which is true only of a pane nobody is reading. The rows are aligned, so row *i* is
  the same change on both sides, and scrolling right to reach the end of a long line pushed its
  counterpart off screen at exactly the moment it was the thing being compared. Having to drag two
  columns sideways separately to read one difference was the worse of the two problems, and a merge
  made it worse again with three.

### Added

- **An open dialog, and drop zones that take folders.** Opening a comparison was a bare file picker,
  which can only answer one of the four questions the act actually involves: which two things, whether
  they are files or folders, which way round they go, and under what rules. The other three were
  discovered afterwards - by comparing the wrong pair with the wrong options and starting again.

  Ctrl+O now opens a dialog with all four on screen. Two sides that each take a file OR a folder, drag
  and drop onto whichever half you mean, a swap button between them, the comparison rules seeded from
  your saved settings and overridable for this one comparison, and the recent list. Each side reports
  what it made of the path - *File*, *Folder*, *Not found* - so a typo shows before Compare rather
  than after.

  Dropping two files at once fills both sides whichever half they landed on, because dragging a pair
  out of a file manager is the fastest way in and making someone aim first would throw that away. A
  single folder with the other side empty opens the one-folder linked comparison, which is the shape a
  snapshot review already has. A file against a folder is refused with a reason rather than by a
  greyed-out button with no explanation.

  What a pair of paths MEANS lives in `ComparisonTargets` in Core, not in the window: the same
  question decides whether Compare is enabled and what it opens, and two answers that could disagree
  is how a button ends up enabled for something that then fails. Its symmetry is pinned by a test,
  because the swap button would otherwise be able to change the answer.

- **Structural C# comparison: what changed, member by member.** Every diff tool in existence compares
  two C# files as lines of text, which means none of them can tell a reformatted method from a
  rewritten one - both are a block of red beside a block of green. A file someone ran a formatter
  over, reordered three methods in and rewrapped the comments of produces hundreds of changed lines
  and looks exactly like a file with a bug fixed in it, and the only way to tell them apart is to read
  every hunk.

  Both sides are now parsed with Roslyn (syntax only - there is no project around two files on a disk,
  and none is needed) and matched member to member, producing a panel beside the diff: *`Total` -
  method · changed and moved*, *`Render → Draw` - method · renamed*, *`Add` - method · reformatted*.
  Clicking a row scrolls both panes to it, so a large file is navigated by meaning rather than by
  pressing Next through fourteen hunks.

  The headline is the point: **"No functional changes."** Said in the panel, in the status bar, in
  every report, and as an exit code - `--functional` exits 0 when the only differences are formatting,
  comments and declaration order. That flag is deliberately separate from `--check`: the default has
  to stay "do these bytes differ", and it only answers where the structural pass actually ran, because
  a check that passed because the tool could not read the language would be the same lie as one that
  passed on a changed file.

  Members are matched by signature, then by name, then by an identical body - which is what turns a
  rename into one change rather than an unrelated deletion and insertion, and a field promoted to a
  property into one change rather than two. A rename is only claimed when the body occurs exactly once
  on each side, the same bar `MoveDetector` holds itself to. Move detection runs a
  longest-increasing-subsequence, so inserting one method at the top of a file does not mark every
  method below it as moved.

  It changes NOTHING about the text diff beside it, and that is a rule rather than an omission: a
  reformatted C# file genuinely differs on disk, a review is about those bytes, and a tool that
  quietly reported the two as identical would be lying about what it was shown. The line diff keeps
  saying exactly what changed; this says what it meant.

  Roslyn is confined to `Fubar.Diff.Infrastructure` behind `ICodeStructureParser`, enforced by the
  architecture tests - the differ, the summary, the panel and the CLI all work on a language-neutral
  tree, so a second language is one adapter rather than a change to any of them.

- **The merged result of a three-way merge is now a pane you can type into.** A merge tool whose only
  verbs are "take left" and "take right" is a tool for choosing between two answers, and the answer to
  a real conflict is regularly neither: two people edited the same line, and what belongs there is a
  third line that exists in neither file. Until now the way through that was to resolve the conflict
  badly, save, and fix it in an editor afterwards - which is exactly the moment a merge tool is meant
  to save you from.

  A **Result** pane sits under the three columns, showing the merged file as it currently stands and
  rewritten on every decision, so it is a live result rather than a preview someone has to remember to
  refresh. It is editable; the three input columns are not, deliberately, because editing an input
  needs a full re-merge and a re-merge renumbers the regions every decision is keyed by - see the
  Roadmap note in `docs/diff.md`, half of which this closes and half of which still stands.

  Once it has been typed into, the decisions and the document disagree, and the document is the one
  that is right: it is what the user is looking at and what they mean to save. `SaveThreeWayTextAsync`
  writes those lines directly, taking only the path and the file format from the destination, so a
  hand-finished merge is written with the same encoding and line endings as one that was only clicked
  through. The status bar says *Hand-edited* while that is true.

  Resolving a region after a hand edit would rebuild the document and throw the typing away, so it
  asks - and *Keep my edits* is first, which makes it the primary button and the answer a dismissed
  dialog gives. With no confirmation service wired up at all the resolve buttons stop working rather
  than the work being lost, and the status line says why. The pane tells its own writes apart from the
  user's, which is what stops a decision's rewrite being read back as a hand edit.

- **Rules that travel with the repository: `.fubardiff.json`.** The interesting rules in this app are
  facts about particular files - "the requestId in our snapshots changes every run", "our users array
  is keyed by id", "the generated client is minified, compare it as text". Every one of those is true
  for the whole team and for every checkout, and until now each person had to discover it and set it
  up again by hand, on every machine. Beyond Compare's rules are per-machine for the same reason:
  nobody thought to make them travel.

  A config is found by walking up from the file being compared, the way .editorconfig and .gitignore
  are, and the nearest one wins outright - "the file you are looking at is the file that applies" is
  the simpler promise than merging a chain of them. Defaults apply to everything; rules name a file
  pattern and are laid over them in order.

  Single-value settings (the mode, ignore whitespace) are overridden by a later rule, because there
  is one answer to "how should this be compared". Lists (ignored paths, ignored patterns, array keys)
  ADD, because two rules each naming a field to ignore both meant it - and they add to whatever the
  session already has, since a path the user ignored for this comparison and a path the repository
  says is never worth reporting are both true at once.

  It applies in the window and in `--check`, which is where it matters most: a CI gate should not have
  to pass the same six flags every pipeline. Comments and trailing commas are allowed, because it is a
  file people edit by hand. A broken one is reported and then ignored - refusing to compare two files
  because a rules file has a typo would be the wrong trade every time, but so would letting it fail
  silently. A rule with no `files` pattern is dropped rather than applied to everything, which is what
  a typo in that key would otherwise do.

  What is deliberately NOT in it: the theme, whether to reload on change, how the Pretty button lays a
  document out. Those are true of the reader, not of the files, and belong to the machine.

- **Semantic YAML.** A `.yaml` or `.yml` file is compared as structure, not as lines: a manifest whose
  keys were reordered between two branches reports the two values that changed rather than the whole
  file. Multi-document files compare document by document, lists of objects are matched by an identity
  field, ignore paths work, the change tree works, the highlighting works, and `--check` works — all of
  it inherited rather than written, because YAML is parsed into the same AST as JSON. The new code is
  one parser and a rule for choosing it.

  That rule is the interesting part. JSON is recognised by TRYING to parse it, since almost nothing
  else is valid JSON. YAML cannot be recognised that way at all — a plain English sentence is a valid
  YAML document, and so is a log file — so it is taken from the file's name and never guessed at.
  Sniffing would have turned every text comparison in the app into a comparison of two one-scalar
  documents with nothing to report. `--mode yaml`, or View → Compare as → Yaml, is there for the
  manifest that came out of a pipeline with no extension.

  Two scalar rules are deliberate. `port: 8080` and `port: "8080"` are a number and a string and are
  reported as different — it is the change most likely to break something, and a resolver that treated
  them alike would hide it. And `country: NO` stays the string somebody wrote, rather than becoming
  `false` the way YAML 1.1 would have it.

  What does not survive the parse is comments, because they are not part of YAML's data model. A
  comparison that differs only in comments is a Text-mode question, and the fallback says so.

  The format is tracked per SIDE, which costs nothing and means a JSON config can be compared against
  its YAML translation — YAML being a superset, both land in the same tree. The Pretty button is
  offered only for a JSON pair: it re-lays-out JSON and there is no YAML emitter behind it, and a
  control that quietly does nothing is worse than one that is not there.

- **Manual alignment.** Put the caret on a line in each pane, press **Ctrl+Shift+A**, and those two
  lines are paired — the regions either side of the pairing are then compared independently of each
  other.

  This closes the one gap nothing else in the app could. Every option here changes what *counts* as a
  difference; not one of them changes which lines *correspond*, and when an aligner pairs the wrong
  two — a rewritten block, a reordered config, a generated file whose boilerplate matches everywhere —
  the user has no move to make. Now they do.

  A pairing is honoured absolutely, at any file size, but it is not a claim that the two lines match:
  a rewritten line still reads as changed, because calling it unchanged would hide the very
  difference someone was lining up to read. A second pairing that would need the lines between them to
  run backwards replaces the first rather than being refused — the newest instruction is the one the
  user is looking at. They are dropped when either file is replaced, and never persisted: "line 40
  here is line 62 there" means nothing about a different pair of files.

  It reuses the anchor splitting that made large files fast, which is the same idea arrived at from
  the other end: there, anchors are *found* and are a way of going faster; here they are *given* and
  are the answer.

- **A command line, with exit codes and reports.** The same executable now answers without opening
  anything: `FubarDiff --check expected.json actual.json` compares and exits 0 if they match, 1 if
  they differ, 2 if the question could not be answered. Those are `diff`'s codes, because a script
  author will assume them without reading anything — and the third matters most, since a file that
  could not be read must never come back as a clean result.

  `--report <file>` writes the comparison out, in a format taken from the extension: a self-contained
  HTML page for a build artifact (no scripts, no external anything, still readable years later), JSON
  for a gate to test, plain text for a log, or a unified diff. `--report -` writes to standard output
  and moves the summary line to standard error, so `--report - --report-format patch > changes.patch`
  produces a patch rather than a patch with a sentence on the end.

  Every comparison option has a flag, and `--ignore-path` may be repeated, which is what turns this
  into a usable CI check: *"is this response the same apart from the request id and the timestamp?"*
  is one line, with reordered keys already not counting as differences.

  Two file names still open a window, and so does `--merge` — those are what a difftool and a
  mergetool configuration pass, and quietly turning one of them into a batch job would break every git
  integration with no error to go on. Only flags that mean nothing on screen run headless. On Windows
  the process attaches to the console that launched it, so a GUI executable can still print.

- **F5 refreshes the comparison, and the window says when it needs to.** With something typed into a
  pane, F5 re-compares what the panes now hold; with nothing typed it re-reads both files from disk.
  The distinction is the whole command: going to disk over unsaved edits would discard text that exists
  nowhere else, and a refresh key may not do that.

  Between a keystroke and the re-diff behind it, every count, tint and hunk boundary on screen
  describes the file as it was *before* that keystroke. The status bar now says **Diff out of date**
  while that is true, with a Refresh button beside it. Ordinarily it flashes past, since the comparison
  re-runs by itself a moment after you stop typing — but on a pair big enough for that to be a stutter
  rather than a pause, **Settings → Re-compare as you type** turns the automatic run off, and the diff
  then waits for F5 and says so until you press it. Off is a real working mode rather than a
  degradation: a stale diff that admits it is stale is honest, where one that quietly is not is the
  failure the whole feature exists to prevent.

- **Swap sides** (Open ▾ → *Swap sides*). A multi-select dialog reports files in the platform's order,
  not the order they were clicked, so a pair can land the wrong way round through no fault of yours —
  and everything about a diff reverses with the sides. Refused while either side has unsaved edits,
  because swapping re-compares and that reloads both sides from disk.

- **Moved code is shown as moved.** A block that was reordered rather than rewritten is tinted blue on
  both sides instead of red here and green there, counted separately in the status line (`… 2 block(s)
  moved`), and drawn blue in the diff map so the map answers "how much is left to review" honestly on a
  reordered file. Nothing else changes: the rows keep their kinds, so the counts, the hunks, F7/F8, the
  merge and the patch all still describe what is genuinely on disk. The mark only says *why*.

  Marks are per side, which is what makes it work on real edits rather than only on the textbook case.
  Moving a method far enough that it has no counterpart gives a deleted block and an inserted block, and
  matching those is easy. Swapping two methods of similar shape gives neither — the aligner pairs
  `void Helper()` against `void Run()` and calls the row modified, because from a line differ's point of
  view that is what it is. Looking at each side's own text independently finds both, and a swapped row
  is correctly marked as two different blocks: its left text moved down, its right text moved up.

  Two blocks are paired only when their text appears **exactly once** on each side. A run of `}` matches
  a hundred other runs of `}`, and drawing a confident line between two unrelated braces is worse than
  drawing none — ambiguity is reported as "not a move" rather than guessed at. Blank lines at the ends
  of a block are ignored when matching, since a method takes its neighbouring blank line with it and
  ends up with it below in the file it left and above in the file it arrived in. A moved row also loses
  its word-level highlights: the two lines the aligner paired turn out not to be counterparts, so
  highlighting the letters that differ between them would invite reading a change nobody made.

- **Json view: right-click an array to choose how its elements are matched.** *Match by position*, or
  by any field that could identify them — the auto-detected one first and labelled as suggested, then
  every other field that would actually work. A field that is missing from some element, or repeated
  across two, is not offered: it would silently fail to match and produce a diff that looks like data
  loss. *Match by another field…* accepts anything, including a dotted path like `meta.id` for
  identity that is nested.

  The choice is **per array**, which is the point — one document can hold a list of users, where order
  means nothing and matching by id is the only way to read a diff of it, beside a list of migration
  steps where order is the entire content.

- **Json view: an added or removed field now highlights its key as well as its value.** Previously
  only the value was coloured, leaving the key beside it looking untouched — the opposite of what
  happened. A value that merely *changed* still highlights the value alone, because the key really is
  unchanged.

- **Json view: a Pretty button on each document.** Re-lays-out that side for reading — the case being
  a minified file next to a formatted one. Per document rather than one button in the toolbar, since a
  single one could not say which side it meant. Settings for indent size or tabs, whether an object of
  only scalars stays on one line, and whether to sort keys. It changes nothing about what the
  comparison found and never touches the file, and every number is written back exactly as the author
  wrote it — `1.0` stays `1.0`.

- **Json view: the change tree is simpler to use.** Clicking anywhere along a row expands or collapses
  it, rather than only the chevron doing that while the rest merely selected.

- **The View selector no longer offers Json.** The Compare selector (Auto / Text / Json) already
  decides that, and having it in both places meant picking Text in one and Json in the other was a
  contradiction the app resolved behind your back. View now chooses between side-by-side and unified,
  and hides itself when the Json view is showing. To see JSON as two columns of text, set Compare to
  Text. Your side-by-side/unified preference now survives the next comparison, too.

- **Nothing is lost silently any more.** Closing a tab or the window with unsaved changes now asks —
  *Save and close*, *Close without saving*, or Cancel — and naming the files that would be lost rather
  than saying "you have unsaved changes". Cancelling, or dismissing the dialog, keeps the tab open:
  going away is never agreement to discard.

- **Both sides save independently.** Both panes are editable, so a session can leave two files to
  write. Each side has its own *Save* button, appearing only when that file has something unsaved, and
  Ctrl+S writes every side that changed. *Save as ▾* writes one side to a different file and
  deliberately leaves the compared file as unsaved as it was. Saving with nothing changed now writes
  nothing at all, rather than rewriting a file with its own content and moving its timestamp.

- **A file changing on disk under your edits is treated as a conflict.** It used to raise a passive
  banner; now it asks: *keep my changes*, *save mine over what changed*, or *reload and discard mine*.
  Keeping your work is first and is what a dismissed dialog gives. Nothing changes for the ordinary
  case — with no unsaved edits the comparison still refreshes silently — except that with auto-refresh
  **off** you are now told the files moved, where before nothing happened at all and you carried on
  reading a stale comparison with no sign of it.

- **Editable panes** (toolbar → *Edit*). Type directly into either side of a side-by-side comparison.
  The diff re-runs as you pause, so it stays live rather than going stale under you. Find gains
  **Replace** at the same time. Off by default and remembered — a diff tool is a reading tool most of
  the time, and a caret blinking in someone's source file is an invitation to change it by accident.

  **Take left / take right are now edits.** They rewrite the file there and then instead of recording a
  decision applied at save, which means the change is visible immediately, the difference disappears as
  you resolve it, and **Ctrl+Z takes it back** along with everything you typed. The *Reset* button is
  gone — there is nothing pending to reset, and a second way to undo one kind of change would only
  raise the question of which to reach for.

  The panes still show each file with blank filler rows so the two columns line up, so they are not
  the file's text — which is why this was read-only for so long. They now track those rows and hand
  back the file's own lines, which meant the diff map, the folds, the change tints and the
  lock-step scrolling all kept working exactly as they were. One rule does it: *a line belongs to the
  file unless it is empty and still a filler.* Typing into a filler is how you add a line where the
  other side already has one.

  Editing is offered only in the side-by-side view of a text comparison. The unified view has its own
  row numbering, the Json view shows each side unaligned, and a hex dump of a binary file is not text
  that can be written back.

- **Copy files between the two sides of a folder comparison.** Select a file — or a folder, meaning
  everything under it — and copy it to the other side. The button says what it would actually do
  (*Copy 3 files to the right, replacing 2*) rather than showing a bare arrow, and a confirmation names
  the paths and how many existing files would be replaced before anything is written.

  In one-folder mode this is how a snapshot is accepted: copying `Thing.received.json` leftwards writes
  it over `Thing.verified.json`, which is the action snapshot review was missing.

  **It copies and never deletes.** There is no "make this side match the other" — removing what the
  other side does not have is where a folder tool turns a mistake into lost work, and it is not
  offered. A copy that fails part-way stops rather than pressing on, and says which file it stopped at.

- **Binary and image comparison.** Opening two files that are not text used to produce
  *"it appears to be a binary file"* and nothing else. They are now compared as bytes: the status line
  says whether they are identical, how big each is and where they first differ, and the panes show a
  hex dump of each side with the differing rows tinted.

  The hex is an ordinary diff result, which is why it arrived with everything already working — scroll
  sync, the change tints, the diff map, F7/F8 between differing regions and collapse-unchanged all
  operate on it without knowing it is hex. Alignment is by byte offset, which is the only honest answer
  for binary content: matching a row of bytes against a similar-looking row elsewhere would invent a
  correspondence the format does not have.

  **Two images are shown as pictures**, side by side above their bytes, scaled to the same size with
  each one's real dimensions written underneath — because at equal display size a rescale and a redraw
  look identical. PNG, JPEG, GIF, BMP, WebP and ICO, recognised from the file's own signature rather
  than its extension, so a renamed file still shows. A picture that will not decode says so and leaves
  the hex view to answer the question instead.

  Merging is refused for binary comparisons, and the take-left/take-right controls do not appear: the
  hex on screen is a view of the bytes, not the file, and writing it back would destroy the file it
  came from.

- **Word wrap in the unified view** (toolbar → *Wrap lines*). Minified JSON, long log lines and wide
  string literals stop running off the right edge. Off by default and remembered.

  Unified only, and that is a constraint rather than an oversight: the two side-by-side panes are
  aligned by having the same number of visual lines — which is what makes their scroll sync a plain
  offset copy — and a line long enough to wrap on one side and not the other pulls them apart by a line
  for every wrap above the viewport. The unified view has one document and nothing to keep in step
  with. The checkbox is hidden in the other views rather than sitting there greyed out.

  Centring a difference now asks the editor where the line actually is instead of multiplying its
  number by the line height. That arithmetic was only ever right when every line is one row tall, which
  stopped being true when collapsing arrived — a fold above the target already threw it off, quietly.

- **Collapse unchanged context.** Long stretches both sides agree on fold behind a `42 unchanged lines`
  placeholder, keeping three lines either side of every change. A three-thousand-line file with two
  changes now reads as two changes instead of two screens of scrolling to find them. On by default, one
  click to expand any fold, and a toolbar toggle that is remembered. Both panes fold identically —
  which they do for free, since the folds are computed from row indices and both sides already have
  identical row counts, so scroll sync stays the plain offset copy it always was. The three-way merge
  gets it too, where it is worth more: most regions of a merge resolve themselves, so what you are
  hunting is the few that do not, through the same thousands of untouched lines. An ignored row is
  never folded away — its faint band is the only evidence that an ignore rule is doing anything.

- **Export as a patch** (toolbar → *Patch*). Copy to the clipboard or save as a unified diff — the
  format `git apply`, `patch` and every review tool already read. The point is that a comparison stops
  being something only this app can open: it can be pasted into a review, attached to an issue, or
  applied on another machine.

  Three lines of context around each change, and hunks whose context overlaps are merged into one —
  emitting them separately would describe the same lines twice and produce a patch that does not apply.
  Line ranges come from the files' own line numbers rather than from row counts, since filler rows have
  no number and counting would drift by one per insertion. The headers name the files, not your
  absolute paths. Verified by generating a patch and running `git apply` on it.

- **Snapshot review: one folder, linked by name.** Tick *One folder, linked by name* and the comparison
  pairs files against each other inside a single directory — `Thing.verified.json` against
  `Thing.received.json`. That is what Verify and ApprovalTests leave behind after a run, and reviewing
  it previously meant picking two files out of a folder by hand, one pair at a time, with the names
  differing by one word in the middle.

  The rules are markers, not globs — `.verified = .received` — because the shape is always the same:
  one name is the other with a word inserted before the extension, so removing it gives the key the two
  share and works for any file type without a rule per extension. `.approved`/`.received` and
  `.expected`/`.actual` ship too, and the list is editable.

  The counts are phrased for the job: a `.received` with no baseline is a **new** snapshot waiting to be
  accepted, and a `.verified` with nothing beside it is one **nothing produces any more** — which is how
  a dead test goes unnoticed. Files no rule matches are simply not in the answer; an ordinary source
  file next to some snapshots is not a difference.

  It reuses the folder comparison's own result shape with both roots set to the one folder, so the
  window, the filtering and opening a pair all work unchanged — the two halves differ by file name
  rather than by root, which is exactly what the per-side paths already carried.

- **Compare any two selected files.** A file renamed between two trees appears as one left-only row and
  one right-only row, neither openable on its own. Select both and the button opens them against each
  other, naming the pair it will open. The pairing is not guessed: a similarity heuristic would be
  wrong precisely on the cases that matter, so the user says so instead.

- **Folder comparison.** Two directory trees walked together and reported as a tree — changed, left
  only, right only — opened from **Folders…** in the toolbar. Double-click any changed pair and it opens
  as an ordinary comparison tab in the main window, which is the point: a folder comparison that could
  not open a file would be a listing, not a diff tool.

  Three defaults do most of the work of making it usable rather than merely correct. **Identical files
  are hidden**, because on two real checkouts they are most of the tree and the answer to "what
  differs" should not arrive buried inside them — the count of what is hidden stays in the status line,
  so nothing goes missing quietly. **`.git`, `bin`, `obj`, `node_modules`** and friends are excluded out
  of the box, editable, with `*` and `?` wildcards. And files are compared **by contents**: two files of
  the same length are routinely different, and reporting them identical is the one mistake that costs a
  comparison tool its credibility for good.

  An unreadable folder is skipped rather than fatal — any tree of size contains something the current
  user cannot open — but an unreadable *file* is reported as a difference, never as a match.

- **Reload when files change on disk.** Keep the diff open beside the editor doing the editing and it
  follows along. On by default, switchable in Settings → Appearance.

  It refuses in exactly one case: **unsaved merge decisions.** Those are keyed by hunk index, and a
  fresh comparison renumbers the hunks — so reloading would either discard the decisions or, worse,
  apply them to different changes. Instead a banner appears with a Reload button, and the choice stays
  the user's. Nothing about a file-system event should be able to lose work.

  Two details that decide whether this works in practice rather than in a demo: the watcher listens to
  the containing **directory**, because most editors save by writing a temporary file and renaming it
  over the target, and a watcher bound to the file itself stops seeing anything at that moment; and
  events are coalesced behind a short quiet period, because one save arrives as several. The app also
  recognises its own writes, so saving a merge does not report itself as an external change.

- **A unified (inline) view.** The whole comparison as one patch-style document — removals, then
  additions, with shared context between them — switchable from the **View** selector, which now offers
  *Side by side / Unified / Json* and no longer hides itself for non-JSON files. No second column, so it
  reads well on a narrow window, in a screenshot, and for anyone who spends their day in patches.

  It is a genuinely separate flattening rather than a mode on the existing one, because it cannot share
  the invariant everything else rests on: side by side guarantees editor line *i* is `DiffResult.Lines[i]`
  on both sides, and a unified document breaks that the moment one modified row becomes two lines. So it
  carries its own hunk ranges, its own folds and its own row mapping back to the comparison, and the
  side-by-side view keeps the guarantee it was built on.

  The close-up hides itself while the unified view is showing and comes back when you leave — there, the
  two versions of a change are already one line apart, so a close-up would be a copy of what is on
  screen. If you had turned it off yourself, it stays off.

- **Ignored text patterns** (Settings → Text compare). Regular expressions whose matches stop counting
  as differences — a build timestamp, a generated GUID, a version stamp in a header. Only the **match**
  is ignored rather than the whole line, so a real change elsewhere on the same line is still reported,
  which is the difference between this and simply filtering lines out. Your files are never altered:
  masking produces a comparison key, and the panes still show the timestamp.

  Two things a user-supplied regular expression can do that an application must survive, and does: a
  malformed one is rejected rather than thrown (Add stays disabled until it compiles, and a bad rule
  hand-edited into the settings file is dropped rather than stopping the comparison), and a
  catastrophically backtracking one cannot hang the window — patterns run on .NET's non-backtracking
  engine where possible, which is linear in the input, and on the ordinary engine with a timeout where
  the pattern needs lookaround or backreferences.

- **Java, Go, C, C++ and Python** join C#, JavaScript and TypeScript in the code-aware comparison, so
  "ignore comments" and token-level highlighting now cover most of what developers actually open. The
  scanner became rules-driven rather than a set of branches, so each language is a line of data plus
  its own tests: Java text blocks, Go's raw backtick strings (where a backslash is a backslash, not an
  escape — treating it as one would swallow the closing delimiter), Python's `#` comments, both kinds
  of triple-quoted string, and its `f`/`r`/`b`/`u` string prefixes. A Python docstring is treated as
  the string it is, not as a comment, so "ignore comments" cannot delete a real value.

  Rust is deliberately still absent: a lifetime (`&'a str`) is indistinguishable from an unterminated
  character literal without parsing, and its block comments nest. Both would make the scanner
  confidently wrong about where a string or comment ends, which is worse than treating the file as
  plain text.

- **Documented git integration.** `difftool` and `mergetool` config that works, in the README — the
  argument shapes already matched what git passes, but nothing said so.

- **Take both** in the three-way merge (Alt+B). The resolution a merge needs more often than any other
  and the only one that is not a choice between alternatives: two people added a different method at
  the same point, a different import, a different case to the same switch. Neither edit is wrong and
  the answer is both of them. Each side's block is kept whole and in order rather than interleaved,
  which is why this is the one resolution decided per region rather than per row. Without it the user
  had to take one side, save, and finish the job in a text editor.

- **Three-way merge.** Give it a common ancestor and two edits and it settles, on its own, every region
  only one side touched — plus every region both sides changed to the same thing, which is what a
  cherry-pick, a shared reformatting or a rebase over someone else's landed change looks like. What is
  left is the set that genuinely disagrees, and only that set is put to you.

  Three panes with the ancestor in the **middle**, because a conflict is read by comparing each edit
  against the ancestor, and putting it between them makes both comparisons a glance at the adjacent
  column. All three scroll as one. Conflicts are tinted amber — not the green and red the other rows
  already use, since every column of a conflict is an addition or a removal. Navigation stops only on
  conflicts by default (F7 / F8, Alt+Up / Alt+Down); resolving one with Take left / Take base / Take
  right moves straight to the next.

  Within a region, each edit highlights the characters it altered relative to the ancestor — so two
  conflicting versions of nearly the same line are told apart by the two words that differ rather than
  by reading both in full. A row that has an ancestor line opposite it gives up its full-row tint to
  those spans, the same bargain the two-way view already makes for a modified line; a row the ancestor
  has nothing opposite keeps the tint, since the whole row is the change.

  Saving writes the merged file to whichever of the three you pick, in that file's own encoding, line
  endings and trailing newline. A still-unresolved conflict keeps the ancestor's text — the conservative
  answer, since the alternatives are inventing a merge nobody approved or writing conflict markers into
  a file someone asked to save — and says so twice: a banner before, and the count in the status line
  after.

  A **Diff pane** below the columns stacks the three versions of the current region — left, base,
  right — for the reason the two-way view has one: once a file is any size, the three versions of one
  conflict are a screen apart, and reading them together is the hard part. Three columns makes that two
  eye-jumps rather than one, so it matters more here.

  Open it from **3-way merge…** in the toolbar, or from the command line as
  `FubarDiff --merge $BASE $LOCAL $REMOTE`, which is the argument order `git mergetool` passes.

- **Syntax highlighting in the panes**, for every language a TextMate grammar ships for — not only the
  ones the code options below understand. It follows the app's theme, and it is on by default with a
  switch in Settings → Appearance. Purely visual: it never changes what the comparison found and never
  re-runs it.

- **Source-code comparison for C#, JavaScript and TypeScript.** The language comes from the file
  extension, and it changes three things:

  - **Ignore comments** (Settings → Code compare): a line whose only change is inside a comment stops
    being a difference, and a comment-only line that was added or removed is drawn faintly rather than
    counted. The code on a line still compares normally — `foo(); // note` matches `foo();`, but not
    `bar(); // note`. Block comments and multi-line strings are tracked across lines, so the middle of
    a `/* … */` is treated as a comment even though, read on its own, it looks like code.
  - **Ignore blank lines** (Settings → Code compare), for a file whose vertical spacing was reformatted
    and nothing else.
  - **Character-level highlighting on token boundaries.** `==` becoming `===` now highlights the whole
    operator instead of a lone third `=`, `>` becoming `>=` highlights `>=`, and a changed word inside
    a long message string highlights that word rather than the entire string.

  Both options are off by default — a changed comment *is* a change until you say otherwise — and both
  say so on screen when the pair is not a language they apply to, rather than silently doing nothing.

### Changed

- **Large files are between four and five thousand times faster, depending on how you break them.**
  Measured, then fixed, in that order — the first measurement said the obvious plan was wrong.

  | | before | after |
  | --- | --- | --- |
  | 1,000,000 lines, 50,000 scattered changes | 15.8 s | 1.4 s |
  | 1,000,000 lines, one localised change | 1.6 s | 0.37 s |
  | 1.8 MB minified (one line), as text | 68 s | 13 ms |
  | 1.8 MB minified (one line), as JSON | 124 s | 0.3 s |

  The plan had been to virtualise the aligned document, on the reasoning that a million rows of
  metadata are built and only fifty are ever looked at. That is true and it was worth about 110 MB,
  but it was not the problem: of the 15.8 seconds, 15.5 were inside a single call to the diff engine
  and everything else in the pipeline came to under 800 ms between them.

  Three things were actually wrong. **The engine was given the whole file at once** — it now trims the
  identical head and tail, splits what remains at lines that occur exactly once on each side (a
  method signature, a key, a timestamp: a line unique in both documents has exactly one plausible
  counterpart), and aligns each piece independently. Below 10,000 lines nothing is split at all, so
  ordinary comparisons produce byte-identical output to before.

  **Every modified line was being word-diffed twice**, once by DiffPlex's side-by-side builder to fill
  in sub-pieces nobody read, and once by our own inline engine on the display text. Dropping the
  builder is where the minified numbers come from: 68 seconds to 13 milliseconds, for output that was
  being discarded.

  **A JSON property lookup was a linear scan**, and every caller sits inside a loop over the other
  document's properties. A 120,000-property document spent 45 seconds in the array-key scanner alone,
  looking for arrays it never found. Large objects now index themselves on first use.

  Two smaller guards came with it: character-level diffing is skipped on lines over 20,000 characters
  (the line still shows as changed — a bundle on one line would have highlighted most of itself
  anyway), and the per-row metadata behind each pane is derived on access rather than stored, which
  is the virtualisation that was planned, kept because it is 110 MB and one less copy of the document.

- **Every difference now has a background, and the one you are on stands out from it.** Two gaps,
  either side of the same idea.

  A row that was *edited* had no row-level mark at all — only the words that changed were highlighted,
  on the argument that a full-row wash competes with something more precise. It does, but the cost was
  that scanning a file for "which lines changed" worked for insertions and deletions and simply did
  not for edits, which are most of a real diff. Every changed row is tinted now, at a weight (0.12)
  well under the word-level highlight (0.30–0.55) so the precise mark stays the loud one: the row says
  *where*, the words say *what*. A modified row takes the colour of the column it is in — the removal
  colour on the left, the addition colour on the right — matching the highlights already inside it.

  And with nothing selected, everything now draws quietly. A negative "current range" used to mean
  every change drew at full strength, which was survivable when a third of them had no tint and became
  a wall of colour once they all did. An unnavigated document reads as one even wash saying "the
  changes are here"; F7/F8 then lifts one out of it, with its accent bar and outline on top.

- **The Json view marks every change, not just the current one.** Its two documents used to highlight
  exactly the difference you were standing on, which made them unreadable as documents: a file with
  eleven differences showed one, and the only way to find the others was to press Next eleven times.
  Each change now carries a quiet tint of its own — the exact characters, not a band across the line,
  since one line of JSON routinely holds several properties — and the current one is painted at full
  strength inside the band and bracket it already had. An ignored change is drawn too, in the same
  neutral, barely-there colour the aligned views use: it still exists, you just asked not to be told
  about it.

- **The toolbar is eight controls.** Open, ◀ ▶, three toggles (*Whitespace*, *Collapse*, *Edit*),
  **View** and **⋯**. What used to sit beside them — a "Compare:" label and its combo, a
  side-by-side / unified switch, *Diff pane*, *Wrap*, *Patch* and *Settings…* — is now two menus:
  **View** for everything about what is on screen, **⋯** for patch export and settings. The three
  toggles that stayed are the ones reached for while reading a particular diff; the rest were being
  set once and then occupying the row for the rest of the session.

  **Compare as** and **Layout** are submenus with those names, which also settles an old trap: the
  mode selector (Auto / Text / Json — *how* the files are compared) and the view switch (*what* is on
  screen) rendered the same two words for different questions, and no amount of inline labelling made
  two adjacent controls read as two questions. Under headings, they do.

- **One pair of Prev/Next buttons, in every view.** The Json view used to bring its own strip of
  Prev/Next plus a caption, stacked directly under the window's toolbar, because a hunk and a semantic
  change are different things to step through — so the toolbar hid *its* buttons in Json mode rather
  than offer two "next" buttons that disagreed. `DiffPaneViewModel.NextDifference` now decides from
  the view that is actually on screen, so the toolbar's buttons are the only ones, that whole second
  row is gone, and F7 / F8 finally do the same thing in the Json view that they do everywhere else.
  Where the strip's caption went: the status bar, which already said "Change 2 of 5" for text
  comparisons and now says `$.meta.owner · Modified · 2 of 5` for JSON ones.

  API Studio still gets the strip — it embeds this view where there is no toolbar to put buttons in.
  `JsonView.ShowToolbar` is how a host says which it is.

- **Settings is written in sentences.** Every option is a row with a plain-language description under
  it, instead of a terse label whose explanation lived in a tooltip nobody hovers: *Ignore invisible
  encoding differences — "Text that looks identical counts as identical. An accented é can be stored
  two ways, and macOS and Windows disagree about which."* The groups are named for what they do
  (General, What counts as a difference, JSON, Display, The Pretty button) rather than for the layer
  they belong to, switches replaced check boxes, and the three rules that need a regular expression or
  a JSON path moved behind a collapsed **Advanced** — they are for particular files, and having them
  open made a page of ordinary switches look like something you had to configure before use.

- **Opening files is one button.** The toolbar's first row — two text boxes, two Browse buttons, a
  Compare button, Recent, + Tab, 3-way merge and Folders — is gone, replaced by **Open ▾** (Ctrl+O).
  It picks both files in one dialog: select two, or one to fill whichever side is free. The same menu
  replaces a single side, swaps them, reopens something recent, opens a tab, and starts a folder
  comparison or a three-way merge.

  Eight controls for a question with one answer ("which files?") were occupying a band of the window
  for the whole session, and the two commonest ways in — dropping files on the window, and the command
  line — never touched them. The collapsed one-line summary that stood in for the row afterwards is
  gone too, because the tab already carries `left.json ↔ right.json`. What the row could do and a
  button cannot is show a half-finished choice, so the empty state does that instead: open one file and
  it says which one is loaded and that it needs the other.

- **The comparison tabs moved into the title bar**, Chrome-style, and no longer hide themselves until a
  second one opens. The strip used to cost a row of window, which is why a single tab was not worth
  showing; up here it costs nothing the window was not already spending on decoration. A tab carries a
  dot while it holds unsaved changes — the one thing about a tab its title cannot say, and the thing
  worth knowing *before* choosing which tab to close.

- **The orange banners are a status bar.** "The files changed on disk" (with its Reload button) and the
  format-difference warning were bands across the top of the window, each pushing the diff down a row
  to say something you had not asked about; the three-way merge's unresolved-conflict count was another
  one. All of them are now in the status bar along the bottom, beside the change counts and the unsaved
  badge — the one place a user already looks to find out what the app thinks is going on. An **error**
  still gets a band: it means the thing you just asked for did not happen, and that may not be filed
  away quietly in a corner.

- **Toolbar options are toggle buttons, not check boxes**, in all three windows. A tick box in a row of
  buttons is a second visual language for the same act — click this, something changes — sitting at its
  own height halfway along the row. The toolbar also carries only what is reached for mid-comparison
  (ignore whitespace, collapse unchanged, the diff pane, edit, and wrap in the unified view); the rest
  stays in Settings, which is what makes a row that short possible.

- **Every button is the same height.** `.primary-btn` sized itself from its own padding and stood a few
  pixels taller than everything beside it, which is why the gallery had a `Height="30"` on the blue
  button and nothing else did. All the button classes now share one `ControlHeight`, and
  `ToggleButton` finally picks up `.toolbar-btn` at all — an Avalonia type selector matches the exact
  type, so `Button.toolbar-btn` never reached a `ToggleButton`, and the Json view's Pretty toggle had
  been rendering as a stock Fluent button among a row of flat ones. The merge window's file summary
  carried a `link-btn` class no style has ever defined, with the same result; it is a `toolbar-btn`
  now.

- **A moved block now reads as the block, not as a brace and half of the next one.** When a run of
  added or removed lines is bounded by lines identical to the ones just inside it, the diff is
  genuinely ambiguous — several placements describe the same two files, and every one of them is
  equally minimal, so no aligner has grounds to prefer one. Source code hits this constantly, because
  the lines at a block's edges (`}`, `});`, blank lines) are its least distinctive. Move a method and
  the removal was as likely to come back as `}` + the *next* method's opening as it was the method you
  actually moved. Change groups are now slid to the placement that reads best — preferring boundaries
  at blank lines and at lower indentation — which is what git's own compaction heuristic does, and for
  the same reason. Provably content-neutral: a group only moves across a line identical to the one
  leaving it, so both documents, the counts and the hunks are unchanged; only the pairing of equal
  lines moves.

- **The window is mostly diff now: five rows of chrome became two.** The file pickers collapse to a
  one-line `before.json ↔ after.json` summary the moment a comparison succeeds (click it to get them
  back), the two toolbar rows merged into one, and the controls that only work sometimes now only
  appear then — the merge buttons when a difference is selected, Save once a decision has been made.
  Recent and + Tab moved up beside the file pickers (they answer the same question: what am I
  comparing), the theme picker moved into Settings, and Prev/Next became icons since the diff map,
  F7/F8 and the status line were already saying it. In the Json view the toolbar's own Prev/Next now
  hides, because that view brings its own — one walks hunks, the other walks semantic changes, and two
  "next" buttons that disagree is worse than one.

- **The toolbar is decluttered, detailed options moved to a Settings window, and its two "Text/Json"
  controls are labelled apart.** "Ignore whitespace" stays on the toolbar - it's the one developers
  reach for constantly reviewing a diff - and everything else opens from a single "Settings…" button:
  a two-section window, **Text compare** and **JSON compare**, replacing six checkboxes that used to
  fight for space in one row. Separately, the comparison-mode dropdown (Auto/Text/Json - how to
  compare) and the view switch (Text/Json - what to show right now) render the exact same two words for
  two different questions; both now carry an inline "Compare:"/"View:" label instead of leaving the
  distinction to a tooltip. The action row's dividers were also regrouped around what they actually
  separate (navigation | merge | save | display | appearance) instead of one divider per control, and
  the previously-unlabelled theme picker got a tooltip.

- **Text mode never reformats a file, JSON included.** A minified file diffed against a pretty one
  stays exactly as minified as it was on disk - Text mode shows literal content, full stop. Comparing
  two very differently-formatted JSON files is what the **Json view** is for (below): it needs no line
  alignment at all, so it has no reason to touch either side's formatting, and it is the default the
  moment a comparison turns out to be JSON. The one remaining way to reformat for display is the
  existing, explicit "Reformat" toggle (renamed from "Normalize XML", and now shown for JSON too - it
  was previously hidden whenever a comparison turned out to be JSON, which made it unreachable for the
  one format most worth reformatting) - opt-in, and unaffected by any of this. Turning it on affects
  only the Text view; if you then take a side and save, the reformatted text is what gets written,
  which is the point of it being an explicit opt-in rather than automatic.

- **The Diff pane now stacks old above new instead of side by side.** The same line directly above its
  replacement makes the character-level highlight - already the strongest signal it draws - readable
  at a glance instead of asking the eye to jump a pane's width to compare two short strings. A pure
  insertion or deletion now shows nothing at all on the side that has none, rather than a blank filler
  line: side-by-side alignment needed matching row counts on both sides, but a stacked block does not.

### Added

- **Files that differ only in encoding, BOM or line endings are now reported as different.** They were
  previously reported as *identical*, which is the worst possible answer right after your version
  control said otherwise: the reader consumes the byte order mark and splits on every terminator, so
  the two documents genuinely are identical by the time anything can see them. The difference is now
  detected alongside the lines and named in full — `byte order mark (present vs absent), line endings
  (CRLF vs LF)` — in the status line and a banner, since when it is the only difference there is
  nothing else on screen to notice.

- **Reveal invisible characters** (Settings): marks non-breaking spaces, zero-width characters, soft
  hyphens and bidirectional controls with a visible tag — `NBSP`, `ZWSP`, `RLO` — where they occur.
  This is for the case where the diff is right and *looks* wrong: two lines differing only by a
  non-breaking space are flagged as changed and appear identical, with nothing to explain why. The
  bidi controls are included because a run of them can make source read in one order and compile in
  another. Curly quotes and dashes are deliberately not marked — they are visibly different already,
  and flagging them would cry wolf on ordinary prose.

- **Normalize Unicode (NFC)** (Settings): treats text that renders identically as equal — `é` written
  as one character and as `e` plus a combining accent. macOS decomposes where Windows and Linux
  compose, so the same edit made on two machines can differ in every accented word and look identical
  in every editor. Off by default: it *is* a real difference in the bytes, and a tool whose job is
  showing what changed should not hide one until asked.

- **Two JSON comparison options that were built but never reachable: "treat null and missing as the
  same", and array key overrides** (which field identifies an array's elements, for the arrays where
  auto-detection guesses wrong). Both existed fully in Core - and, for array key overrides, even had a
  persisted settings field - but nothing in the UI ever read or set them; the new Settings window is
  the first place either has actually been usable.
- **A manual "ignored paths" list**, also in the Settings window: JSON paths whose differences are
  never reported, for a field that changes on every run (a `requestId`, a timestamp). The click-to-
  ignore affordance in the tree is API Studio-only; this is Fubar Diff's own way to set one.

- **Json view**: replaces the old standalone Tree view as the second mode alongside Text, and is now
  the **default** whenever a comparison turns out to be JSON - the change tree, plus BOTH documents
  shown exactly as given, not reformatted. Stepping through changes (Prev/Next, or a click in the
  tree) highlights the current change's own location directly in each pane. There is no cross-document
  line alignment at all, which is what makes it immune to formatting and property-order differences by
  construction - reformatting, minifying, or reordering keys on one side changes nothing about where
  the other side's matching field gets highlighted, since each side is addressed independently by its
  own parsed structure rather than by a shared line number. A minified file stays visibly minified
  here too - neither view touches your file's formatting; Json just doesn't need to align in the
  first place, which is what makes a wildly different pairing a non-issue instead of a special case.
  It now has its own **Diff pane** too (below), stepping in lockstep with the tree and the highlight
  above it.

- **Diff pane, now also in the Json view.** Text mode's close-up - the current difference shown large,
  old above new - now has a Json-mode counterpart: the current change's own lines, isolated from the
  rest of each document. Since Json changes have no aligned rows to excerpt from, it isolates by the
  change's own source location instead, so it works the same regardless of how differently the two
  sides are formatted. The same "Diff pane" toggle shows or hides both.

- **A modified line no longer washes the whole row.** Only the actual changed word(s) are tinted now -
  the full-row amber background was competing with that more precise highlight rather than helping it.

- **The Diff pane highlights just the difference, not the line it's on - and stronger than the main
  panes do, in both modes.** It has no full-line or full-width tint at all now, for any kind of change:
  a page showing nothing but the current difference has no "where" left for a whole-row band to answer,
  so it shows exactly the changed text instead, at a bolder intensity than the main view's tints ever
  run at. In Json mode this is the first place that highlights down to the exact column, not just the
  line, of a change.

- **The hunk you just navigated to now stands out from every other change in the file**, not just from
  unchanged text: every OTHER hunk's tint fades once you have a current one selected, so the one you
  are looking at does not have to compete for attention with the rest of the file's changes.

- **Navigating to a difference now centres it in the viewport**, in both Text and Json mode, instead of
  merely scrolling it into view at whichever edge it happened to approach from.

- **Two-editor side-by-side view** built on AvaloniaEdit, replacing the row list. Line numbers show
  each line's number in its own file rather than in the aligned view, so they still match what is on
  disk across insertions.
- **Character-level diff** within modified lines, so a one-word change reads at a glance instead of
  tinting the whole row.
- **Diff map** between the panes: one tick per change, coloured by kind, with a viewport indicator;
  click or drag to jump.
- **Synchronized scrolling** between the two editors.
- **Hunk-level merge and save**: take the left or right version of the current change, reset a
  decision, then Save or Save As. Saving preserves the file's encoding, BOM, line endings and trailing
  newline byte-for-byte.
- **Keyboard shortcuts**: F7/F8 or Alt+Up / Alt+Down for previous/next change, Alt+Left / Alt+Right to
  merge, Ctrl+S to save.
- **Diff pane** under the side-by-side view, showing the current difference on its own — the two sides
  of one change are often far enough apart vertically that reading them together is the hard part.
  Resizable, and toggleable from the toolbar. Available in API Studio's diff dialog too, since both
  hosts share the same widget.
- **The current difference is marked with shape, not just colour** — an accent bar down its edge and a
  hairline boxing it in. In a file where most rows are already tinted, a denser tint does not say
  which change you just navigated to.

- **Semantic JSON comparison.** A hand-written parser records the line and column of every value, and
  the differ compares structure rather than text:
  - reordering object properties is not a difference (JSON objects are unordered) — with a
    **Report key order** toggle for when it matters;
  - reformatting alone is not a difference;
  - array elements are matched by an auto-detected identity key (`id`, `_id`, `uuid`, `guid`, `key`,
    `name`), so an element inserted mid-array marks only itself instead of everything after it —
    with **Arrays by position** to turn it off;
  - a **Text / Tree** switch shows the changes as a structural tree.
  - Falls back to a text diff whenever a file does not parse, since a broken file is exactly when a
    diff is most wanted.
- **Tabs**: several comparisons open at once (Ctrl+T / Ctrl+W). `MainViewModel` became the per-tab
  `ComparisonViewModel`, with a thin `ShellViewModel` owning the tab collection and the genuinely
  shared state (theme, settings file, recent list).
- **Search** within either pane (Ctrl+F), from AvaloniaEdit's own find bar.
- **Drag and drop**: drop two files onto the window to compare them, or one to fill the empty side.
- **Recent comparisons**, with comparison options and theme persisted to
  `%APPDATA%/fubar-diff/settings.json`. The file is hand-editable (enums by name) and also holds the
  per-JSON-path array key overrides.
- Comparisons and re-comparisons now run **off the UI thread**, with the in-flight one cancelled when
  options change again — so toggling several options quickly cannot queue diffs or apply them out of
  order.

### Changed

- `TextDocument` now carries a `TextFormat` (encoding, BOM, line ending, trailing newline) instead of
  loose encoding and line-ending fields — the BOM and trailing newline cannot be recovered from the
  lines alone, and losing either on save turns a one-line merge into a whole-file diff.

- Initial side-by-side file comparison: pick two files (or pass them on the command line) and see them
  aligned, with line numbers and per-line change highlighting.
- Placeholder rows opposite insertions and deletions, and a single shared scroller, so the two panes
  cannot drift out of alignment.
- Next / previous change navigation, wrapping at both ends.
- Comparison options: ignore whitespace, ignore case, and JSON/XML structure normalization (which
  falls back to a plain text diff when the content does not parse).
- Encoding and line-ending detection (UTF-8 / UTF-16 BOMs, CRLF / LF / CR), a 64 MB size cap, and
  rejection of binary files with a readable reason.
- Dark / light theme switching at runtime, from the shared `Fubar.Controls` design system.
- Clean layered architecture (Core / Application / Infrastructure / UI) with a NetArchTest suite
  enforcing it, including that DiffPlex stays confined to Infrastructure.
