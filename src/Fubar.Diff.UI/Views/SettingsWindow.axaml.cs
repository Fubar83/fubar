using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Fubar.Controls;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// The detailed comparison-settings dialog. Every option is a direct two-way binding onto the
/// <c>ComparisonViewModel</c> passed in as its DataContext, and changing one re-runs the comparison
/// exactly the way the toolbar's controls already did (see <c>ComparisonViewModel.OptionChanged</c>) -
/// this window is just a roomier place to reach them from.
///
/// <para>The only logic here is which category is on screen, which is view state and nothing else -
/// putting a selected-index on the comparison view model would make a diff session carry a note about
/// which settings tab someone last looked at.</para>
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        // Sections are addressed by position so the list and the panels cannot drift apart silently:
        // adding a ListBoxItem without a panel throws here, at startup, rather than showing an empty
        // pane for one category that nobody notices until they click it.
        _sections =
        [
            SecGeneral, SecDifference, SecJson, SecDisplay, SecPretty, SecAdvanced,
        ];

        // Watched rather than bound to a TextChanged event: SearchBox exposes Text as a styled
        // property and no event, and the clear button writes it directly.
        SettingsSearch.PropertyChanged += (_, args) =>
        {
            if (args.Property == SearchBox.TextProperty)
            {
                Refresh();
            }
        };

        Refresh();
    }

    /// <summary>
    /// Nullable, and every use guarded, because the XAML sets SelectedIndex="0" - which raises
    /// SelectionChanged from INSIDE InitializeComponent, before the constructor has had a chance to
    /// fill this in. The first render then crashed the whole process on a null array.
    /// </summary>
    private readonly Control[]? _sections;

    private void Category_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => Refresh();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Shows one category, or - while a search is running - every category still holding a visible row.
    ///
    /// <para>The rows filter THEMSELVES: <c>SettingRow.Filter</c> is an inherited attached property set
    /// once in the markup, so this only has to decide which containers are worth showing, and a row
    /// added later is searchable without anyone remembering to register it here.</para>
    /// </summary>
    private void Refresh()
    {
        if (_sections is not { } sections)
        {
            return; // still initialising - see the field
        }

        var searching = !string.IsNullOrWhiteSpace(SettingsSearch.Text);
        var selected = Math.Clamp(CategoryList.SelectedIndex, 0, sections.Length - 1);

        for (var i = 0; i < sections.Length; i++)
        {
            sections[i].IsVisible = searching ? HasVisibleRow(sections[i]) : i == selected;
        }

        // The category list is not disabled while searching, it just stops deciding: results come from
        // everywhere, and clicking a category is how you leave the search.
        CategoryList.Opacity = searching ? 0.5 : 1.0;
        NoMatches.IsVisible = searching && sections.All(s => !s.IsVisible);
    }

    /// <summary>
    /// Whether anything inside this section survived the filter.
    ///
    /// <para>Only <c>SettingRow</c>s are counted. A section whose rows are all filtered out but which
    /// still holds a heading and an explanatory paragraph would otherwise look like a match while
    /// containing nothing you can change.</para>
    /// </summary>
    private static bool HasVisibleRow(Control section) =>
        section.GetLogicalDescendants().OfType<SettingRow>().Any(row => row.MatchesFilter);

}
