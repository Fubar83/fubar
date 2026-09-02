using Avalonia.Headless.XUnit;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;
using Fubar.Diff.UI.Views;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// The open dialog: what a pair of chosen paths means, what dropping does, and that the window itself
/// can be constructed at all.
///
/// That last one earns its place. The first version of this window hand-wrote
/// <c>InitializeComponent</c>, which OVERRIDES the one the XAML compiler generates - and the generated
/// one is what assigns the <c>x:Name</c> fields. So the drop targets were null, the constructor threw
/// an NullReferenceException, and because the caller was an <c>async void</c> click handler the whole
/// process exited. Nothing in the suite caught it; it took clicking the button and watching the app
/// vanish. A test that merely CONSTRUCTS the window would have.
/// </summary>
public class OpenComparisonTests
{
    private sealed class Picker(string? file = null, string? folder = null) : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult(file);

        public Task<IReadOnlyList<string>> PickFilesAsync(string title) =>
            Task.FromResult<IReadOnlyList<string>>(file is null ? [] : [file]);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult(folder);
    }

    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase) { "a.cs", "b.cs" };

    private static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase) { "one", "two" };

    private static OpenComparisonViewModel Build(IFilePickerService? picker = null) =>
        new(picker ?? new Picker(), Files.Contains, Folders.Contains);

    // ---- The window ------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_window_can_be_constructed()
    {
        // See the class comment: this is the test that was missing when the dialog crashed the app.
        var window = new OpenComparisonWindow { DataContext = Build() };

        Assert.NotNull(window);
    }

    // ---- What the pair means ---------------------------------------------------------------------

    [Fact]
    public void Two_files_can_be_compared()
    {
        var model = Build();
        model.LeftPath = "a.cs";
        model.RightPath = "b.cs";

        Assert.True(model.CanCompare);
        Assert.False(model.IsFolderComparison);
        Assert.Equal(string.Empty, model.Message);
    }

    [Fact]
    public void A_file_against_a_folder_is_refused_and_says_why()
    {
        var model = Build();
        model.LeftPath = "a.cs";
        model.RightPath = "one";

        Assert.False(model.CanCompare);
        Assert.True(model.HasProblem);
        Assert.Contains("folder", model.Message);
    }

    [Fact]
    public void Each_side_says_what_it_understood()
    {
        // A typo in a path should be visible BEFORE Compare is pressed, not after.
        var model = Build();
        model.LeftPath = "a.cs";
        model.RightPath = "nope.cs";

        Assert.Equal("File", model.LeftCaption);
        Assert.Equal("Not found", model.RightCaption);
    }

    [Fact]
    public void Compare_is_not_offered_for_a_pair_that_cannot_be_opened()
    {
        var model = Build();

        Assert.False(model.CompareCommand.CanExecute(null));

        model.LeftPath = "a.cs";
        model.RightPath = "b.cs";

        Assert.True(model.CompareCommand.CanExecute(null));
    }

    // ---- Swapping --------------------------------------------------------------------------------

    [Fact]
    public void Swapping_exchanges_the_two_sides()
    {
        var model = Build();
        model.LeftPath = "a.cs";
        model.RightPath = "b.cs";

        model.SwapCommand.Execute(null);

        Assert.Equal("b.cs", model.LeftPath);
        Assert.Equal("a.cs", model.RightPath);
    }

    // ---- Dropping --------------------------------------------------------------------------------

    [Fact]
    public void Dropping_two_at_once_fills_both_sides_whichever_half_they_landed_on()
    {
        // Dragging a pair out of a file manager and letting go is the fastest way to open a
        // comparison; making the user aim at the correct half first would throw that away.
        var model = Build();

        model.Drop(OpenSide.Right, ["a.cs", "b.cs"]);

        Assert.Equal("a.cs", model.LeftPath);
        Assert.Equal("b.cs", model.RightPath);
    }

    [Fact]
    public void Dropping_one_on_a_side_fills_that_side()
    {
        var model = Build();

        model.Drop(OpenSide.Right, ["b.cs"]);

        Assert.Equal(string.Empty, model.LeftPath);
        Assert.Equal("b.cs", model.RightPath);
    }

    [Fact]
    public void Dropping_on_the_window_completes_the_pair_rather_than_replacing_it()
    {
        // A second file dropped after a first should finish the job, not overwrite the first.
        var model = Build();

        model.Drop(["a.cs"]);
        Assert.Equal("a.cs", model.LeftPath);

        model.Drop(["b.cs"]);

        Assert.Equal("a.cs", model.LeftPath);
        Assert.Equal("b.cs", model.RightPath);
    }

    [Fact]
    public void An_empty_drop_changes_nothing()
    {
        var model = Build();
        model.LeftPath = "a.cs";

        model.Drop(OpenSide.Left, []);
        model.Drop(OpenSide.Left, ["   "]);

        Assert.Equal("a.cs", model.LeftPath);
    }

    [Fact]
    public void A_dropped_folder_is_accepted()
    {
        // The main window's own drop handler ignores folders. Here they are meaningful, and the dialog
        // shows what it made of them before anything is opened.
        var model = Build();

        model.Drop(OpenSide.Left, ["one"]);

        Assert.Equal("Folder", model.LeftCaption);
        Assert.True(model.CanCompare);
        Assert.True(model.IsFolderComparison);
    }

    // ---- What Compare asks for -------------------------------------------------------------------

    [Fact]
    public void Comparing_two_files_asks_for_a_file_comparison_with_the_chosen_options()
    {
        var model = Build();
        model.LeftPath = "a.cs";
        model.RightPath = "b.cs";
        model.IgnoreWhitespace = true;
        model.Mode = ComparisonMode.Text;

        OpenComparisonRequest? request = null;
        model.Accepted += (_, r) => request = r;

        model.CompareCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(ComparisonTargetKind.Files, request!.Kind);
        Assert.Equal("a.cs", request.Left);
        Assert.True(request.Options.IgnoreWhitespace);
        Assert.Equal(ComparisonMode.Text, request.Options.Mode);
    }

    [Fact]
    public void One_folder_is_asked_for_as_a_linked_comparison_whichever_box_it_was_in()
    {
        // The folder window takes ONE root in linked mode, so whichever side was filled becomes it -
        // otherwise dropping into the right-hand box would open a comparison of nothing.
        var model = Build();
        model.RightPath = "one";

        OpenComparisonRequest? request = null;
        model.Accepted += (_, r) => request = r;

        model.CompareCommand.Execute(null);

        Assert.Equal(ComparisonTargetKind.LinkedFolder, request!.Kind);
        Assert.Equal("one", request.Left);
        Assert.Equal(string.Empty, request.Right);
    }

    [Fact]
    public void Cancelling_asks_for_nothing()
    {
        var model = Build();
        model.LeftPath = "a.cs";
        model.RightPath = "b.cs";

        var accepted = 0;
        var cancelled = 0;
        model.Accepted += (_, _) => accepted++;
        model.Cancelled += (_, _) => cancelled++;

        model.CancelCommand.Execute(null);

        Assert.Equal(0, accepted);
        Assert.Equal(1, cancelled);
    }

    // ---- Settings --------------------------------------------------------------------------------

    [Fact]
    public void The_dialog_opens_showing_what_would_happen_anyway()
    {
        // The point of "check settings" rather than "set settings": the boxes start from what is
        // saved, so the one comparison that needs something different can have it without a trip to
        // the settings window.
        var model = Build();

        model.ApplyDefaults(AppSettings.Default with
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            Mode = ComparisonMode.Json,
            Recent = [new RecentComparison("a.cs", "b.cs")],
        });

        Assert.True(model.IgnoreWhitespace);
        Assert.True(model.IgnoreComments);
        Assert.Equal(ComparisonMode.Json, model.Mode);
        Assert.True(model.HasRecent);
    }

    [Fact]
    public void A_recent_pair_fills_the_boxes_rather_than_opening_immediately()
    {
        // The commonest reason to reach for a recent pair from HERE is to compare against one of them,
        // or to re-run it with a different setting. Opening straight away would undo the reason for
        // being in this dialog.
        var model = Build();
        var opened = 0;
        model.Accepted += (_, _) => opened++;

        model.UseRecentCommand.Execute(new RecentComparison("a.cs", "b.cs"));

        Assert.Equal("a.cs", model.LeftPath);
        Assert.Equal("b.cs", model.RightPath);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task Browsing_for_a_folder_fills_the_side_it_was_asked_for()
    {
        var model = Build(new Picker(folder: "two"));

        await model.BrowseFolderCommand.ExecuteAsync(OpenSide.Right);

        Assert.Equal("two", model.RightPath);
        Assert.Equal("Folder", model.RightCaption);
    }
}
