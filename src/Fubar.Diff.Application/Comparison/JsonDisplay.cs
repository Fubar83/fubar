using System.Collections.Generic;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Application.Comparison;

/// <summary>
/// What the Json view should show, once the user's per-side formatting choices are applied.
///
/// The changes travel WITH the text and that is the whole point. A <see cref="JsonChange"/> carries
/// spans into a specific string, so reformatting one side without re-deriving them would leave every
/// highlight pointing at the line the value used to be on. They are re-derived here, together, or not
/// at all.
///
/// Nothing here is ever written to a file, and none of it changes what the comparison FOUND -
/// reformatting is a way of reading a document, not of editing it.
/// </summary>
/// <param name="LeftText">The left document as it should appear.</param>
/// <param name="RightText">The right document as it should appear.</param>
/// <param name="Changes">The semantic changes, with spans into those two strings.</param>
public sealed record JsonDisplay(
    string LeftText,
    string RightText,
    IReadOnlyList<JsonChange> Changes);
