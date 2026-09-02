using System;

namespace Fubar.Diff.Core.Files;

/// <summary>What one side of the open dialog is pointing at.</summary>
public enum PathKind
{
    /// <summary>Nothing chosen yet.</summary>
    Empty,

    /// <summary>A file that exists.</summary>
    File,

    /// <summary>A directory that exists.</summary>
    Folder,

    /// <summary>Something was typed or dropped, and it is not on disk.</summary>
    Missing,
}

/// <summary>What comparing the two sides would actually open.</summary>
public enum ComparisonTargetKind
{
    /// <summary>Not enough has been chosen yet. Not an error - the dialog just opened.</summary>
    Incomplete,

    /// <summary>Two files: the ordinary side-by-side comparison.</summary>
    Files,

    /// <summary>Two folders: the folder comparison window.</summary>
    Folders,

    /// <summary>
    /// One folder, whose contents are paired against each other by name - snapshot review, where
    /// <c>Thing.received.json</c> is compared with <c>Thing.verified.json</c> inside a single tree.
    ///
    /// Reachable from this dialog by naming one folder and leaving the other side empty, which is the
    /// shape someone dropping a single folder already has.
    /// </summary>
    LinkedFolder,

    /// <summary>The two sides cannot be compared, and <see cref="ComparisonTarget.Problem"/> says why.</summary>
    Invalid,
}

/// <summary>
/// What the two chosen paths add up to, and what to say when they add up to nothing.
///
/// Pure and in Core for the usual reason - this is the rule, not the dialog. It is also the one place
/// that decides a FILE and a FOLDER cannot be compared with each other, which is worth having in one
/// testable function rather than spread across a Compare button's enabled state and whatever the
/// window does when it is pressed anyway.
/// </summary>
/// <param name="Kind">What would open.</param>
/// <param name="Problem">
/// Why it cannot, phrased for a person and null when it can. <see cref="ComparisonTargetKind.Incomplete"/>
/// carries a prompt rather than a complaint: an empty dialog has not done anything wrong.
/// </param>
public sealed record ComparisonTarget(ComparisonTargetKind Kind, string? Problem)
{
    /// <summary>True when there is something to open.</summary>
    public bool CanCompare => Kind is ComparisonTargetKind.Files
        or ComparisonTargetKind.Folders
        or ComparisonTargetKind.LinkedFolder;

    /// <summary>True when the pair opens the folder window rather than a comparison tab.</summary>
    public bool IsFolders => Kind is ComparisonTargetKind.Folders or ComparisonTargetKind.LinkedFolder;
}

/// <summary>Classifies paths and decides what a pair of them means.</summary>
public static class ComparisonTargets
{
    /// <summary>
    /// What a path points at. The existence checks are passed in rather than reached for, so this
    /// stays a pure function and the rules below can be tested without touching a disk.
    /// </summary>
    public static PathKind Classify(string? path, Func<string, bool> fileExists, Func<string, bool> folderExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(folderExists);

        if (string.IsNullOrWhiteSpace(path))
        {
            return PathKind.Empty;
        }

        var trimmed = path.Trim();

        // Folder first. A directory whose name happens to look like a file - `dist.bak`, or a macOS
        // bundle - must not be taken for one, and File.Exists answers false for a directory anyway.
        if (folderExists(trimmed))
        {
            return PathKind.Folder;
        }

        return fileExists(trimmed) ? PathKind.File : PathKind.Missing;
    }

    /// <summary>
    /// What the two sides add up to.
    ///
    /// The orderings are deliberately symmetric - left and right are interchangeable here, because
    /// the dialog can swap them and a rule that only worked one way round would make the swap button
    /// change the answer.
    /// </summary>
    public static ComparisonTarget Resolve(PathKind left, PathKind right) => (left, right) switch
    {
        // A path that is not on disk is reported before anything else: it is the one problem the user
        // can fix immediately, and letting it fall through to "choose a second file" would send them
        // looking at the wrong side.
        (PathKind.Missing, _) => Invalid("The left path does not exist."),
        (_, PathKind.Missing) => Invalid("The right path does not exist."),

        (PathKind.File, PathKind.File) => new(ComparisonTargetKind.Files, null),
        (PathKind.Folder, PathKind.Folder) => new(ComparisonTargetKind.Folders, null),

        // One folder on its own compares its own contents against each other by name. The other side
        // being empty is what asks for it, so dropping a single folder does something useful rather
        // than waiting for a second one that may not exist.
        (PathKind.Folder, PathKind.Empty) or (PathKind.Empty, PathKind.Folder) =>
            new(ComparisonTargetKind.LinkedFolder, null),

        (PathKind.File, PathKind.Folder) or (PathKind.Folder, PathKind.File) =>
            Invalid("A file cannot be compared against a folder. Choose two files, or two folders."),

        (PathKind.File, PathKind.Empty) or (PathKind.Empty, PathKind.File) =>
            new(ComparisonTargetKind.Incomplete, "Choose a second file to compare against."),

        _ => new(ComparisonTargetKind.Incomplete, "Choose two files, or two folders."),
    };

    private static ComparisonTarget Invalid(string problem) => new(ComparisonTargetKind.Invalid, problem);
}
