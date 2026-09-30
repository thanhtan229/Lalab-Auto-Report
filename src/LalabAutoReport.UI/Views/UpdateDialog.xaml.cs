using System.Windows;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class UpdateDialog : Window
{
    public UpdateDialog(UpdateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
