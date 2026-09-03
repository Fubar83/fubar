using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Settings;

namespace Fubar.Diff.Infrastructure.Settings;

/// <summary>
/// Finds <c>.fubardiff.json</c> by walking up from the file being compared, the way every tool that
/// keeps its rules beside the code does - .editorconfig, .gitignore, .eslintrc.
///
/// Walking up rather than looking in one place is what makes it useful in a monorepo: a config at the
/// root covers everything, and one in a subdirectory covers the part of the tree that needs something
/// different. The FIRST one found wins outright rather than being merged with the ones above it,
/// which is the simpler promise to reason about - the file you are looking at is the file that
/// applies.
/// </summary>
public sealed class FileSystemProjectConfigStore : IProjectConfigStore
{
    public const string FileName = ".fubardiff.json";

    /// <summary>
    /// How far up to look. Deep enough for any real repository, bounded so a path on a mounted share
    /// that keeps answering cannot turn a comparison into a directory crawl.
    /// </summary>
    private const int MaxDepth = 64;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public ProjectConfig Find(string? path, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return ProjectConfig.Empty;
        }

        try
        {
            var directory = Directory.Exists(path)
                ? new DirectoryInfo(path)
                : new FileInfo(Path.GetFullPath(path)).Directory;

            for (var depth = 0; directory is not null && depth < MaxDepth; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, FileName);

                if (File.Exists(candidate))
                {
                    return Read(candidate, out problem);
                }
            }
        }
        catch (IOException)
        {
            // A path that cannot be walked is not a reason to refuse a comparison; it just means
            // there are no project rules. Same for the two below.
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ArgumentException)
        {
        }

        return ProjectConfig.Empty;
    }

    /// <summary>
    /// Reads one config file. A broken one is reported and then ignored - refusing to compare two
    /// files because a rules file has a trailing comma in it would be the wrong trade every time, and
    /// the user needs to be told rather than left wondering why their rules stopped working.
    /// </summary>
    private static ProjectConfig Read(string path, out string? problem)
    {
        problem = null;

        try
        {
            var file = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path), ReadOptions);

            if (file is null)
            {
                return ProjectConfig.Empty;
            }

            var rules = new List<ProjectRule>(file.Rules?.Count ?? 0);

            foreach (var rule in file.Rules ?? [])
            {
                // A rule with no files pattern cannot be matched against anything, so it is dropped
                // rather than silently applied to everything - which is what a typo in "files" would
                // otherwise do.
                if (!string.IsNullOrWhiteSpace(rule.Files))
                {
                    rules.Add(ToRule(rule));
                }
            }

            return new ProjectConfig(ToRule(file), rules);
        }
        catch (JsonException failure)
        {
            problem = $"{path} could not be read: {failure.Message}";
        }
        catch (IOException failure)
        {
            problem = $"{path} could not be read: {failure.Message}";
        }
        catch (UnauthorizedAccessException failure)
        {
            problem = $"{path} could not be read: {failure.Message}";
        }

        return ProjectConfig.Empty;
    }

    private static ProjectRule ToRule(RuleFile rule) => new()
    {
        Files = rule.Files,
        Mode = ParseMode(rule.Mode),
        IgnoreWhitespace = rule.IgnoreWhitespace,
        IgnoreCase = rule.IgnoreCase,
        IgnoreComments = rule.IgnoreComments,
        IgnoreBlankLines = rule.IgnoreBlankLines,
        IgnoredLinePatterns = rule.IgnoredLinePatterns ?? [],
        IgnoredPaths = rule.IgnoredPaths ?? [],
        ArrayKeys = rule.ArrayKeys ?? new Dictionary<string, string>(StringComparer.Ordinal),
        UnorderedArrays = rule.UnorderedArrays ?? [],
    };

    /// <summary>
    /// An unrecognised mode is ignored rather than rejected: a config written for a later version
    /// naming a format this build does not have should lose that one line, not the whole file.
    /// </summary>
    private static ComparisonMode? ParseMode(string? mode) =>
        Enum.TryParse<ComparisonMode>(mode, ignoreCase: true, out var parsed) ? parsed : null;

    /// <summary>
    /// The file's own shape. Separate from <see cref="ProjectRule"/> so the on-disk format is a
    /// decision of its own: nullable everywhere, and never coupled to a Core type that might want to
    /// change without breaking every checked-in config in the world.
    /// </summary>
    private class RuleFile
    {
        public string? Files { get; set; }

        public string? Mode { get; set; }

        public bool? IgnoreWhitespace { get; set; }

        public bool? IgnoreCase { get; set; }

        public bool? IgnoreComments { get; set; }

        public bool? IgnoreBlankLines { get; set; }

        public List<string>? IgnoredLinePatterns { get; set; }

        public List<string>? IgnoredPaths { get; set; }

        public Dictionary<string, string>? ArrayKeys { get; set; }

        public List<string>? UnorderedArrays { get; set; }
    }

    private sealed class ConfigFile : RuleFile
    {
        public List<RuleFile>? Rules { get; set; }
    }
}
