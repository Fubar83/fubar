using System.Globalization;
using Avalonia.Data.Converters;

namespace Fubar.Controls;

/// <summary>Multi-value equality check - true when both bound values are equal and non-null. Useful for
/// "this row is the currently-selected one" highlighting where the two values live on different
/// DataContexts and so can't be compared with a single Binding. Reference via
/// <c>{x:Static fc:EqualityConverter.Instance}</c>.</summary>
public sealed class EqualityConverter : IMultiValueConverter
{
    public static readonly EqualityConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [{ } a, { } b] && Equals(a, b);
}
