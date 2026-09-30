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

    private async void EditAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CustomerAlias alias && DataContext is CustomersViewModel vm)
        {
            var window = Window.GetWindow(this);
            string? input = InputDialog.Show(
                window,
                "Sửa Alias Khách Hàng",
                $"Nhập tên alias mới thay thế cho '{alias.AliasText}':",
                alias.AliasText
            );

            if (!string.IsNullOrWhiteSpace(input) && !string.Equals(input.Trim(), alias.AliasText, System.StringComparison.Ordinal))
            {
                await vm.UpdateCustomerAliasAsync(alias, input.Trim());
            }
        }
    }

    private void RemoveAlias_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CustomerAlias alias && DataContext is CustomersViewModel vm)
        {
            var res = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa alias '{alias.AliasText}' khỏi khách hàng này?",
                "Xác nhận xóa alias",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                vm.RemoveAliasCommand.Execute(alias);
            }
        }
    }

    private async void DeleteCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CustomersViewModel vm && vm.SelectedCustomer != null)
        {
            var custName = vm.SelectedCustomer.CanonicalName;
            var res = MessageBox.Show(
                $"Bạn có chắc chắn muốn xóa khách hàng '{custName}' và toàn bộ các alias liên quan không?\n\nLưu ý: Nếu khách hàng đã có đơn hàng hoặc hóa đơn trong lịch sử, hệ thống sẽ từ chối xóa để bảo vệ số liệu kế toán.",
                "Xác nhận xóa khách hàng",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                await vm.DeleteCustomerCommand.ExecuteAsync(null);
            }
        }
    }
}
