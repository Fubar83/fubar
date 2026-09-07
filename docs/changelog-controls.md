# Changelog

All notable changes to `Fubar.Controls` are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are derived from git
tags by MinVer.

## [Unreleased]

### Added

- `TreeItemState.ExpandedPath` — binds a tree row's folded state to a property on the item behind it,
  two-way. Binding `TreeViewItem.IsExpanded` from a style setter looks like it works and then quietly
  stops: the expander writes the user's fold as a **local** value, which outranks a style setter
  permanently, so the first fold by hand severs the connection. This establishes the binding on the row
  itself, at the same local priority the expander writes at, so the two share one slot and the later
  write wins in either direction. A path (rather than a value) keeps it opt-in — a tree whose items have
  no such property simply does not set it.

- The tree expander is a boxed `+` / `−` on the connector, the way a tree drawn in line characters has
  always done it. One glyph for both states — the bar is always drawn and the stem is hidden when
  expanded — so a plus is a minus with a stem and the two cannot drift apart. The button keeps its
  column on a leaf and hides only the box, which is what stops a file's label sitting a notch left of
  the folders beside it.

- `TreeIndentGuides` — the connector rails that make a tree read as a tree, in the shape everyone
  already knows: a tee (`├`) into every row, an elbow (`└`) into the last one, and an ancestor's rail
  carried down past its descendants only while that ancestor still has siblings below it. It lives in
  the `TreeViewItem` template bound to `Level`, reserving `(Level + 1) × 14px` and drawing inside the
  space it reserved, so **the indent and the guides are the same thing** and every tree in an app gets
  them without the host binding anything.

  "Last child" is a fact about a row's position among its siblings — and about every ancestor's position
  among theirs — so it is read from the container tree at render time rather than bound. Each owning
  `ItemsControl`'s `ItemCount` is watched: without that, deleting the last request in a folder would
  leave the new last row still drawing a tee into nothing.

  Rows have to touch for the per-row segments to join up, which is why the row theme moved its
  breathing room from vertical padding into `MinHeight`. New `TreeGuide` palette token, a shade stronger
  than `BorderSubtle` so the rail survives crossing a selected row.

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

### Removed

- **`TreeLevelIndentConverter`.** It indented a row by putting a level-sized margin on the row's
  content, which is a second thing claiming to own the indent — and it was stacking with the one Fluent's
  row template already applied, so a nested row sat ~30px in from its siblings on a 14px step. Indent is
  `TreeIndentGuides` in the row template now, and nothing else. Hosts drop the `Margin` binding; there is
  nothing to replace it with.

### Fixed

- **Row indentation came from two places at once.** Fluent writes its level indent as an attribute
  *inside* its own template, which makes it a local value — and a local value beats every style, so it
  could not be turned off from outside. `TreeViewItem` now gets its own template (part names kept:
  `PART_LayoutRoot`, `PART_HeaderPresenter`, `PART_ExpandCollapseChevron`, `PART_ItemsPresenter`), with
  a bare rotating chevron in place of Fluent's bordered `ToggleButton` box, and one indent that the
  guides both reserve and draw.

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
