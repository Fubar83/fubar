using Avalonia.Controls;

namespace Fubar.Controls.Gallery.Views.Pages;

public partial class JsonEditorPage : UserControl
{
    public JsonEditorPage()
    {
        InitializeComponent();
        Editor.Text = "{\n  \"name\": \"Fubar\",\n  \"version\": 1,\n  \"nested\": {\n    \"enabled\": true,\n    \"items\": [1, 2, 3]\n  }\n}";
    }
}
