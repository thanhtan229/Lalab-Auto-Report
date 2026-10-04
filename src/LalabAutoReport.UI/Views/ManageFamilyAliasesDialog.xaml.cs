using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class ManageFamilyAliasesDialog : Window
{
    private readonly ManageFamilyAliasesViewModel _viewModel;

    public ManageFamilyAliasesDialog(ManageFamilyAliasesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += async (s, e) =>
        {
            await _viewModel.InitializeAsync();
        };
    }

    private async void AddFamily_Click(object sender, RoutedEventArgs e)
    {
        var promptResult = CreateFamilyDialog.Prompt(this);
        if (promptResult != null)
        {
            var (name, category, billingMethod, aliases) = promptResult.Value;
            await _viewModel.CreateFamilyAsync(name, category, billingMethod, aliases);
        }
    }

    private async void RenameFamily_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FamilyCardViewModel card)
        {
            string? input = InputDialog.Show(
                this,
                "Đổi Tên Dòng Sản Phẩm",
                $"Nhập tên mới thay thế cho '{card.Name}':",
                card.Name
            );

            if (!string.IsNullOrWhiteSpace(input) && !string.Equals(input.Trim(), card.Name, StringComparison.Ordinal))
            {
                await _viewModel.RenameFamilyAsync(card, input.Trim());
            }
        }
    }

    private async void DeleteFamily_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FamilyCardViewModel card)
        {
            var res = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa dòng sản phẩm '{card.Name}' cùng toàn bộ từ khóa của dòng này?\n\nLưu ý: Chỉ xóa được nếu dòng chưa có quy cách/sản phẩm nào.",
                "Xác nhận xóa dòng sản phẩm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                await _viewModel.DeleteFamilyAsync(card);
            }
        }
    }

    private async void EditAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProductFamilyAlias alias)
        {
            string? input = InputDialog.Show(
                this,
                "Sửa Từ Khóa Dòng",
                $"Nhập từ khóa mới thay thế cho '{alias.AliasText}':",
                alias.AliasText
            );

            if (!string.IsNullOrWhiteSpace(input) && !string.Equals(input.Trim(), alias.AliasText, StringComparison.Ordinal))
            {
                await _viewModel.UpdateAliasAsync(alias, input.Trim());
            }
        }
    }

    private async void AddAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FamilyCardViewModel card)
        {
            await _viewModel.AddAliasAsync(card);
        }
    }

    private async void NewAliasTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.DataContext is FamilyCardViewModel card)
        {
            e.Handled = true;
            await _viewModel.AddAliasAsync(card);
        }
    }

    private async void RemoveAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProductFamilyAlias alias)
        {
            var res = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa alias dùng chung '{alias.AliasText}' khỏi dòng sản phẩm?",
                "Xác nhận xóa alias dòng",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                await _viewModel.RemoveAliasAsync(alias);
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
