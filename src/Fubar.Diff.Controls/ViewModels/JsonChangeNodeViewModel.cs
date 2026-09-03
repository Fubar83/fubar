using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.ViewModels;

/// <summary>
/// One entry in an array row's right-click menu.
/// </summary>
/// <param name="Path">The array this is about.</param>
/// <param name="Key">The field to match elements by, or null to compare by position.</param>
/// <param name="Label">What the menu says.</param>
/// <param name="IsCurrent">Whether this is what the comparison is already doing, for a check mark.</param>
/// <param name="Mode">Which way to compare. <see cref="Key"/> is set only for
/// <see cref="ArrayMatchMode.Key"/>.</param>
public sealed record ArrayKeyOption(string Path, ArrayMatchMode Mode, string? Key, string Label, bool IsCurrent);

/// <summary>
/// One row of the JSON change tree: a path segment, with any nested changes beneath it.
///
/// The tree shows CHANGES rather than the whole document. Rendering every node of a large file with
/// most of them unchanged buries the handful that matter - which is the same reason the text view
/// exists alongside it. The structure comes from the change paths, so a change at
/// <c>$.users[2].email</c> appears under <c>users</c>, then <c>[2]</c>.
/// </summary>
public sealed partial class JsonChangeNodeViewModel : ObservableObject
{
    private readonly List<JsonChangeNodeViewModel> _children = [];

    private JsonChangeNodeViewModel(string label) => Label = label;

    /// <summary>The row above this one, or null at the top. Exists so a row can open its own ancestors
    /// when navigation lands on it - see <see cref="Reveal"/>.</summary>
    public JsonChangeNodeViewModel? Parent { get; private set; }

    /// <summary>
    /// Whether this row is open, owned by the view model rather than only by the container.
    ///
    /// It has to live here because navigation needs to OPEN a row it did not draw: stepping to a
    /// difference five levels down selects a node the reader cannot see while its ancestors are shut.
    /// Two-way, so expanding a row by hand is still the row's own state and not something the next
    /// navigation silently disagrees with. Defaults to closed, which is exactly what the tree did
    /// before this existed.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// Opens every row between this one and the top, so a selection made by navigation is actually on
    /// screen.
    ///
    /// <para>Selecting a node inside collapsed ancestors is a selection nobody can see - which is what
    /// stepping through differences looked like before: the tree agreed it had moved and showed
    /// nothing. This opens the ancestors and deliberately does NOT open the node itself; a change row
    /// has no children worth unfolding, and unfolding a group would bury the row that was selected
    /// under its own contents.</para>
    /// </summary>
    public void Reveal()
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.IsExpanded = true;
        }
    }

    /// <summary>This step of the path, e.g. <c>users</c> or <c>[2]</c>.</summary>
    public string Label { get; }

    /// <summary>The change at exactly this path, or null for an intermediate grouping row.</summary>
    public JsonChange? Change { get; private set; }

    /// <summary>
    /// The full path of this row, in ignore-rule syntax - <c>$.items[2].updatedAt</c>.
    ///
    /// Set on grouping rows too, so a user can ignore a whole object from the row that represents it
    /// rather than having to ignore each of its fields.
    /// </summary>
    public string Path { get; private set; } = "$";

    /// <summary>The rule this row would create - the path with array indices generalized to [*].</summary>
    public string IgnorePath => JsonPathPattern.Generalize(Path);

    public IReadOnlyList<JsonChangeNodeViewModel> Children => _children;

    /// <summary>True for rows that are a change in their own right, rather than just a grouping.</summary>
    public bool IsChange => Change is not null;

    /// <summary>What happened here, for tinting. Grouping rows have no kind of their own.</summary>
    public ChangeKind Kind => Change?.Kind ?? ChangeKind.Unchanged;

    // One bool per style class rather than a class name: Avalonia's Classes property is not bindable,
    // so styles are applied as Classes.name="{Binding Flag}".

    public bool IsInserted => Kind == ChangeKind.Inserted;

    public bool IsDeleted => Kind == ChangeKind.Deleted;

    public bool IsModified => Kind == ChangeKind.Modified;

    /// <summary>True when a rule covers this row, so it can be shown dimmed rather than dropped.</summary>
    public bool IsIgnored => Change?.IsIgnored ?? false;

    /// <summary>
    /// Whether to offer "ignore" on this row.
    ///
    /// Only on rows that ARE a difference. A grouping row like <c>items</c> or <c>[0]</c> exists to
    /// give the tree its shape, and offering to ignore one invites hiding a whole subtree from a row
    /// that shows no change of its own - far more than the click looks like it does.
    /// </summary>
    public bool ShowIgnore => IsChange;

    /// <summary>Ignoring is only offered for a row not already covered by a rule.</summary>
    public bool CanIgnore => !IsIgnored;

    /// <summary>
    /// What this row's array could be matched by, or null when the row is not an array.
    ///
    /// Every array row in the tree has differences beneath it by construction - the tree is built from
    /// change paths - so there is no separate "has differences" test to make.
    /// </summary>
    public ArrayKeyChoices? ArrayChoices { get; private set; }

    /// <summary>True when a right-click here has something to offer.</summary>
    public bool IsArray => ArrayChoices is not null;

    /// <summary>
    /// The entries a right-click offers: match by position, then the suggested key, then every other
    /// field that could serve as one.
    ///
    /// The suggestion is first because it is almost always right - it is the same answer the
    /// comparison is already using - and labelled as such so choosing it is not a shot in the dark.
    /// </summary>
    public IReadOnlyList<ArrayKeyOption> ArrayKeyOptions
    {
        get
        {
            if (ArrayChoices is not { } choices)
            {
                return [];
            }

            var options = new List<ArrayKeyOption>
            {
                new(choices.Path, ArrayMatchMode.Position, null, "Compare by position",
                    choices.Current == ArrayMatchMode.Position),

                // Offered for EVERY array, including one of plain strings - which is the case that had
                // no answer before, since a set of tags or roles has no field to be keyed by and could
                // only be compared by position.
                new(choices.Path, ArrayMatchMode.Unordered, null, "Ignore order (match equal values)",
                    choices.Current == ArrayMatchMode.Unordered),
            };

            if (choices.Suggested is { } suggested)
            {
                options.Add(new ArrayKeyOption(
                    choices.Path, ArrayMatchMode.Key, suggested, $"Match by {suggested}  (suggested)",
                    choices.Current == ArrayMatchMode.Key && IsKey(suggested)));
            }

            foreach (var candidate in choices.Candidates)
            {
                if (!string.Equals(candidate, choices.Suggested, StringComparison.Ordinal))
                {
                    options.Add(new ArrayKeyOption(
                        choices.Path, ArrayMatchMode.Key, candidate, $"Match by {candidate}",
                        choices.Current == ArrayMatchMode.Key && IsKey(candidate)));
                }
            }

            bool IsKey(string name) => string.Equals(name, choices.Suggested, StringComparison.Ordinal);

            return options;
        }
    }

    /// <summary>A short summary of the change, shown beside the label.</summary>
    public string Summary => Change is null
        ? string.Empty
        : Change.Kind switch
        {
            ChangeKind.Inserted => $"added  {Describe(Change.Right)}",
            ChangeKind.Deleted => $"removed  {Describe(Change.Left)}",
            _ when Change.IsReorder => "moved",
            _ => $"{Describe(Change.Left)}  →  {Describe(Change.Right)}",
        };

    /// <summary>
    /// Builds the tree from a flat change list.
    ///
    /// Each change's path is walked from the root, creating grouping rows on the way down, so nothing
    /// depends on the changes arriving in any particular order.
    ///
    /// Also returns the path index used to build it, keyed the same way <see cref="Path"/> is - the
    /// Hybrid view uses it to find the tree row for a given <see cref="JsonChange"/> without a second
    /// walk of the tree.
    /// </summary>
    public static (IReadOnlyList<JsonChangeNodeViewModel> Roots, IReadOnlyDictionary<string, JsonChangeNodeViewModel> ByPath)
        Build(IReadOnlyList<JsonChange> changes, IReadOnlyDictionary<string, ArrayKeyChoices>? arrayKeys = null)
    {
        var root = new JsonChangeNodeViewModel("$");
        var index = new Dictionary<string, JsonChangeNodeViewModel> { ["$"] = root };

        foreach (var change in changes)
        {
            EnsureNode(change.Path, index, root).Change = change;
        }

        // Annotated afterwards rather than while building, because a row can be created as a grouping
        // step on the way to a deeper change - $.users exists because $.users[2].email did - and it is
        // still the array a right-click should offer options for.
        if (arrayKeys is not null)
        {
            foreach (var (path, node) in index)
            {
                if (arrayKeys.TryGetValue(GeneralizeIndices(path), out var choices)
                    || arrayKeys.TryGetValue(path, out choices))
                {
                    node.ArrayChoices = choices;
                }
            }
        }

        return (root._children, index);
    }

    /// <summary>
    /// The scanner walks the FIRST element of each array to reach nested ones, so a nested array is
    /// recorded at <c>$.groups[0].items</c> while the tree may have rows for <c>[2]</c> and <c>[7]</c>
    /// too. Trying the first-element form is what lets those rows find their choices; the arrays
    /// themselves are the same shape at every index, which is the assumption keying an array rests on
    /// in the first place.
    /// </summary>
    private static string GeneralizeIndices(string path)
    {
        var generalized = System.Text.RegularExpressions.Regex.Replace(
            path,
            @"\[\d+\]",
            "[0]",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(100));

        return generalized;
    }

    private static JsonChangeNodeViewModel EnsureNode(
        JsonPath path,
        Dictionary<string, JsonChangeNodeViewModel> index,
        JsonChangeNodeViewModel root)
    {
        var key = path.ToString();
        if (index.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var parent = path.Parent is { } parentPath ? EnsureNode(parentPath, index, root) : root;

        var node = new JsonChangeNodeViewModel(path.Label) { Path = key, Parent = parent };
        parent._children.Add(node);
        index[key] = node;

        return node;
    }

    /// <summary>
    /// A one-line rendering of a value. Containers are summarised rather than expanded - the tree
    /// already shows their structure through child rows.
    /// </summary>
    private static string Describe(JsonAstNode? node) => node switch
    {
        null => string.Empty,
        JsonAstScalar scalar => scalar.Kind == JsonAstKind.String ? $"\"{scalar.Value}\"" : scalar.RawText,
        JsonAstObject obj => $"{{ {obj.Properties.Count} propert{(obj.Properties.Count == 1 ? "y" : "ies")} }}",
        JsonAstArray array => $"[ {array.Items.Count} item{(array.Items.Count == 1 ? string.Empty : "s")} ]",
        _ => string.Empty,
    };
}
