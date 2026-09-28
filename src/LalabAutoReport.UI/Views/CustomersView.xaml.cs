using System.Windows;
using System.Windows.Controls;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class CustomersView : UserControl
{
    public CustomersView()
    {
        InitializeComponent();
    }

    private void RemoveAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CustomerAlias alias && DataContext is CustomersViewModel vm)
        {
            vm.RemoveAliasCommand.Execute(alias);
        }
    }
}
