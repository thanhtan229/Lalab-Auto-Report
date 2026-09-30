using System.Windows;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class QuickBillSetupWindow : Window
{
    public QuickBillSetupWindow(QuickBillSetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();
    }
}
