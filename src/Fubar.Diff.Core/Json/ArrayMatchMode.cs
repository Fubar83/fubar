namespace Fubar.Diff.Core.Json;

/// <summary>
/// How one array's elements are paired up.
///
/// Three modes rather than two, because "match by a field" cannot serve an array of strings: a set of
/// tags, roles or feature flags has no field to be identified by, and before <see cref="Unordered"/>
/// existed such an array could only be compared by position - so reordering it reported every moved
/// element as changed.
/// </summary>
public enum ArrayMatchMode
{
    /// <summary>Element 0 against element 0. Right when the order IS the content - a sequence of steps,
    /// a changelog - and wrong for everything else.</summary>
    Position,

    /// <summary>Matched by whole value, so order does not matter. Needs no field, so it works for
    /// scalars, objects and nested arrays alike.</summary>
    Unordered,

    /// <summary>Matched by an identity field. The best answer where one exists: it ignores order AND
    /// says which field of which element changed.</summary>
    Key,
}
