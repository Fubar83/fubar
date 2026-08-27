# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Text mode never reformats a file, JSON included.** A minified file diffed against a pretty one
  stays exactly as minified as it was on disk - Text mode shows literal content, full stop. Comparing
  two very differently-formatted JSON files is what the **Json view** is for (below): it needs no line
  alignment at all, so it has no reason to touch either side's formatting, and it is the default the
  moment a comparison turns out to be JSON. The one remaining way to reformat JSON for display is the
  existing, explicit "Normalize XML" toggle - opt-in, and unaffected by any of this.

- **The Diff pane now stacks old above new instead of side by side.** The same line directly above its
  replacement makes the character-level highlight - already the strongest signal it draws - readable
  at a glance instead of asking the eye to jump a pane's width to compare two short strings. A pure
  insertion or deletion now shows nothing at all on the side that has none, rather than a blank filler
  line: side-by-side alignment needed matching row counts on both sides, but a stacked block does not.

### Added

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
