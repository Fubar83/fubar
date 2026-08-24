using System;
using Avalonia.Controls;

namespace Fubar.Diff.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The diff renderers resolve their brushes from the palette on each render pass, but nothing
        // tells AvaloniaEdit's TextView that a theme swap invalidated what it already painted - so
        // without this the tints keep the old theme's colours until the next scroll.
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => Diff.OnThemeChanged();
}
