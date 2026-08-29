using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Lets a comparison carry the settings hierarchy it belongs to, and offer to remember a change at any
/// level of it.
///
/// Only some comparisons have somewhere to remember a setting. Two responses of a request do - the
/// rules belong on the request, its folder, or the user's global preferences - so that comparison
/// passes one of these. The OpenAPI import preview does not: it compares request definitions, where a
/// differing field is the whole point of reviewing the import. Passing null there means the settings
/// affordances never appear rather than appearing and doing nothing.
///
/// Replaces the older ignore-paths-only context: ignore rules turned out to be one setting among
/// several wanting the same global → folder → request treatment, so this carries the whole hierarchy
/// rather than that one list.
/// </summary>
/// <param name="InheritedLayers">
/// Everything ABOVE the request - the global level then each ancestor folder, root-most first. The
/// request's own layer is deliberately excluded: the dialog edits it live, so it stacks its own
/// working copy on top of these and re-resolves, rather than being handed a result it cannot recompute.
/// </param>
/// <param name="RequestOverrides">
/// The request level's own overrides as persisted. Null when it overrides nothing. Cloned by the
/// dialog before editing, so cancelling changes nothing.
/// </param>
/// <param name="FolderName">
/// The nearest ancestor folder's display name, for labelling the folder save option. Null when the
/// request sits directly in the collections root, where saving to a folder is not offered.
/// </param>
/// <param name="SaveAsync">
/// Persists one level's overrides, or null when this comparison can only change settings for the
/// session. A callback rather than the request itself keeps this out of the business of knowing how a
/// request, folder or app-settings file is stored. Null settings clear that level entirely.
/// </param>
public sealed record DiffSettingsContext(
    IReadOnlyList<ComparisonSettingsLayer> InheritedLayers,
    ComparisonSettings? RequestOverrides = null,
    string? FolderName = null,
    Func<ComparisonScope, ComparisonSettings?, Task>? SaveAsync = null);
