using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace Fubar.Controls;

/// <summary>
/// A single-line filter input with a leading search glyph and a trailing clear (&#x2715;) button - the
/// latter an <see cref="IconButton"/> that appears only while <see cref="Text"/> is non-empty and
/// resets it on click. <see cref="Text"/> is two-way by default so callers just bind it to their query
/// property; <see cref="Watermark"/> sets the placeholder.
/// </summary>
[TemplatePart("PART_ClearButton", typeof(Button))]
public class SearchBox : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SearchBox, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<SearchBox, string?>(nameof(Watermark), "Search");

    private Button? _clearButton;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(SearchBox);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_clearButton is not null)
        {
            _clearButton.Click -= OnClearClicked;
        }

        _clearButton = e.NameScope.Find<Button>("PART_ClearButton");

        if (_clearButton is not null)
        {
            _clearButton.Click += OnClearClicked;
        }
    }

    private void OnClearClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Text = string.Empty;
}
