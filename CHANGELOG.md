# Changelog

This repository holds two apps and a shared library. Their individual histories live in
[`docs/changelog-api-studio.md`](docs/changelog-api-studio.md),
[`docs/changelog-diff.md`](docs/changelog-diff.md) and
[`docs/changelog-controls.md`](docs/changelog-controls.md) — those record what happened while the three
were separate repositories, and their statements were true at the time.

This file records changes to the repository as a whole from the consolidation onward.

## [Unreleased]

### Changed

- **Consolidated `fubar-components`, `Fubar-API-Studio` and `fubar-diff` into one repository**, with
  full history preserved for all three.

  The split existed so `Fubar.Controls` could be consumed as a NuGet package, which was reasonable
  while the sharing ran one way. It stopped being reasonable once API Studio needed the **diff view**
  as well: that made the diff engine a second shared library and the dependency graph a mesh, so every
  cross-cutting change would have meant two or three pull requests plus a package publish and a version
  bump. The friction was already real — the first package was never published, and that left CI red.

  Everything is now a project reference, under one `Fubar.slnx`. `Directory.Build.props` sets
  `IsPackable=false` repo-wide.

  **The trade, stated plainly:** `Fubar.Controls` is no longer consumable from outside this
  repository. That was a deliberate choice, not an oversight.

- Dropped: the NuGet release workflow, `build/pack.ps1`, both `nuget.config` files and their
  `local-packages` folder feeds, the MinVer tag-versioning, and the `UseLocalComponents` dual-mode
  reference switch — all of which existed only to serve the package boundary.
