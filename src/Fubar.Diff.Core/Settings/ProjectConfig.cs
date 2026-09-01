using System;
using System.Collections.Generic;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Folders;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Core.Settings;

/// <summary>
/// Comparison rules that belong to a REPOSITORY rather than to a machine - <c>.fubardiff.json</c>,
/// checked in beside the code it describes.
///
/// The gap this fills: the interesting rules in this app are facts about particular files. "The
/// requestId in our snapshots changes every run." "Our users array is keyed by id." "Compare the
/// generated client as text, it is minified." Every one of those is true for the whole team and for
/// every checkout, and until now each person had to discover it and set it up again by hand, in their
/// own settings, on every machine. Beyond Compare's rules are per-machine for the same reason: nobody
/// thought to make them travel.
///
/// Deliberately NOT a second copy of the settings window. What belongs here is what is true of the
/// files; what stays in settings is what is true of the reader - the theme, whether to reload on
/// change, how the Pretty button lays a document out.
/// </summary>
/// <param name="Defaults">Rules for every file the config covers.</param>
/// <param name="Rules">
/// Rules for particular files, applied in order after the defaults, so a later one wins. Matching is
/// by file NAME pattern (<c>*.json</c>, <c>*.min.js</c>) rather than by path glob - see
/// <see cref="NamePattern"/> for why this app does not implement <c>**/</c>.
/// </param>
public sealed record ProjectConfig(ProjectRule Defaults, IReadOnlyList<ProjectRule> Rules)
{
    /// <summary>A config that says nothing, which is what "no file found" resolves to.</summary>
    public static ProjectConfig Empty { get; } = new(new ProjectRule(), []);

    /// <summary>True when this config would change nothing, so a caller can skip it entirely.</summary>
    public bool IsEmpty => Defaults.IsEmpty && Rules.Count == 0;

    /// <summary>
    /// Everything that applies to one file: the defaults, then every rule whose pattern matches it.
    ///
    /// Later rules win on the single-value settings and ADD to the list ones. That asymmetry is the
    /// only sensible reading of what the two kinds mean: two rules disagreeing about the comparison
    /// mode need one of them to win, while two rules each naming a field to ignore both meant it.
    /// </summary>
    public ProjectRule For(string? path)
    {
        var resolved = Defaults;
        var name = path is null ? string.Empty : System.IO.Path.GetFileName(path);

        foreach (var rule in Rules)
        {
            if (rule.Files is { } pattern && NamePattern.Matches(name, pattern, ignoreCase: true))
            {
                resolved = resolved.Merge(rule);
            }
        }

        return resolved;
    }
}

/// <summary>
/// One set of rules, either the defaults or a rule for matching files.
///
/// Every single-value setting is nullable, and that is load-bearing: null means "this file says
/// nothing about it", which is what lets a rule override one thing without silently asserting
/// defaults for everything else it did not mention.
/// </summary>
public sealed record ProjectRule
{
    /// <summary>The file-name pattern this rule applies to. Null on the defaults, which apply to all.</summary>
    public string? Files { get; init; }

    /// <summary>How to compare - see <see cref="ComparisonMode"/>.</summary>
    public ComparisonMode? Mode { get; init; }

    public bool? IgnoreWhitespace { get; init; }

    public bool? IgnoreCase { get; init; }

    public bool? IgnoreComments { get; init; }

    public bool? IgnoreBlankLines { get; init; }

    /// <summary>Regular expressions whose matches stop counting - see <c>LinePatternMask</c>.</summary>
    public IReadOnlyList<string> IgnoredLinePatterns { get; init; } = [];

    /// <summary>JSON paths whose differences are never reported.</summary>
    public IReadOnlyList<string> IgnoredPaths { get; init; } = [];

    /// <summary>Which field identifies the elements of an array, by the array's path.</summary>
    public IReadOnlyDictionary<string, string> ArrayKeys { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>True when this rule asserts nothing at all.</summary>
    public bool IsEmpty =>
        Mode is null
        && IgnoreWhitespace is null
        && IgnoreCase is null
        && IgnoreComments is null
        && IgnoreBlankLines is null
        && IgnoredLinePatterns.Count == 0
        && IgnoredPaths.Count == 0
        && ArrayKeys.Count == 0;

    /// <summary>This rule with <paramref name="other"/> laid over it. See <see cref="ProjectConfig.For"/>.</summary>
    public ProjectRule Merge(ProjectRule other)
    {
        var keys = new Dictionary<string, string>(ArrayKeys, StringComparer.Ordinal);
        foreach (var (path, key) in other.ArrayKeys)
        {
            keys[path] = key;
        }

        return new ProjectRule
        {
            Files = Files,
            Mode = other.Mode ?? Mode,
            IgnoreWhitespace = other.IgnoreWhitespace ?? IgnoreWhitespace,
            IgnoreCase = other.IgnoreCase ?? IgnoreCase,
            IgnoreComments = other.IgnoreComments ?? IgnoreComments,
            IgnoreBlankLines = other.IgnoreBlankLines ?? IgnoreBlankLines,
            IgnoredLinePatterns = [.. IgnoredLinePatterns, .. other.IgnoredLinePatterns],
            IgnoredPaths = [.. IgnoredPaths, .. other.IgnoredPaths],
            ArrayKeys = keys,
        };
    }

    /// <summary>
    /// Lays this rule over a set of comparison options.
    ///
    /// The list settings are ADDED to whatever the session already has rather than replacing it: a
    /// path the user chose to ignore for this comparison, and a path the repository says is never
    /// worth reporting, are both true at once. The single-value ones are set, because there is only
    /// one answer to "how should this be compared".
    /// </summary>
    public ComparisonOptions ApplyTo(ComparisonOptions options)
    {
        // The overwhelmingly common case - no config file anywhere - returns the caller's own options
        // untouched rather than an identical copy with fresh lists in it. Every comparison in every
        // repository without a .fubardiff.json goes through here.
        if (IsEmpty)
        {
            return options;
        }

        var keys = new Dictionary<string, string>(options.Json.ArrayKeyOverrides, StringComparer.Ordinal);
        foreach (var (path, key) in ArrayKeys)
        {
            keys[path] = key;
        }

        return options with
        {
            Mode = Mode ?? options.Mode,
            IgnoreWhitespace = IgnoreWhitespace ?? options.IgnoreWhitespace,
            IgnoreCase = IgnoreCase ?? options.IgnoreCase,
            IgnoredLinePatterns = [.. options.IgnoredLinePatterns, .. IgnoredLinePatterns],
            Code = options.Code with
            {
                IgnoreComments = IgnoreComments ?? options.Code.IgnoreComments,
                IgnoreBlankLines = IgnoreBlankLines ?? options.Code.IgnoreBlankLines,
            },
            Json = options.Json with
            {
                IgnoredPaths = [.. options.Json.IgnoredPaths, .. IgnoredPaths],
                ArrayKeyOverrides = keys,
            },
        };
    }
}

/// <summary>
/// PORT. Finds the project config that governs a file, if there is one.
///
/// A port because finding it means walking up the directory tree, which is I/O and belongs to an
/// adapter - and because a host that has no file system (a test, an embedded comparison) should be
/// able to answer "there is none" without one.
/// </summary>
public interface IProjectConfigStore
{
    /// <summary>
    /// The config governing <paramref name="path"/>, or <see cref="ProjectConfig.Empty"/> when there
    /// is none.
    ///
    /// Never throws: a malformed config file is reported through <paramref name="problem"/> and
    /// treated as absent. Refusing to compare two files because a rules file has a typo in it would be
    /// the wrong trade every time.
    /// </summary>
    ProjectConfig Find(string? path, out string? problem);
}
