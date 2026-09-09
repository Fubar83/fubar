# Screenshots

What each file shows, and what a replacement has to show to still be that file. The content is most of
the value: a screenshot of an empty window is worse than no screenshot, and these apps both look like
every other app in their category until they are showing real work.

## How to shoot

- **Dark theme.** Both apps default to it, both READMEs are read on a dark GitHub by most people, and
  the diff tints were chosen against it.
- **Window at 1500×950 or so**, then capture the window rather than the screen. Wide enough that the
  side-by-side panes are not scrolled sideways; short enough to stay legible inline on a README.
- **Nothing personal on screen** — no real hostnames, tokens, customer data or local paths under
  a home directory. Everything here is shot against a throwaway "petstore" workspace pointed at a
  local stub server, or against throwaway files. Use something equally disposable: a screenshot
  that has to be redacted has already been taken.
- **PNG, not JPEG.** These are screenshots of text; JPEG rings around every glyph.
- Keep each file **under ~400 KB** so a clone stays cheap. `oxipng -o4` or similar if needed.

## Fubar API Studio

| File | What is on screen | Used by |
| --- | --- | --- |
| `studio-request.png` | The hero. `list-pets#all` open on its Tests tab — the endpoint's URL with `{{baseUrl}}` in it, the case's two assertions, the environment picker on Staging, and a real JSON response beside them reading `200 OK · 51 ms · 224 B` with `Tests (2/2)`. | `README.md`, `api-studio.md` |
| `studio-rules.png` | The Rules tab of the same case: the comparison options on *Inherit*, and below them an ignored path and a tolerance that both came from `_folder.json`, each labelled `Folder: pets`. The point is that nothing on the screen was written on this case. | `api-studio.md` |
| `studio-batch-run.png` | `@pet-lifecycle` finished: four green steps, then a fifth row marked `cleanup` answering 404, and the verdict `4/4 passed in 133 ms · Ran 5 of 5`. Cleanup that found nothing left to clean up is the happy path. | `api-studio.md` |
| `studio-comparison.png` | `@smoke` judged by *Compare with Production* against a drifted server: one row `differs · 1 difference from Production`, one row `matches Production`, verdict `2/2 passed, 1 differ, 1 within tolerance`. One difference, not five — the rest were absorbed by the folder's rules. | `api-studio.md` |
| `studio-capture.png` | A case's Tests tab with one capture rule — `catId` ← `JsonBody $.id`, Session scope — which is how a value gets from one step of a chain to the next. | `integration-tests.md` |
| `studio-assertions.png` | The same tab on a later case, with one assertion of each shape: a status, a JSONPath equality, an `Exists`, a header `Contains`, and a response-time `LessThan`. The point is that the whole grid is legible without a script engine. | `integration-tests.md` |

## Fubar Diff

| File | What is on screen | Used by |
| --- | --- | --- |
| `diff-side-by-side.png` | The hero. Two versions of a real C# file with one difference selected — row tints, character-level spans, the location map, and the Diff pane close-up stacking the two versions of the selected line. Status bar: `Difference 1 of 8`. | `diff.md` |
| `diff-structural-csharp.png` | **The differentiator.** The same pair with the Structure panel open: `1 added, 3 changed, 1 reformatted and 1 moved`, listing each member by name and kind, beside a text diff that just says eight changes. The contrast between the two panels IS the feature. | `README.md`, `diff.md` |
| `diff-json-semantic.png` | Two JSON documents whose properties are in a different order, reported as `moved` four times over — with the one genuine value change, `$.fulfilment.expedited`, marked in both documents and shown in the close-up. A line differ would call these files completely different. | `diff.md` |

## Fubar.Controls

| File | What is on screen | Used by |
| --- | --- | --- |
| `controls-gallery.png` | The Gallery's Primitives page: method badges, status dots, validity icons, the button classes, chips and search. | `controls.md` |

## Still wanted

Nobody has shot these yet. The slots are left out of the docs rather than left broken: a missing image
renders as a broken icon on the repository front page, which reads as neglect.

| File | What it would have to show | Would go in |
| --- | --- | --- |
| `studio-oauth.png` | **Shoot this one carefully.** The token-request editor after a successful Test: the "what this profile will send" line, the `{{variables}}` list, and the token response with its Capture buttons. Use a throwaway client against a test tenant — and check the masking really is masking before the shutter, since this is the one screen that has a live credential on it. | `api-studio.md` |
| `studio-environments.png` | The environment editor with a **secret** value masked and a **session-only** variable, so the two kinds are visibly different things. The petstore demo has neither, so this needs its own workspace. | `api-studio.md` |
| `diff-three-way.png` | Three columns, a conflict region banded, and the **Result pane hand-edited** to something that is neither side — that is the thing other merge tools do not let you do. | `diff.md` |
| `diff-folder.png` | Folder comparison over two checkouts — added / removed / changed / identical rows, and the filter. Note that it is opened from *Open ▾*: two directories on the command line are not a folder comparison. | `diff.md` |
