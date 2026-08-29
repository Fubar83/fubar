# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Collapse unchanged context.** Long stretches both sides agree on fold behind a `42 unchanged lines`
  placeholder, keeping three lines either side of every change. A three-thousand-line file with two
  changes now reads as two changes instead of two screens of scrolling to find them. On by default, one
  click to expand any fold, and a toolbar toggle that is remembered. Both panes fold identically —
  which they do for free, since the folds are computed from row indices and both sides already have
  identical row counts, so scroll sync stays the plain offset copy it always was. The three-way merge
  gets it too, where it is worth more: most regions of a merge resolve themselves, so what you are
  hunting is the few that do not, through the same thousands of untouched lines. An ignored row is
  never folded away — its faint band is the only evidence that an ignore rule is doing anything.

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
