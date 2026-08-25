using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Lets a comparison carry ignore rules, and offer to remember them.
///
/// Only some comparisons have somewhere to remember a rule. Two responses of a request do - the rule
/// belongs on the request - so that comparison passes one of these. The OpenAPI import preview does
/// not: it compares request definitions, where a differing field is the whole point of reviewing the
/// import. Passing null there means the affordance never appears rather than appearing and doing
/// nothing.
/// </summary>
/// <param name="Paths">Rules already in force, from wherever they were persisted.</param>
/// <param name="SaveAsync">
/// Persists the current set, or null when this comparison can only ignore for the session. Taking a
/// callback rather than the request itself keeps this out of the business of knowing how a request is
/// stored.
/// </param>
public sealed record DiffIgnoreContext(
    IReadOnlyList<string> Paths,
    Func<IReadOnlyList<string>, Task>? SaveAsync = null);
