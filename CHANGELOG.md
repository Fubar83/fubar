# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
- **Keyboard shortcuts**: F7/F8 for previous/next change, Alt+Left / Alt+Right to merge, Ctrl+S to save.

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
