using System.Windows;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class CustomerBillReviewWindow : Window
{
    public CustomerBillReviewWindow(CustomerBillReviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is CustomerBillReviewViewModel vm)
        {
            await vm.SaveCurrentStateAsync();
        }
    }
}
