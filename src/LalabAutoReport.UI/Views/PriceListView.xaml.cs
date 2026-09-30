using System.Windows;
using System.Windows.Controls;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class PriceListView : UserControl
{
    public PriceListView()
    {
        InitializeComponent();
    }

    private async void EditAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PrintSpecificationAlias alias && DataContext is PriceListViewModel vm)
        {
            var window = Window.GetWindow(this);
            string? input = InputDialog.Show(
                window,
                "Sửa Alias Quy Cách",
                $"Nhập tên alias mới thay thế cho '{alias.AliasText}':",
                alias.AliasText
            );

            if (!string.IsNullOrWhiteSpace(input) && !string.Equals(input.Trim(), alias.AliasText, System.StringComparison.Ordinal))
            {
                await vm.UpdateSpecAliasAsync(alias, input.Trim());
            }
        }
    }

    private void RemoveAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PrintSpecificationAlias alias && DataContext is PriceListViewModel vm)
        {
            var res = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa alias '{alias.AliasText}' khỏi quy cách này?",
                "Xác nhận xóa alias",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                vm.RemoveAliasCommand.Execute(alias);
            }
        }
    }

    private void RemoveFamilyAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProductFamilyAlias alias && DataContext is PriceListViewModel vm)
        {
            var res = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa alias dùng chung '{alias.AliasText}' khỏi dòng sản phẩm?",
                "Xác nhận xóa family alias",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                vm.RemoveFamilyAliasCommand.Execute(alias);
            }
        }
    }

    private async void RenameSpec_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PriceListViewModel vm && vm.SelectedSpec != null)
        {
            var window = Window.GetWindow(this);
            string currentName = vm.SelectedSpec.CanonicalName;
            string? input = InputDialog.Show(
                window,
                "Đổi Tên Quy Cách / Sản Phẩm",
                $"Nhập tên quy cách chuẩn mới (ví dụ: 13x18 in hoặc Album 25x25):",
                currentName
            );

            if (!string.IsNullOrWhiteSpace(input) && !string.Equals(input.Trim(), currentName, System.StringComparison.Ordinal))
            {
                await vm.RenameSpecificationAsync(vm.SelectedSpec, input.Trim());
            }
        }
    }

    private async void DeleteSpec_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PriceListViewModel vm && vm.SelectedSpec != null)
        {
            var specName = vm.SelectedSpec.CanonicalName;
            var res = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa quy cách '{specName}' không?\n\nLưu ý: Nếu quy cách này đã được dùng trong các đơn hàng cũ, hệ thống sẽ ngừng kích hoạt quy cách để bảo toàn dữ liệu lịch sử.",
                "Xác nhận xóa quy cách",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                await vm.DeleteSelectedSpecCommand.ExecuteAsync(null);
            }
        }
    }
}
