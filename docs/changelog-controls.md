# Changelog

All notable changes to `Fubar.Controls` are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are derived from git
tags by MinVer.

## [Unreleased]

### Added

- `TreeIndentGuides` — the vertical hairlines that make a tree read as a tree. Bound to a node's depth
  in the row's `DataTemplate`, it reserves the level indent *and* draws a rail through it, so nesting is
  expressed once rather than by a margin that says nothing about structure. Rows have to touch for the
  per-row segments to join into a continuous line, which is why the row theme moved its breathing room
  from vertical padding into `MinHeight`. New `TreeGuide` palette token, a shade stronger than
  `BorderSubtle` so the rail survives crossing a selected row.

- `SettingRow` — one line of a settings page: a `Header`, a muted `Description` under it, and the
  control itself (`Content`) on the right. The description is a real element rather than a tooltip,
  because an explanation nobody hovers to find is an explanation nobody reads — and "Normalize Unicode
  (NFC)" is not a question anyone can answer from the label alone. A `ToggleSwitch` inside one loses
  Fluent's default "On"/"Off" text, which in a column of rows is the same word repeated fifteen times
  to say what the knob already shows.

- `ToggleButton.toolbar-btn` — a `.toolbar-btn` that stays pressed, for a toolbar option that used to
  be a check box. An Avalonia type selector matches the exact type, so `Button.toolbar-btn` never
  reached a `ToggleButton` at all: one carrying the class rendered as a stock Fluent button among a row
  of flat ones. Checked state is a tinted fill with a blue border, and it wins over hover so an active
  toggle does not read as off while the pointer rests on it.

### Fixed

- **The explorer-tree row theme had never applied.** Its styles were selected as
  `fc|TreeView TreeViewItem`, and Avalonia matches a type selector against a control's *style key* —
  which `fc:TreeView` overrides to the base `TreeView` on purpose, to keep the Fluent template. So the
  selector matched nothing that can exist, and every tree rendered in stock Fluent: 32px rows, no
  palette hover, and a selected row filled with the raw accent (`#3B82F6`) as a full-bleed blue bar,
  while `BgSelected` sat in the palette unused. The row look is now a `ControlTheme` applied through
  `ItemContainerTheme` (as `TabStrip` and `SegmentedControl` already do), selected on the base type.
  `TreeRowThemeTests` pins the rendered metrics and fill, because a selector that matches nothing
  reports nothing.

  Two traps in one bug, both silent: a descendant combinator *also* stops matching once a selector
  reaches into a `/template/`, so spelling the type correctly would still not have been enough.

### Changed

- **Every button class is one height.** `ControlHeight` (30) is now the `MinHeight` of `.toolbar-btn`,
  `.primary-btn`, `.secondary-btn` and the new toggle, with vertical padding kept below it so the
  height decides the box. `.primary-btn` previously sized itself from `Padding="22,0"` and stood taller
  than everything beside it — which is why the Gallery carried `Height="30"` on the blue button and
  nothing else. A host should never set `Height` on one of these to patch a mismatch; fix it here.

## [0.1.0] - 2026-08-24

First release as a standalone package. The library was extracted, with its history, from the
[Fubar API Studio](https://github.com/Fubar83/fubar) repository, where it had been
developed as an app-agnostic component library from the start.

### Added

- Initial public package: colour tokens + theme (`Themes/Palette.axaml`,
  `Themes/Fubar.Controls.axaml`), the control catalog (`TabStrip`, `SeamlessTabControl`,
  `KeyValueGrid`, `TreeView`, `JsonEditor`, `SearchBox`, `SegmentedControl`, `Card`, `Section`,
  `Badge`, `Chip`, `Banner`, `EmptyState`, `Spinner`, `StatusDot`, `MetricChip`, `IconButton`,
  `PillToggle`, `Toolbar`, `Divider`, `LabeledField`, `ValidityIcon`), and value converters.
- `Fubar.Controls.Gallery` — a living style guide and development harness.
- Headless Avalonia test suite.
