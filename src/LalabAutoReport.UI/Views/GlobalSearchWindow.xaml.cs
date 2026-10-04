using System;
using System.Windows;
using System.Windows.Input;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class GlobalSearchWindow : Window
{
    public GlobalSearchViewModel ViewModel { get; }

    public GlobalSearchWindow(GlobalSearchViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        ViewModel.RequestClose += () => Close();

        Loaded += (s, e) =>
        {
            SearchInputBox.Focus();
            SearchInputBox.SelectAll();
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ViewModel.SelectedResult != null)
            {
                _ = ViewModel.OpenSelectedResultAsync();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Down)
        {
            if (ResultsListBox.Items.Count > 0)
            {
                int nextIndex = Math.Min(ResultsListBox.Items.Count - 1, ResultsListBox.SelectedIndex + 1);
                ResultsListBox.SelectedIndex = nextIndex;
                ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up)
        {
            if (ResultsListBox.Items.Count > 0)
            {
                int prevIndex = Math.Max(0, ResultsListBox.SelectedIndex - 1);
                ResultsListBox.SelectedIndex = prevIndex;
                ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                e.Handled = true;
            }
        }
    }

    private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedResult != null)
        {
            _ = ViewModel.OpenSelectedResultAsync();
        }
    }
}
