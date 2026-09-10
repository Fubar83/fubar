using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// Every command a view model exposes must be reachable from somewhere.
///
/// <para>"Built but never wired" is this repository's recurring failure and CLAUDE.md closes on it:
/// <c>WorkspaceExplorerViewModel.NewWorkspaceAsync</c> worked and was bound to nothing, so the app
/// could be installed and then not started; Fubar Diff had Core comparison options fully built with
/// persistence fields waiting while <c>ComparisonViewModel</c> never read them; and
/// <c>ThemeManagerViewModel</c> was complete, registered, documented as driving the left pane's theme
/// switcher, and referenced by no <c>.axaml</c> at all - the theme could be persisted and applied and
/// never chosen.</para>
///
/// <para>Three instances, and the standing advice was "before concluding one is DONE, grep for a
/// binding to it" - which is a habit, not a control. This is the control.</para>
/// </summary>
public class WiringTests
{
    /// <summary>
    /// Commands that genuinely have no XAML binding, each with the reason. Anything not here must be
    /// reachable; anything here must say why it is not.
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        // Reached from the command palette, which builds its entries in C# rather than in markup. Its
        // own button and flyout are gone: the overflow menu they opened held this one item, which is a
        // button plus a flyout for a single action on the row where space is worth most.
        ["RequestEditorViewModel.CopyAsCurlCommand"] =
            "Invoked from MainViewModel.PaletteEntries, not from markup.",

        ["RequestEditorViewModel.CopyAsCurlForPowerShellCommand"] =
            "Invoked from MainViewModel.PaletteEntries, not from markup - beside the POSIX one.",
    };

    /// <summary>
    /// Reachable means bound in markup OR executed from code-behind.
    ///
    /// <para>Markup alone was too narrow, and said so loudly on the first run: nine of the ten it
    /// flagged - the rename commit/cancel pairs, Edit, ActivateSelection - are invoked from a KeyDown
    /// or Tapped handler, which is the correct way to drive a command from a gesture. Exempting all
    /// nine would have buried the ONE that was genuinely orphaned
    /// (<c>SelectWorkspaceCommand</c>, since deleted) in a list of false positives, and an exemption
    /// list that long stops being read.</para>
    /// </summary>
    [Fact]
    public void Every_public_command_is_reachable()
    {
        var markup = LoadAllMarkup();

        var unreachable = ViewModelTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => typeof(IRelayCommand).IsAssignableFrom(p.PropertyType))
                .Select(p => (Qualified: $"{type.Name}.{p.Name}", p.Name)))
            .Where(c => !Exempt.ContainsKey(c.Qualified))
            // A command is reachable if its name appears in any markup file: bindings are written as
            // {Binding FooCommand} or {Binding Section.FooCommand}, so the name alone is the honest,
            // low-false-negative check. It cannot prove the binding is CORRECT - only that the command
            // is not orphaned, which is the failure that keeps happening.
            .Where(c => !markup.Contains(c.Name, StringComparison.Ordinal))
            .Select(c => c.Qualified)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unreachable.Length == 0,
            "These commands are built and bound to nothing, which is this repository's recurring bug "
            + $"(see CLAUDE.md): {string.Join(", ", unreachable)}. Bind them, delete them, or add them "
            + $"to {nameof(Exempt)} with the reason.");
    }

    // A second test asserting "one public constructor per view model" was written here and removed the
    // same day. It flagged two types: StatusLogViewModel, where the ambiguity was real (it IS resolved
    // from DI, and a container picking the greediest resolvable constructor is a silent hazard - fixed
    // by collapsing it to one constructor with optional dependencies), and KeyValueGridViewModel, where
    // it was a false positive: that one is hand-constructed with two perfectly reasonable overloads and
    // never goes near the container.
    //
    // Distinguishing the two needs the DI registrations, which live in the internal Composition class.
    // Rather than prop the test up with an exemption list on the day it was written - which is how an
    // exemption list stops being read - the real bug was fixed and the test dropped.

    private static IEnumerable<Type> ViewModelTypes() =>
        typeof(MainViewModel).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => t.Namespace == "Fubar.Studio.UI.ViewModels")
            .Where(t => t.Name.EndsWith("ViewModel", StringComparison.Ordinal));

    /// <summary>
    /// Every <c>.axaml</c> plus every view code-behind in the UI project, concatenated - the two places
    /// a command can legitimately be reached from.
    ///
    /// <para>Read from SOURCE rather than from embedded resources: the compiled XAML has already been
    /// transformed, and what this test is about is what someone wrote in the file. View models
    /// themselves are excluded, or a command that only calls itself would look wired.</para>
    /// </summary>
    private static string LoadAllMarkup()
    {
        var project = FindUiProjectDirectory();
        var views = Path.Combine(project, "Views");

        var files = Directory
            .EnumerateFiles(project, "*.axaml", SearchOption.AllDirectories)
            .Concat(Directory.Exists(views) ? Directory.EnumerateFiles(views, "*.cs", SearchOption.AllDirectories) : [])
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        return string.Concat(files.Select(File.ReadAllText));
    }

    private static string FindUiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Fubar.Studio.UI");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/Fubar.Studio.UI by walking up from the test output directory.");
    }

}
