using System.Collections.Generic;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// How strictly two JSON documents should be compared.
///
/// These differ in kind from the text-level <c>ComparisonOptions</c>: those decide which LINES look
/// equal, these decide which VALUES do. Formatting, indentation and property order are not values, so
/// none of them are options here - they simply do not survive parsing.
/// </summary>
public sealed record JsonComparisonOptions
{
    /// <summary>The sensible default: structural comparison, property order ignored.</summary>
    public static JsonComparisonOptions Default { get; } = new();

    /// <summary>
    /// Report a property that moved as a difference.
    ///
    /// Off by default because JSON objects are unordered by definition - most serializers make no
    /// promise about order, so reporting it produces noise on files nobody edited. Turn it on when key
    /// order matters to the tooling that reads the file.
    /// </summary>
    public bool ReportPropertyOrder { get; init; }

    /// <summary>
    /// Treat an explicit <c>null</c> and an absent property as the same thing.
    ///
    /// Off by default: the two genuinely differ in most schemas (absent means "unset", null means
    /// "explicitly nothing"), but plenty of serializers emit them interchangeably, and then this
    /// removes a whole class of false differences.
    /// </summary>
    public bool IgnoreNullVsMissing { get; init; }

    /// <summary>
    /// Identity keys for specific arrays, overriding the auto-detected one, keyed by the array's JSON
    /// path (e.g. <c>$.users</c>).
    ///
    /// The heuristic in <see cref="ArrayKeyResolver"/> covers the common cases; this is for when it
    /// guesses wrong, or when the key is named something it would never think of.
    /// </summary>
    public IReadOnlyDictionary<string, string> ArrayKeyOverrides { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Paths whose differences are never reported, in <see cref="JsonPathPattern"/> syntax.
    ///
    /// For the fields that change on every call - <c>requestId</c>, <c>timestamp</c>, a trace header
    /// echoed into the body - which otherwise bury the one difference that matters.
    /// </summary>
    public IReadOnlyList<string> IgnoredPaths { get; init; } = [];

    /// <summary>
    /// Disable identity matching entirely and compare arrays by position.
    ///
    /// Occasionally what you want: for an array whose order IS the meaning (a sequence of steps, a
    /// changelog), matching by key hides the fact that something moved.
    /// </summary>
    public bool MatchArraysByPosition { get; init; }

    /// <summary>
    /// Specific arrays to compare by position, by JSON path - the per-array form of
    /// <see cref="MatchArraysByPosition"/>.
    ///
    /// Both forms exist because the answer genuinely varies within one document: a file can hold a
    /// list of users, where order means nothing and matching by id is the only way to read a diff of
    /// it, alongside a list of migration steps, where order is the entire content. A single switch
    /// forces the wrong answer on one of them.
    /// </summary>
    public IReadOnlyList<string> PositionalArrays { get; init; } = [];

    /// <summary>
    /// Compare every array as an unordered collection: <c>["A","B"]</c> equals <c>["B","A"]</c>.
    ///
    /// Off by default, because for plenty of arrays the order IS the content. This is the blunt form;
    /// <see cref="UnorderedArrays"/> is the one to reach for first.
    ///
    /// <para>Ranked BELOW automatic identity-key matching on purpose: for a list of objects that carry
    /// an <c>id</c>, matching by that id already ignores order AND reports which field of which element
    /// changed, where matching whole values could only say "this one went, that one arrived".</para>
    /// </summary>
    public bool IgnoreArrayOrder { get; init; }

    /// <summary>
    /// Specific arrays whose order does not matter, by JSON path - the per-array form of
    /// <see cref="IgnoreArrayOrder"/>, and the reason it exists.
    ///
    /// <para>Identity keys answer "which element is this?" only for objects carrying an id field. An
    /// array of STRINGS - a set of tags, roles, feature flags, enabled locales - has no field to key
    /// on, so it always fell through to positional comparison and <c>["A","B"]</c> against
    /// <c>["B","A"]</c> reported two modifications for a document that had not changed. Marking the
    /// path unordered matches the elements by their whole VALUE instead, which needs no field and works
    /// equally for scalars, objects and nested arrays.</para>
    ///
    /// <para>An explicit <see cref="PositionalArrays"/> entry for the same path wins. That is a
    /// contradiction only the user can have written, and positional is the conservative half of it:
    /// reporting a reorder nobody cares about is a smaller failure than hiding one that matters.</para>
    /// </summary>
    public IReadOnlyList<string> UnorderedArrays { get; init; } = [];
}
