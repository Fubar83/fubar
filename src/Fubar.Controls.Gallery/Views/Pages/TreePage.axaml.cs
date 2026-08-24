using System.Collections.Generic;
using Avalonia.Controls;
using Fubar.Controls.Gallery.Views;

namespace Fubar.Controls.Gallery.Views.Pages;

public partial class TreePage : UserControl
{
    public TreePage()
    {
        InitializeComponent();

        EnvList.ItemsSource = new[] { "Production", "Staging", "Local" };
        AuthList.ItemsSource = new[] { "Bearer (main)", "Basic (admin)" };

        RequestTree.ItemsSource = new List<DemoTreeNode>
        {
            new DemoTreeNode("Users").With(
                new DemoTreeNode("List users", "GET"),
                new DemoTreeNode("Create user", "POST", isDirty: true),
                new DemoTreeNode("Admin").With(
                    new DemoTreeNode("Delete user", "DELETE"),
                    new DemoTreeNode("Reset password", "PUT"))),
            new DemoTreeNode("Billing").With(
                new DemoTreeNode("Get invoice", "GET"),
                new DemoTreeNode("Refund", "POST")),
            new DemoTreeNode("Health check", "GET"),
        };
    }
}
