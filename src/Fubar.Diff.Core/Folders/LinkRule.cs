using System;
using System.Collections.Generic;

namespace Fubar.Diff.Core.Folders;

/// <summary>
/// Pairs two files in the SAME folder by a marker in their names.
///
/// The case this exists for is snapshot testing. Verify writes <c>Thing.Test.received.json</c> beside
/// <c>Thing.Test.verified.json</c>; ApprovalTests writes <c>.received</c> beside <c>.approved</c>.
/// Reviewing those pairs is a daily task, and until now it meant picking two files out of a folder by
/// hand, one pair at a time, with the names differing by one word in the middle.
///
/// A marker rather than a glob because the shape is always the same: one name is the other with a word
/// inserted before the extension. Removing the marker gives a KEY the two share, which is all the
/// pairing needs - and it works for any extension without a rule per file type.
/// </summary>
/// <param name="Left">
/// The marker identifying the left-hand file. The left side is the EXPECTED one - the committed
/// baseline - because that is what a diff's left side means everywhere else in this app, and a review
/// reads as "what changed since the approved version".
/// </param>
/// <param name="Right">The marker identifying the right-hand file: the newly produced one.</param>
public sealed record LinkRule(string Left, string Right)
{
    /// <summary>
    /// The conventions worth knowing out of the box - the two snapshot libraries most .NET codebases
    /// use, plus the generic pair people write by hand.
    /// </summary>
    public static IReadOnlyList<LinkRule> Defaults { get; } =
    [
        new(".verified", ".received"),
        new(".approved", ".received"),
        new(".expected", ".actual"),
    ];

    /// <summary>Whether both markers are usable; a rule with a blank half would match everything.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Left) && !string.IsNullOrWhiteSpace(Right);

    /// <summary>How the two halves are written in the rules box, e.g. <c>.verified = .received</c>.</summary>
    public override string ToString() => $"{Left} = {Right}";
}

/// <summary>Which half of a link rule a file matched.</summary>
public enum LinkSide
{
    /// <summary>The expected, committed baseline.</summary>
    Left,

    /// <summary>The newly produced output.</summary>
    Right,
}

/// <summary>The result of matching one file name against the rules.</summary>
/// <param name="Key">The name with the marker removed - what the two halves of a pair share.</param>
/// <param name="Side">Which half this file is.</param>
public readonly record struct FileLink(string Key, LinkSide Side);

/// <summary>Applies <see cref="LinkRule"/>s to file names.</summary>
public static class FileLinker
{
    /// <summary>
    /// Matches a name against the rules, or returns null when no rule applies - which is most files in
    /// most folders, and simply means the file is not half of a pair.
    ///
    /// The FIRST matching rule wins, and the left marker is tried before the right, so a name
    /// containing both (there is no such convention, but nothing stops someone naming a file
    /// <c>a.verified.received.json</c>) resolves deterministically rather than by dictionary order.
    /// </summary>
    public static FileLink? Match(string name, IReadOnlyList<LinkRule> rules, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var rule in rules)
        {
            if (!rule.IsValid)
            {
                continue;
            }

            if (Remove(name, rule.Left, comparison) is { } left)
            {
                return new FileLink(left, LinkSide.Left);
            }

            if (Remove(name, rule.Right, comparison) is { } right)
            {
                return new FileLink(right, LinkSide.Right);
            }
        }

        return null;
    }

    /// <summary>
    /// The name with the first occurrence of the marker removed, or null when it does not contain it.
    /// </summary>
    private static string? Remove(string name, string marker, StringComparison comparison)
    {
        var at = name.IndexOf(marker, comparison);

        return at < 0 ? null : name[..at] + name[(at + marker.Length)..];
    }

    /// <summary>
    /// Parses the rules as a user types them: one per line or separated by commas, each
    /// <c>left = right</c>. Anything unparseable is dropped rather than rejected, for the same reason
    /// a malformed ignore pattern is - these live in a settings file someone can hand-edit.
    /// </summary>
    public static IReadOnlyList<LinkRule> Parse(string text)
    {
        var rules = new List<LinkRule>();

        foreach (var line in text.Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(['=', ':'], 2, StringSplitOptions.TrimEntries);

            if (parts.Length == 2)
            {
                var rule = new LinkRule(parts[0], parts[1]);

                if (rule.IsValid)
                {
                    rules.Add(rule);
                }
            }
        }

        return rules;
    }
}
