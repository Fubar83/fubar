namespace Fubar.Diff.Core.Code;

/// <summary>
/// What a node in a source file's structure IS.
///
/// Deliberately a language-neutral vocabulary rather than a mirror of C#'s syntax kinds, even though
/// C# is the only language with a parser behind it today. Everything downstream - the tree, the
/// summary, the navigation - reads this enum, and a second language should be able to arrive by
/// writing one adapter rather than by teaching every consumer a second set of names. The members are
/// the ones that survive that translation: nearly every curly-brace language has types, methods,
/// fields and a way of importing.
/// </summary>
public enum CodeMemberKind
{
    /// <summary>The whole file. Exactly one of these, and it is the root.</summary>
    File,

    /// <summary>An import - <c>using</c>, <c>import</c>, <c>#include</c>.</summary>
    Import,

    /// <summary>A namespace, package or module.</summary>
    Namespace,

    /// <summary>A class.</summary>
    Class,

    /// <summary>A struct or value type.</summary>
    Struct,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>A record - kept apart from <see cref="Class"/> because changing between them is a real change.</summary>
    Record,

    /// <summary>An enum.</summary>
    Enum,

    /// <summary>A delegate type.</summary>
    Delegate,

    /// <summary>A method, including local functions promoted to their own row.</summary>
    Method,

    /// <summary>A constructor.</summary>
    Constructor,

    /// <summary>A finalizer.</summary>
    Destructor,

    /// <summary>An operator or conversion.</summary>
    Operator,

    /// <summary>A property.</summary>
    Property,

    /// <summary>An indexer.</summary>
    Indexer,

    /// <summary>A field or constant.</summary>
    Field,

    /// <summary>An event.</summary>
    Event,

    /// <summary>One member of an enum.</summary>
    EnumMember,

    /// <summary>Anything the parser recognised but has no better name for.</summary>
    Other,
}
