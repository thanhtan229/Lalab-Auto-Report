using System.Windows;
using System.Windows.Controls;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.UI.ViewModels;

namespace LalabAutoReport.UI.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    private void FilterAll_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.CurrentFilter = "All";
    }

    private void FilterNeedsReview_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.CurrentFilter = "NeedsReview";
    }

    private void FilterReady_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.CurrentFilter = "Ready";
    }

    private void FilterLocked_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.CurrentFilter = "Locked";
    }

    private void LockOrder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderDisplayModel order && ViewModel != null)
        {
            var res = MessageBox.Show(
                $"Xác nhận quét kiểm tra và khóa đơn hàng '{order.OriginalFolderName}'?\n\nĐơn hàng sau khi khóa sẽ được lưu thành hóa đơn lịch sử.",
                "Khóa đơn hàng",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                ViewModel.VerifyAndLockOrderCommand.Execute(order);
            }
        }
    }

    private void ReopenOrder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderDisplayModel order && ViewModel != null)
        {
            var res = MessageBox.Show(
                $"Bạn có chắc chắn muốn mở khóa đơn hàng '{order.OriginalFolderName}'?\n\nThao tác này sẽ mở khóa đơn hàng để chỉnh sửa lại.",
                "Mở lại đơn hàng",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                ViewModel.ReopenOrderCommand.Execute(order);
            }
        }
    }

    private void OpenOrderFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderDisplayModel order && ViewModel != null)
        {
            ViewModel.OpenOrderFolderCommand.Execute(order);
        }
    }

    private void RescanOrder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderDisplayModel order && ViewModel != null)
        {
            ViewModel.RescanOrderCommand.Execute(order);
        }
    }

    private void OpenSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderItemDisplayModel item && ViewModel != null)
        {
            ViewModel.OpenSourceFolderCommand.Execute(item);
        }
    }

    private void OpenPrintFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderItemDisplayModel item && ViewModel != null)
        {
            ViewModel.OpenPrintFolderCommand.Execute(item);
        }
    }

    private void RescanSpec_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderItemDisplayModel item && ViewModel != null)
        {
            ViewModel.RescanSpecificationCommand.Execute(item);
        }
    }

    private void SelectCandidateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string candidatePath && ViewModel != null)
        {
            // Find parent OrderItemDisplayModel
            var parent = FindParent<Border>(btn);
            if (parent?.DataContext is OrderItemDisplayModel item)
            {
                ViewModel.SelectCandidatePrintFolderCommand.Execute((item, candidatePath));
            }
        }
    }

    private void UsePrintQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderItemDisplayModel item && ViewModel != null)
        {
            if (item.PrintCount.HasValue)
            {
                ViewModel.ResolveQuantityCommand.Execute((item, QuantityResolutionMode.UsePrint, item.PrintCount.Value, (string?)"Dùng số lượng in"));
            }
        }
    }

    private void UseSourceQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderItemDisplayModel item && ViewModel != null)
        {
            ViewModel.ResolveQuantityCommand.Execute((item, QuantityResolutionMode.UseSource, item.SourceCount, (string?)"Dùng số lượng gốc"));
        }
    }

    private void CustomQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderItemDisplayModel item && ViewModel != null)
        {
            var window = Window.GetWindow(this);
            string? input = InputDialog.Show(
                window,
                "Tùy chỉnh số lượng tính tiền",
                $"Nhập số lượng tính tiền tùy chỉnh cho quy cách '{item.SpecName}':",
                (item.PrintCount ?? item.SourceCount).ToString()
            );

            if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out int customQty) && customQty >= 0)
            {
                ViewModel.ResolveQuantityCommand.Execute((item, QuantityResolutionMode.Custom, customQty, (string?)"Người dùng nhập thủ công"));
            }
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (parent != null && parent is not T)
        {
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return parent as T;
    }
}
