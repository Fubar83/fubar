# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
