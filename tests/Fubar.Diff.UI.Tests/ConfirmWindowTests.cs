using Avalonia.Headless.XUnit;
using Fubar.Diff.UI.Views;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// The confirmation dialog.
///
/// It exists to stand between the user and the only operation in this app that can destroy a file
/// they did not name, and it is reached only by starting a copy - which no other test does. A mistake
/// in its XAML would be a runtime failure at exactly that moment, so what this mainly checks is that
/// it loads and shows what it was given.
/// </summary>
public class ConfirmWindowTests
{
    [AvaloniaFact]
    public void It_loads_and_shows_what_it_was_given()
    {
        var window = new ConfirmWindow("Replace 2 files?", @"C:\a will be written to C:\b", ["Replace"]);

        window.Show();
        window.UpdateLayout();

        Assert.Equal("Replace 2 files?", window.Title);

        window.Close();
    }
}
