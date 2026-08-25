# Fubar.Controls

[![CI](https://github.com/Fubar83/fubar/actions/workflows/ci.yml/badge.svg)](https://github.com/Fubar83/fubar/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A reusable, **app-agnostic** Avalonia **design system + component library**. It is the single source
of the shared look and feel for every app built on it — colour tokens, typography-neutral styles,
shared button/tab styles, and a catalog of composable controls. It depends only on Avalonia
(+ AvaloniaEdit for the JSON editor) — no reference to a host app, a domain model, or any view model.

Used by [Fubar API Studio](api-studio.md) and [Fubar Diff](diff.md), both in this repository. It is
not published as a package - it is consumed by project reference.

## Using it (two lines)

```xml
<Application ...>
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- 1. Palette in Resources so ThemeDictionaries follow RequestedThemeVariant. -->
                <ResourceInclude Source="avares://Fubar.Controls/Themes/Palette.axaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>

    <Application.Styles>
        <FluentTheme />
        <!-- 2. Everything else: component themes, SeamlessTabControl, Button classes, workspace tabs. -->
        <StyleInclude Source="avares://Fubar.Controls/Themes/Fubar.Controls.axaml" />
    </Application.Styles>
</Application>
```

Reference the controls with an xmlns:

```xml
xmlns:fc="using:Fubar.Controls"
```

To switch Dark/Light at runtime, set `Application.Current.RequestedThemeVariant` — every token,
component and style repaints with no restart.

## What's in the box

### Colour tokens — `Themes/Palette.axaml`

The design system's single source of truth: semantic `DynamicResource` keys defined for both Dark and
Light via `ResourceDictionary.ThemeDictionaries`. Surfaces (`BgSidebar`, `BgHeader`, `BgHover`,
`BgSelected`, `BgEditorCanvas`, `BgResponsePanel`), text (`TextPrimary`, `TextSecondary`), borders
(`BorderSubtle`, `BorderEditor`), accents (`BtnSendBg`), HTTP-method colours
(`Method{Get,Post,Put,Delete,Other}Brush`), JSON syntax colours, status-badge ranges, and legacy
`Postman*` aliases. Every component and style below resolves its colours from here, so re-theming an
app is a matter of overriding these keys.

### Shared styles

| Include | Provides |
| --- | --- |
| `Themes/ButtonStyles.axaml` | `Button` classes: `.toolbar-btn`, `.primary-btn`, `.secondary-btn`, `.icon-btn`, `.icon-btn-danger`, `.TabPill`. |
| `Themes/WorkspaceTabStyles.axaml` | `Border.WorkspaceTab` (+ `.Active`) — Chrome-style title-bar tabs. |
| `Themes/SeamlessTab.axaml` | Styling for the `SeamlessTabControl` boxed-tab look. |

(All three are pulled in automatically by `Fubar.Controls.axaml`.)

### Components

Primitives — small, single-purpose, coloured through their own `Background`/`Foreground`:

| Control | Kind | Notes |
| --- | --- | --- |
| `Badge` | `ContentControl` | Rounded pill label (method tags, counts, status words). |
| `StatusDot` | `TemplatedControl` | Filled circle; `Diameter`, colour = `Background`. |
| `ValidityIcon` | `TemplatedControl` | Glyph driven by `State` (`Unknown`/`Valid`/`Invalid`). |
| `Divider` | `TemplatedControl` | 1px rule; `Orientation` horizontal/vertical. |
| `IconButton` | `Button` | Compact, borderless, glyph-only button. |
| `PillToggle` | `ToggleButton` | Rounded toggle segment for view/filter switchers. |
| `SectionHeader` | `TemplatedControl` | `Title` + right-aligned `Action` slot. |
| `LabeledField` | `HeaderedContentControl` | Caption (`Header`) + field (`Content`); stacked or inline. |
| `Toolbar` | `ItemsControl` | Horizontal items strip with consistent spacing. |
| `Spinner` | `TemplatedControl` | Indeterminate rotating-ring loader; `Diameter`, colour = `Foreground`. |
| `MetricChip` | `TemplatedControl` | Icon + monospace value readout (latency/size/count); `Icon`, `Text`, threshold colour via `Foreground`. |

Composed — built from the primitives above:

| Control | Kind | Composes | Notes |
| --- | --- | --- | --- |
| `Chip` | `ContentControl` | `IconButton` | Content + optional close (`CloseCommand`, `ShowClose`). |
| `SearchBox` | `TemplatedControl` | `IconButton` | Search glyph + `Text` + clear button. |
| `Card` | `HeaderedContentControl` | `Divider` | Bordered surface with optional header strip. |
| `EmptyState` | `TemplatedControl` | (action slot) | Centered icon + title + description + action. |
| `Banner` | `TemplatedControl` | (action slot) | Inline message strip; `Severity` (Info/Success/Warning/Error) + `Icon` + `Message` + `Action`. |
| `SegmentedControl` | `ListBox` | `PillToggle` look | Single-select joined pill row (iOS-style); bind `ItemsSource` + `SelectedItem`. |

Data / larger reusable controls:

| Control | Kind | Notes |
| --- | --- | --- |
| `KeyValueGrid` | `TemplatedControl` | Dumb editable key/value(/description) grid. Bind `ItemsSource` (rows exposing `Enabled`/`Key`/`Value`/`Description`); `AddCommand`/`RemoveCommand`; optional `KeyCellTemplate`/`ValueCellTemplate`/`DescriptionCellTemplate` for richer cells. |
| `SeamlessTabControl` | `TabControl` | Boxed tabs whose selected tab merges into the content area. Host sets `Background` to the surface behind the tabs. |
| `JsonEditor` | `UserControl` | Pretty-printed JSON editor: line numbers, TextMate highlighting, brace folding, Ctrl+F. Bind `Text`; `IsReadOnly`. |
| `FocusHelper` | attached behavior | `fc:FocusHelper.FocusOnTrue="{Binding IsEditing}"` — focuses (and selects) an element when the flag flips true. |

Smart (interactive) controls — own their gestures/state but stay app-agnostic via abstractions:

| Control | Kind | Notes |
| --- | --- | --- |
| `TabStrip` | `ListBox` | Chrome-style tab strip owning its **own** drag & drop: reorder within the strip, a floating ghost, and — via a host-supplied `ITabDragHost` — live move between strips in other windows + tear-off into a new window. Bind `ItemsSource`, two-way `SelectedItem`, `ItemTemplate`, `CloseCommand`, `ShowCloseButton`, `DragHost`. |
| `ITabDragHost` | interface | The app implements this (over its window manager) so `TabStrip` can move a tab's data between collections, tear off into a new window, and enumerate peer strips — the only app-specific seam the strip needs. |

Also included: generic value converters — `EqualityConverter`, `InheritedOpacityConverter`,
`TreeLevelIndentConverter` (reference via `{x:Static fc:<Name>.Instance}`).

## Dumb vs smart (the design rule)

- **Dumb (default):** `StyledProperty` inputs + `RoutedEvent`/`ICommand` outputs; no app types; no
  business logic; theming via `DynamicResource` tokens only. Most controls here are dumb.
- **Smart (rare — e.g. `TabStrip`):** may own gestures, transient state, even a transient window, but
  only against **generic abstractions + events** (`ITabDragHost`), never an app view model. The domain
  decisions (what a tab represents, how a window is created) are pushed back to the host.
- **Enforcement:** `Fubar.Controls.csproj` has no `ProjectReference`, and `Fubar.Controls.Tests`
  asserts the built assembly references nothing outside an allowlist of Avalonia, AvaloniaEdit and
  the BCL, and that no public type name leaks a domain concept.

## Gallery

`Fubar.Controls.Gallery` (a sibling project that references **only** this library) is a living style guide + dev
harness: it renders every component in Dark/Light and includes a two-window `TabStrip` drag demo, so
components can be built and visually locked without launching a full app.

```bash
dotnet run --project src/Fubar.Controls.Gallery
```

## What deliberately stays in the host app

Anything that depends on a host app's domain is **not** here, so the library stays app-agnostic. For
example, Fubar API Studio keeps its `{{variable}}` tooltip/intellisense behaviors and their
border-tint styles app-side, along with its HTTP/JSON-specific value converters (method/status/
JSON-kind/latency → brushes). These consume the tokens above, so they still match the shared look
without living in the library.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Develop against the Gallery — it is the fastest loop and the
thing that keeps the app-agnostic boundary honest.

## License

[MIT](LICENSE).
