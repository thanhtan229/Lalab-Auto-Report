using System;
using System.Windows;
using System.Windows.Controls;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.UI.Views;

public partial class ResetConfirmationDialog : Window
{
    private const string ConfirmationKeyword = "reset";

    public ResetDataScope SelectedScope =>
        rbFactoryReset.IsChecked == true ? ResetDataScope.FactoryReset : ResetDataScope.OperationalOnly;

    public ResetConfirmationDialog()
    {
        InitializeComponent();
        txtConfirmation.Focus();
    }

    private void TxtConfirmation_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool matches = string.Equals(txtConfirmation.Text.Trim(), ConfirmationKeyword, StringComparison.OrdinalIgnoreCase);
        btnConfirm.IsEnabled = matches;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public static (bool Confirmed, ResetDataScope Scope) Show(Window? owner)
    {
        var dialog = new ResetConfirmationDialog();
        if (owner != null)
        {
            dialog.Owner = owner;
        }

        bool? result = dialog.ShowDialog();
        return (result == true, dialog.SelectedScope);
    }
}
