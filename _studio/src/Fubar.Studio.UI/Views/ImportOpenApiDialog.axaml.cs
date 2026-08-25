using Avalonia.Controls;
using Fubar.Studio.UI.Services;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Views;

public partial class ImportOpenApiDialog : Window
{
    public ImportOpenApiDialog()
    {
        InitializeComponent();
    }

    public ImportOpenApiDialog(ImportOpenApiViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += result => Close(result);
    }
}
