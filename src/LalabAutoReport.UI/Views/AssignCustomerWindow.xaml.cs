using System.Windows;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class AssignCustomerWindow : Window
{
    private readonly AssignCustomerViewModel _viewModel;

    public AssignCustomerWindow(AssignCustomerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.RequestClose += () =>
        {
            DialogResult = _viewModel.DialogResult;
            Close();
        };

        Loaded += async (s, e) =>
        {
            await _viewModel.InitializeAsync();
        };
    }
}
