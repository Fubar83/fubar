using System.Globalization;
using Avalonia.Data.Converters;

namespace Fubar.Studio.UI.Converters;

/// <summary>Maps <c>EnvironmentVariableRowViewModel.IsSecretKind</c> to a TextBox's <c>PasswordChar</c>
/// - masked while true, plain while false.</summary>
public sealed class SecretMaskConverter : IValueConverter
{
    public static readonly SecretMaskConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? '•' : '\0';

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
