using System.Collections.Generic;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.ViewModels;

/// <summary>
/// One row of the JSON change tree: a path segment, with any nested changes beneath it.
///
/// The tree shows CHANGES rather than the whole document. Rendering every node of a large file with
/// most of them unchanged buries the handful that matter - which is the same reason the text view
/// exists alongside it. The structure comes from the change paths, so a change at
/// <c>$.users[2].email</c> appears under <c>users</c>, then <c>[2]</c>.
/// </summary>
public sealed class JsonChangeNodeViewModel
{
    private readonly List<JsonChangeNodeViewModel> _children = [];

    private JsonChangeNodeViewModel(string label) => Label = label;

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

    /// <summary>Ignoring is only offered for a row not already covered by a rule.</summary>
    public bool CanIgnore => !IsIgnored;

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
    /// </summary>
    public static IReadOnlyList<JsonChangeNodeViewModel> Build(IReadOnlyList<JsonChange> changes)
    {
        var root = new JsonChangeNodeViewModel("$");
        var index = new Dictionary<string, JsonChangeNodeViewModel> { ["$"] = root };

        foreach (var change in changes)
        {
            EnsureNode(change.Path, index, root).Change = change;
        }

        return root._children;
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

        var node = new JsonChangeNodeViewModel(path.Label) { Path = key };
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
