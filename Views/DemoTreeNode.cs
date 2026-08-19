using System.Collections.Generic;

namespace Fubar.Controls.Gallery.Views;

/// <summary>
/// A trivial hierarchical node for the TreeView/Section gallery demo: a name, an optional HTTP-style
/// method label + dirty flag (to show badge/dot adornments), and children.
/// </summary>
public sealed class DemoTreeNode(string name, string? method = null, bool isDirty = false)
{
    public string Name { get; } = name;

    public string? Method { get; } = method;

    public bool IsDirty { get; } = isDirty;

    public List<DemoTreeNode> Children { get; } = [];

    public DemoTreeNode With(params DemoTreeNode[] children)
    {
        Children.AddRange(children);
        return this;
    }
}
