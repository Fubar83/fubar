namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// One row of the "array key overrides" list in the settings window: which key identifies elements of
/// the array at <see cref="Path"/>, overriding <see cref="Fubar.Diff.Core.Json.ArrayKeyResolver"/>'s
/// auto-detection for it.
///
/// A record rather than an observable object: the list only ever adds or removes whole entries, never
/// edits one in place (see <c>ComparisonViewModel.AddArrayKeyOverride</c>), so there is nothing for a
/// property setter to notify.
/// </summary>
/// <param name="Path">The array's JSON path, e.g. <c>$.users</c> - matched against <c>JsonPath.ToString()</c>.</param>
/// <param name="Key">The property name that identifies each element, e.g. <c>id</c>.</param>
public sealed record ArrayKeyOverrideEntry(string Path, string Key);
