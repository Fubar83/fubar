using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Fubar.Controls;

/// <summary>
/// True when a count, or a collection, holds anything.
///
/// <para>For the "hide a header until there is something under it" pattern: a KEY / VALUE /
/// DESCRIPTION strip over an empty grid is three labels and a rule explaining a table that is not
/// there, and several of these sit empty at once on the request editor's own tabs.</para>
///
/// <para>Accepts either an <see cref="int"/> or the collection itself, because a binding to
/// <c>ItemsSource.Count</c> gives one and a binding to <c>ItemsSource</c> gives the other, and having
/// to remember which is a needless way to get an always-hidden header.</para>
/// </summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public static CountToBoolConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            int count => count > 0,
            ICollection collection => collection.Count > 0,
            IEnumerable items => items.GetEnumerator().MoveNext(),
            _ => false,
        };

    /// <summary>Not meaningful: a count cannot be recovered from a bool.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
