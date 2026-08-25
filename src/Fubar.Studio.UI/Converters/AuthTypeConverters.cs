using System.Globalization;
using Avalonia.Data.Converters;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.Converters;

/// <summary>Simple <see cref="AuthType"/> equality converters for the Auth tab's explanatory text
/// (RequestEditorPane.md §5) - avoids a per-value bool property on <c>RequestAuthViewModel</c> for
/// cases that are purely static text, not another editable sub-form.</summary>
public static class AuthTypeConverters
{
    public static readonly IValueConverter IsInherit = new FuncValueConverter<AuthType, bool>(t => t == AuthType.Inherit);

    public static readonly IValueConverter IsNone = new FuncValueConverter<AuthType, bool>(t => t == AuthType.None);
}
