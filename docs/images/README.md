# Screenshots

Nine shots, in priority order per app. Each one names what has to be **on screen**, because the
content is most of the value — a screenshot of an empty window is worse than no screenshot, and
these two apps both look like every other app in their category until they are showing real work.

Drop a PNG in here under the filename given, then uncomment the matching `![…]` line in the doc.
The slots are commented out rather than left broken on purpose: a missing image renders as a broken
icon on the repository front page, which reads as neglect.

## How to shoot

- **Dark theme.** Both apps default to it, both READMEs are read on a dark GitHub by most people, and
  the diff tints were chosen against it.
- **Window at 1600×1000**, then crop to the window. Wide enough that the side-by-side panes are not
  scrolled sideways; short enough to stay legible inline on a README.
- **Nothing personal on screen** — no real hostnames, tokens, customer data or local paths under
  `C:\Users\<you>`. The API Studio shots are the risk here; use a throwaway workspace against
  `httpbin.org` or a local stub.
- **PNG, not JPEG.** These are screenshots of text; JPEG rings around every glyph.
- Keep each file **under ~400 KB** so a clone stays cheap. `oxipng -o4` or similar if needed.

## Fubar Diff

| File | What must be on screen | Goes in |
| --- | --- | --- |
| `diff-side-by-side.png` | The hero shot. Two versions of a **real C# file** — 40+ lines, a method inserted, a line edited so character-level spans show, a deleted block. One hunk selected so the current-difference marker is visible. Diff map on the right showing the whole file's shape. This is the one that has to say "this is a diff tool and it is not ugly" in one glance. | `docs/diff.md` top, `README.md` |
| `diff-structural-csharp.png` | **The differentiator — shoot this one carefully.** The structural C# panel listing members as changed / added / **moved** / **reformatted only**, beside the text diff that shows those same members as a wall of changed lines. The contrast between the two panels IS the feature: the text says "80 lines changed", the panel says "one method changed, three moved". Pick a refactor that reorders members and reformats a few, so the panel earns its place. | `docs/diff.md` |
| `diff-three-way.png` | Three columns, a conflict region banded, and the **Result pane hand-edited** to something that is neither side — that is the thing other merge tools do not let you do. Unresolved-conflict banner visible if you can arrange one. | `docs/diff.md` |
| `diff-json-semantic.png` | Json view: two JSON documents with properties in a **different order**, reported as equal apart from the one value that genuinely differs, with the change tree on the left. A plain line differ would call these two files completely different, which is the point. | `docs/diff.md` |
| `diff-folder.png` | Folder comparison over two checkouts — added / removed / changed / identical rows, and the filter. | `docs/diff.md` |

## Fubar API Studio

| File | What must be on screen | Goes in |
| --- | --- | --- |
| `studio-request.png` | The hero shot. A request with a `{{variable}}` in the URL **highlighted**, an environment selected in the picker, and a real JSON response rendered below with a sane status and timing. Not `200 {}`. | `docs/api-studio.md` top, `README.md` |
| `studio-oauth.png` | **Shoot this one carefully too.** The token-request editor after a successful Test: the "what this profile will send" line, the `{{variables}}` list, and the **token response with its Capture buttons**. Use a throwaway client against a test tenant — and check the masking really is masking before the shutter, since this is the one screen that has a live credential on it. | `docs/api-studio.md` |
| `studio-environments.png` | The environment editor with a **secret** value masked and a **session-only** variable, so the two kinds are visibly different things. | `docs/api-studio.md` |
| `studio-new-workspace.png` | The empty state with *New Workspace…*, or the freshly created workspace showing `collections/` and `environments/` in the tree. This is the first thing a new user sees, and until recently there was no route through it at all. | `docs/api-studio.md` |
