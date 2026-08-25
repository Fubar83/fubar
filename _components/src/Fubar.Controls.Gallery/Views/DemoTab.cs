namespace Fubar.Controls.Gallery.Views;

/// <summary>A trivial tab model for the TabStrip drag demo - just a title.</summary>
public sealed class DemoTab(string title)
{
    public string Title { get; } = title;
}
