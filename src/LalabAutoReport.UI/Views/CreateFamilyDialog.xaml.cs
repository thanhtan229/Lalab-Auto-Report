using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.UI.Views;

public partial class CreateFamilyDialog : Window
{
    public string FamilyName { get; private set; } = string.Empty;
    public ProductCategory Category { get; private set; } = ProductCategory.PhotoPrint;
    public BillingMethod BillingMethod { get; private set; } = BillingMethod.FileCount;
    public List<string> InitialAliases { get; private set; } = new();

    public CreateFamilyDialog()
    {
        InitializeComponent();
        txtFamilyName.Focus();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        string name = txtFamilyName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Vui lòng nhập tên dòng sản phẩm!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            txtFamilyName.Focus();
            return;
        }

        FamilyName = name;

        if (rbAlbum.IsChecked == true)
        {
            Category = ProductCategory.Album;
            BillingMethod = BillingMethod.AlbumBasePlusExtra;
        }
        else
        {
            Category = ProductCategory.PhotoPrint;
            BillingMethod = BillingMethod.FileCount;
        }

        string rawAliases = txtInitialAliases.Text ?? string.Empty;
        InitialAliases = rawAliases
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static (string name, ProductCategory category, BillingMethod billingMethod, List<string> aliases)? Prompt(Window? owner)
    {
        var dialog = new CreateFamilyDialog();
        if (owner != null) dialog.Owner = owner;

        if (dialog.ShowDialog() == true)
        {
            return (dialog.FamilyName, dialog.Category, dialog.BillingMethod, dialog.InitialAliases);
        }

        return null;
    }
}
