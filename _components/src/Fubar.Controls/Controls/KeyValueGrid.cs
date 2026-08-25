using System;
using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;

namespace Fubar.Controls;

/// <summary>
/// A dumb, data-driven editable key/value(/description) grid - the Params / form-data / URL-encoded
/// editor shape. Bind <see cref="ItemsSource"/> to a collection of row objects that expose
/// <c>Enabled</c> (bool), <c>Key</c>, <c>Value</c> and (optionally) <c>Description</c> (strings); the
/// default cells two-way bind to those members. Add/remove are surfaced as <see cref="AddCommand"/> and
/// <see cref="RemoveCommand"/> (the latter invoked with the row) so the control owns no logic itself.
///
/// For cells that need richer content (e.g. a variable-highlighting textbox), supply
/// <see cref="KeyCellTemplate"/> / <see cref="ValueCellTemplate"/> / <see cref="DescriptionCellTemplate"/>;
/// when a template is set it replaces that column's default TextBox, with the row as its DataContext.
/// </summary>
public class KeyValueGrid : TemplatedControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<KeyValueGrid, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> KeyHeaderProperty =
        AvaloniaProperty.Register<KeyValueGrid, string>(nameof(KeyHeader), "KEY");

    public static readonly StyledProperty<string> ValueHeaderProperty =
        AvaloniaProperty.Register<KeyValueGrid, string>(nameof(ValueHeader), "VALUE");

    public static readonly StyledProperty<string> DescriptionHeaderProperty =
        AvaloniaProperty.Register<KeyValueGrid, string>(nameof(DescriptionHeader), "DESCRIPTION");

    public static readonly StyledProperty<bool> ShowDescriptionProperty =
        AvaloniaProperty.Register<KeyValueGrid, bool>(nameof(ShowDescription), true);

    public static readonly StyledProperty<bool> ShowEnabledProperty =
        AvaloniaProperty.Register<KeyValueGrid, bool>(nameof(ShowEnabled), true);

    public static readonly StyledProperty<string> AddButtonTextProperty =
        AvaloniaProperty.Register<KeyValueGrid, string>(nameof(AddButtonText), "+ Add");

    public static readonly StyledProperty<ICommand?> AddCommandProperty =
        AvaloniaProperty.Register<KeyValueGrid, ICommand?>(nameof(AddCommand));

    public static readonly StyledProperty<ICommand?> RemoveCommandProperty =
        AvaloniaProperty.Register<KeyValueGrid, ICommand?>(nameof(RemoveCommand));

    public static readonly StyledProperty<IEnumerable?> KeySuggestionsProperty =
        AvaloniaProperty.Register<KeyValueGrid, IEnumerable?>(nameof(KeySuggestions));

    public static readonly StyledProperty<IDataTemplate?> KeyCellTemplateProperty =
        AvaloniaProperty.Register<KeyValueGrid, IDataTemplate?>(nameof(KeyCellTemplate));

    public static readonly StyledProperty<IDataTemplate?> ValueCellTemplateProperty =
        AvaloniaProperty.Register<KeyValueGrid, IDataTemplate?>(nameof(ValueCellTemplate));

    public static readonly StyledProperty<IDataTemplate?> DescriptionCellTemplateProperty =
        AvaloniaProperty.Register<KeyValueGrid, IDataTemplate?>(nameof(DescriptionCellTemplate));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string KeyHeader
    {
        get => GetValue(KeyHeaderProperty);
        set => SetValue(KeyHeaderProperty, value);
    }

    public string ValueHeader
    {
        get => GetValue(ValueHeaderProperty);
        set => SetValue(ValueHeaderProperty, value);
    }

    public string DescriptionHeader
    {
        get => GetValue(DescriptionHeaderProperty);
        set => SetValue(DescriptionHeaderProperty, value);
    }

    /// <summary>Whether the Description column is shown.</summary>
    public bool ShowDescription
    {
        get => GetValue(ShowDescriptionProperty);
        set => SetValue(ShowDescriptionProperty, value);
    }

    /// <summary>Whether the leading enabled-checkbox column is shown.</summary>
    public bool ShowEnabled
    {
        get => GetValue(ShowEnabledProperty);
        set => SetValue(ShowEnabledProperty, value);
    }

    public string AddButtonText
    {
        get => GetValue(AddButtonTextProperty);
        set => SetValue(AddButtonTextProperty, value);
    }

    public ICommand? AddCommand
    {
        get => GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    /// <summary>Invoked with the row object when its delete button is clicked.</summary>
    public ICommand? RemoveCommand
    {
        get => GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    /// <summary>Optional candidate keys for a key-cell autocomplete (e.g. common HTTP header names, or
    /// an operation's parameter names). When set - and no <see cref="KeyCellTemplate"/> is supplied - the
    /// default key cell becomes an autocomplete box offering these as you type.</summary>
    public IEnumerable? KeySuggestions
    {
        get => GetValue(KeySuggestionsProperty);
        set => SetValue(KeySuggestionsProperty, value);
    }

    public IDataTemplate? KeyCellTemplate
    {
        get => GetValue(KeyCellTemplateProperty);
        set => SetValue(KeyCellTemplateProperty, value);
    }

    public IDataTemplate? ValueCellTemplate
    {
        get => GetValue(ValueCellTemplateProperty);
        set => SetValue(ValueCellTemplateProperty, value);
    }

    public IDataTemplate? DescriptionCellTemplate
    {
        get => GetValue(DescriptionCellTemplateProperty);
        set => SetValue(DescriptionCellTemplateProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(KeyValueGrid);
}
