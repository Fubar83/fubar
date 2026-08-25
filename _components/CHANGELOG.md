# Changelog

All notable changes to `Fubar.Controls` are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are derived from git
tags by MinVer.

## [Unreleased]

## [0.1.0] - 2026-08-24

First release as a standalone package. The library was extracted, with its history, from the
[Fubar API Studio](https://github.com/Fubar83/Fubar-API-Studio) repository, where it had been
developed as an app-agnostic component library from the start.

### Added

- Initial public package: colour tokens + theme (`Themes/Palette.axaml`,
  `Themes/Fubar.Controls.axaml`), the control catalog (`TabStrip`, `SeamlessTabControl`,
  `KeyValueGrid`, `TreeView`, `JsonEditor`, `SearchBox`, `SegmentedControl`, `Card`, `Section`,
  `Badge`, `Chip`, `Banner`, `EmptyState`, `Spinner`, `StatusDot`, `MetricChip`, `IconButton`,
  `PillToggle`, `Toolbar`, `Divider`, `LabeledField`, `ValidityIcon`), and value converters.
- `Fubar.Controls.Gallery` — a living style guide and development harness.
- Headless Avalonia test suite.
