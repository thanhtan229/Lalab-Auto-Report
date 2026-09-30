using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.UI.Views;

namespace LalabAutoReport.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IScanService _scanService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IFileSystemAdapter _fileSystem;
    private readonly ILockingService _lockingService;
    private readonly ICustomerBillingService? _customerBillingService;
    private readonly ICustomerBillRepository? _customerBillRepository;
    private readonly IJpegBillExporter? _jpegBillExporter;
    private readonly IExcelBillExporter? _excelBillExporter;
    private readonly ICustomerRepository? _customerRepository;

    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private string _formattedDateString = DateTime.Today.ToString("yyyy-MM-dd");

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanProgressText = string.Empty;

    [ObservableProperty]
    private int _scanProgressPercent;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _currentFilter = "All"; // "All", "NeedsReview", "Ready"

    [ObservableProperty]
    private ObservableCollection<OrderDisplayModel> _allOrders = new();

    [ObservableProperty]
    private ObservableCollection<OrderDisplayModel> _filteredOrders = new();

    [ObservableProperty]
    private int _totalOrders;

    [ObservableProperty]
    private int _totalCustomers;

    [ObservableProperty]
    private bool _hasOrders;

    [ObservableProperty]
    private int _needsReviewCount;

    [ObservableProperty]
    private int _readyCount;

    [ObservableProperty]
    private int _billedCount;

    private readonly IAutoScanCoordinator? _autoScanCoordinator;

    public int LockedCount => BilledCount;

    public DashboardViewModel(
        IScanService scanService,
        ISettingsRepository settingsRepository,
        IOrderRepository orderRepository,
        IFileSystemAdapter fileSystem,
        ILockingService lockingService,
        ICustomerBillingService? customerBillingService = null,
        ICustomerBillRepository? customerBillRepository = null,
        IJpegBillExporter? jpegBillExporter = null,
        ICustomerRepository? customerRepository = null,
        IExcelBillExporter? excelBillExporter = null,
        IAutoScanCoordinator? autoScanCoordinator = null)
    {
        _scanService = scanService;
        _settingsRepository = settingsRepository;
        _orderRepository = orderRepository;
        _fileSystem = fileSystem;
        _lockingService = lockingService;
        _customerBillingService = customerBillingService;
        _customerBillRepository = customerBillRepository;
        _jpegBillExporter = jpegBillExporter;
        _excelBillExporter = excelBillExporter;
        _customerRepository = customerRepository;
        _autoScanCoordinator = autoScanCoordinator;

        if (_autoScanCoordinator != null)
        {
            _autoScanCoordinator.StatusChanged += msg =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = msg;
                });
            };

            _autoScanCoordinator.OrdersUpdated += count =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _ = LoadOrdersForSelectedDateAsync();
                });
            };
        }

        SelectedDate = DateTime.Today;
        FormattedDateString = SelectedDate.ToString("yyyy-MM-dd");
    }

    [RelayCommand]
    private async Task AssignCustomerAsync(OrderDisplayModel? orderDisplay)
    {
        if (orderDisplay == null) return;
        if (_customerRepository == null) return;

        var vm = new AssignCustomerViewModel(
            orderDisplay.OriginalFolderName,
            FormattedDateString,
            _customerRepository,
            _orderRepository);

        var window = new Views.AssignCustomerWindow(vm)
        {
            Owner = Application.Current?.MainWindow
        };

        if (window.ShowDialog() == true)
        {
            string folderName = orderDisplay.OriginalFolderName;

            // Update all matching orders on Dashboard
            foreach (var order in AllOrders.Where(o => string.Equals(o.OriginalFolderName, folderName, StringComparison.OrdinalIgnoreCase)))
            {
                // If this order was previously attached to a bill, detach it from the old bill
                if (_customerBillingService != null && order.Order.Id > 0 && order.IsBilled)
                {
                    await _customerBillingService.DetachOrderFromCustomerBillAsync(order.Order.Id);
                }

                if (vm.IsGuestAssigned)
                {
                    order.Order.CustomerId = null;
                    order.Order.Customer = null;
                    order.Order.OrderName = "Khách lẻ";
                }
                else if (vm.AssignedCustomer != null)
                {
                    order.Order.CustomerId = vm.AssignedCustomer.Id;
                    order.Order.Customer = vm.AssignedCustomer;
                    if (order.Order.OrderName == "Khách lẻ")
                    {
                        order.Order.OrderName = null;
                    }
                }

                bool itemsHaveIssues = order.Order.Items.Count == 0 ||
                    order.Order.Items.Any(i => i.PrintFolderStatus != PrintFolderResolutionStatus.Resolved ||
                                               i.ScanStatus == ScanStatus.Failed ||
                                               i.PrintSpecificationId == null);
                if (!itemsHaveIssues)
                {
                    order.Order.Status = OrderStatus.Ready;
                    order.Status = OrderStatus.Ready;
                    if (_orderRepository != null && order.Order.Id > 0)
                    {
                        _ = _orderRepository.UpdateOrderStatusAsync(order.Order.Id, OrderStatus.Ready);
                    }
                }
                else
                {
                    order.Order.Status = OrderStatus.NeedsReview;
                    order.Status = OrderStatus.NeedsReview;
                    if (_orderRepository != null && order.Order.Id > 0)
                    {
                        _ = _orderRepository.UpdateOrderStatusAsync(order.Order.Id, OrderStatus.NeedsReview);
                    }
                }

                order.NotifyTotalsChanged();
            }

            ApplyFilter();
            UpdateSummary();

            if (vm.IsGuestAssigned)
            {
                StatusMessage = $"Đã đặt thư mục '{folderName}' là Khách lẻ.";
            }
            else if (vm.AssignedCustomer != null)
            {
                StatusMessage = $"Đã gán thành công '{folderName}' cho khách hàng '{vm.AssignedCustomer.CanonicalName}'.";
            }
        }
    }

    [RelayCommand]
    private void OpenQuickBill()
    {
        if (_customerBillingService == null || _customerBillRepository == null || _jpegBillExporter == null)
            return;

        var vm = new QuickBillSetupViewModel(
            _customerBillingService,
            _customerBillRepository,
            _jpegBillExporter,
            _settingsRepository,
            _excelBillExporter
        );

        var win = new Views.QuickBillSetupWindow(vm)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        win.ShowDialog();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        FormattedDateString = value.ToString("yyyy-MM-dd");
        _ = LoadOrdersForSelectedDateAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnCurrentFilterChanged(string value)
    {
        ApplyFilter();
    }

    public async Task LoadOrdersForSelectedDateAsync()
    {
        try
        {
            var dbOrders = await _orderRepository.GetOrdersByDateAsync(FormattedDateString);
            AllOrders.Clear();
            foreach (var order in dbOrders)
            {
                if (order.IsGuest && order.Status == OrderStatus.NeedsReview && order.Items.Count > 0)
                {
                    bool itemsHaveIssues = order.Items.Any(i => i.PrintFolderStatus != PrintFolderResolutionStatus.Resolved ||
                                                                i.ScanStatus == ScanStatus.Failed ||
                                                                i.PrintSpecificationId == null);
                    if (!itemsHaveIssues)
                    {
                        order.Status = OrderStatus.Ready;
                        _ = _orderRepository.UpdateOrderStatusAsync(order.Id, OrderStatus.Ready);
                    }
                }
                AllOrders.Add(new OrderDisplayModel(order));
            }
            ApplyFilter();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải dữ liệu: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PreviousDay()
    {
        SelectedDate = SelectedDate.AddDays(-1);
    }

    [RelayCommand]
    private void NextDay()
    {
        SelectedDate = SelectedDate.AddDays(1);
    }

    [RelayCommand]
    private void Today()
    {
        SelectedDate = DateTime.Today;
    }

    [RelayCommand]
    private async Task ScanDateAsync()
    {
        if (IsScanning) return;

        var settings = await _settingsRepository.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.RootFolder) || !_fileSystem.DirectoryExists(settings.RootFolder))
        {
            StatusMessage = "Vui lòng chọn Thư Mục Gốc (Root Folder) trong phần Cài Đặt trước khi quét!";
            return;
        }

        string dateFolder = _fileSystem.Combine(settings.RootFolder, FormattedDateString);
        if (!_fileSystem.DirectoryExists(dateFolder))
        {
            StatusMessage = $"Không tìm thấy thư mục ngày: '{FormattedDateString}' trong thư mục gốc.";
            return;
        }

        IsScanning = true;
        ScanProgressText = "Đang chuẩn bị quét...";
        ScanProgressPercent = 0;
        StatusMessage = string.Empty;

        _scanCts = new CancellationTokenSource();

        var progress = new Progress<ScanProgress>(p =>
        {
            ScanProgressText = p.CurrentStep;
            ScanProgressPercent = p.TotalItems > 0 ? (int)((double)p.CompletedItems / p.TotalItems * 100) : 0;
        });

        try
        {
            var scannedOrders = await _scanService.ScanDateAsync(FormattedDateString, progress, _scanCts.Token);
            await LoadOrdersForSelectedDateAsync();
            StatusMessage = $"Quét thành công ngày {FormattedDateString}: {scannedOrders.Count} đơn hàng.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã hủy thao tác quét.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi quét: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ScanProgressText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ScanMissingDaysAsync()
    {
        if (IsScanning) return;

        var settings = await _settingsRepository.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.RootFolder) || !_fileSystem.DirectoryExists(settings.RootFolder))
        {
            StatusMessage = "Vui lòng chọn Thư Mục Gốc trong Cài Đặt trước!";
            return;
        }

        IsScanning = true;
        ScanProgressText = $"Đang tìm các ngày chưa quét trong tháng {SelectedDate:MM/yyyy}...";
        ScanProgressPercent = 0;
        StatusMessage = string.Empty;

        _scanCts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            ScanProgressText = p.CurrentStep;
            ScanProgressPercent = p.TotalItems > 0 ? (int)((double)p.CompletedItems / p.TotalItems * 100) : 0;
        });

        try
        {
            var scanned = await _scanService.ScanMissingDaysAsync(SelectedDate.Year, SelectedDate.Month, progress, _scanCts.Token);
            await LoadOrdersForSelectedDateAsync();
            StatusMessage = scanned.Count > 0
                ? $"Đã quét bổ sung {scanned.Count} đơn hàng từ các ngày còn thiếu."
                : $"Tất cả các ngày trong tháng {SelectedDate:MM/yyyy} đã được quét đầy đủ!";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã hủy thao tác quét ngày thiếu.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi quét ngày thiếu: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ScanProgressText = string.Empty;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    private async Task RescanOrderAsync(OrderDisplayModel? orderDisplay)
    {
        if (orderDisplay == null || IsScanning) return;

        try
        {
            StatusMessage = $"Đang quét lại đơn hàng {orderDisplay.OriginalFolderName}...";
            var updated = await _scanService.ScanOrderAsync(orderDisplay.RelativePath);
            if (updated != null)
            {
                int index = AllOrders.IndexOf(orderDisplay);
                var newDisplay = new OrderDisplayModel(updated);
                if (index >= 0)
                {
                    AllOrders[index] = newDisplay;
                }
                else
                {
                    AllOrders.Add(newDisplay);
                }
                ApplyFilter();
                UpdateSummary();
                StatusMessage = $"Đã quét lại thành công {orderDisplay.OriginalFolderName}.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi quét lại đơn: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteOrderAsync(OrderDisplayModel? orderDisplay)
    {
        if (orderDisplay == null) return;

        if (!orderDisplay.CanDelete)
        {
            MessageBox.Show("Không thể xóa đơn hàng đã tính bill hoặc đã khóa hóa đơn.\n\nNếu muốn xóa, vui lòng mở khóa hóa đơn trước.", "Không Thể Xóa Đơn Hàng", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa đơn hàng sau khỏi danh sách?\n\n" +
            $"• Mã đơn: {orderDisplay.DisplayOrderCode}\n" +
            $"• Tên đơn: {orderDisplay.FullOrderDisplayName}\n" +
            $"• Ngày: {FormattedDateString}\n\n" +
            "LƯU Ý: Thao tác này chỉ xóa bản ghi dữ liệu trong phần mềm. Tệp ảnh và thư mục trên ổ đĩa của bạn hoàn toàn KHÔNG bị xóa.",
            "Xác nhận xóa đơn hàng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _orderRepository.DeleteOrderAsync(orderDisplay.Id);
            AllOrders.Remove(orderDisplay);
            ApplyFilter();
            UpdateSummary();
            StatusMessage = $"Đã xóa đơn hàng {orderDisplay.DisplayOrderCode} ({orderDisplay.FullOrderDisplayName}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xóa đơn hàng: {ex.Message}";
            MessageBox.Show($"Không thể xóa đơn hàng: {ex.Message}", "Lỗi Xóa Đơn Hàng", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ComputeOrViewBillAsync(OrderDisplayModel? orderDisplay)
    {
        if (orderDisplay == null || IsScanning) return;
        if (_customerBillingService == null || _customerBillRepository == null || _jpegBillExporter == null) return;

        if (orderDisplay.IsBilled)
        {
            await ViewBillAsync(orderDisplay);
            return;
        }

        await ComputeBillForOrderAsync(orderDisplay);
    }

    private async Task ViewBillAsync(OrderDisplayModel orderDisplay)
    {
        try
        {
            StatusMessage = $"Đang mở hóa đơn cho {orderDisplay.FullOrderDisplayName}...";

            CustomerBill? bill = await _customerBillRepository!.GetLockedBillByOrderIdAsync(orderDisplay.Id);

            if (bill == null && !string.IsNullOrWhiteSpace(orderDisplay.RelativePath))
            {
                var billsByPath = await _customerBillRepository.GetBillsBySourceFolderPathAsync(orderDisplay.RelativePath);
                bill = billsByPath.FirstOrDefault(b => b.Status != CustomerBillStatus.Draft) ?? billsByPath.FirstOrDefault();
            }

            if (bill == null && orderDisplay.Order.CustomerId.HasValue)
            {
                bill = await _customerBillRepository.GetLastLockedOrExportedBillByCustomerIdAsync(orderDisplay.Order.CustomerId.Value);
            }

            if (bill == null)
            {
                MessageBox.Show(
                    $"Không tìm thấy bản lưu hóa đơn đã khóa cho đơn hàng '{orderDisplay.FullOrderDisplayName}'.\nĐơn có thể đã được tính trong một phiên làm việc khác.",
                    "Không Tìm Thấy Hóa Đơn",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var vm = new CustomerBillReviewViewModel(
                bill,
                _customerBillingService!,
                _jpegBillExporter!,
                _excelBillExporter,
                settingsRepository: _settingsRepository
            );

            var win = new CustomerBillReviewWindow(vm)
            {
                Owner = Application.Current?.MainWindow
            };

            win.ShowDialog();

            // Refresh data in case bill was reopened
            await LoadOrdersForSelectedDateAsync();
            StatusMessage = $"Đã xem xong hóa đơn {bill.BillNumber}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi mở hóa đơn: {ex.Message}";
            MessageBox.Show($"Lỗi khi mở hóa đơn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ComputeBillForOrderAsync(OrderDisplayModel orderDisplay)
    {
        try
        {
            // 1. Kiểm tra gán khách hàng nếu chưa có và không phải khách lẻ
            if (!orderDisplay.HasCustomer && !orderDisplay.IsGuest)
            {
                if (_customerRepository == null) return;

                var assignVm = new AssignCustomerViewModel(
                    orderDisplay.OriginalFolderName,
                    FormattedDateString,
                    _customerRepository,
                    _orderRepository);

                var assignWin = new AssignCustomerWindow(assignVm)
                {
                    Owner = Application.Current?.MainWindow
                };

                if (assignWin.ShowDialog() != true)
                {
                    return; // Người dùng hủy gán khách
                }

                // Cập nhật lại các đơn hàng liên quan trên Dashboard
                string folderName = orderDisplay.OriginalFolderName;
                foreach (var order in AllOrders.Where(o => string.Equals(o.OriginalFolderName, folderName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (assignVm.IsGuestAssigned)
                    {
                        order.Order.CustomerId = null;
                        order.Order.Customer = null;
                        order.Order.OrderName = "Khách lẻ";
                    }
                    else if (assignVm.AssignedCustomer != null)
                    {
                        order.Order.CustomerId = assignVm.AssignedCustomer.Id;
                        order.Order.Customer = assignVm.AssignedCustomer;
                        if (order.Order.OrderName == "Khách lẻ")
                        {
                            order.Order.OrderName = null;
                        }
                    }

                    if (order.Order.Status == OrderStatus.NeedsReview)
                    {
                        bool itemsHaveIssues = order.Order.Items.Count == 0 ||
                            order.Order.Items.Any(i => i.PrintFolderStatus != PrintFolderResolutionStatus.Resolved ||
                                                       i.ScanStatus == ScanStatus.Failed ||
                                                       i.PrintSpecificationId == null);
                        if (!itemsHaveIssues)
                        {
                            order.Order.Status = OrderStatus.Ready;
                            order.Status = OrderStatus.Ready;
                            if (_orderRepository != null && order.Order.Id > 0)
                            {
                                _ = _orderRepository.UpdateOrderStatusAsync(order.Order.Id, OrderStatus.Ready);
                            }
                        }
                    }

                    order.NotifyTotalsChanged();
                }

                ApplyFilter();
                UpdateSummary();
            }

            // 2. Tính bill cho Khách hàng cụ thể
            if (orderDisplay.HasCustomer && orderDisplay.Order.CustomerId.HasValue)
            {
                long customerId = orderDisplay.Order.CustomerId.Value;
                StatusMessage = $"Đang chuẩn bị hóa đơn cho {orderDisplay.CanonicalCustomerName}...";

                var result = await _customerBillingService!.BuildOrRefreshDraftAsync(customerId, forceRescan: false);

                var vm = new CustomerBillReviewViewModel(
                    result.Draft,
                    _customerBillingService,
                    _jpegBillExporter!,
                    _excelBillExporter,
                    result.Warnings,
                    result.BlockingIssues,
                    _settingsRepository,
                    result.SuggestedCustomer
                );

                var win = new CustomerBillReviewWindow(vm)
                {
                    Owner = Application.Current?.MainWindow
                };

                win.ShowDialog();

                await LoadOrdersForSelectedDateAsync();
                return;
            }

            // 3. Tính bill cho Khách lẻ
            if (orderDisplay.IsGuest || !orderDisplay.HasCustomer)
            {
                var settings = await _settingsRepository.GetSettingsAsync();
                string fullFolderPath = _fileSystem.Combine(settings.RootFolder, orderDisplay.RelativePath);

                if (!_fileSystem.DirectoryExists(fullFolderPath))
                {
                    MessageBox.Show($"Thư mục không tồn tại trên ổ đĩa:\n\n{fullFolderPath}", "Lỗi Thư Mục", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string guestName;
                if (orderDisplay.Kind == OrderKind.Explicit && !string.IsNullOrWhiteSpace(orderDisplay.OrderName))
                {
                    guestName = orderDisplay.OrderName;
                }
                else if (!string.IsNullOrWhiteSpace(orderDisplay.OriginalFolderName) &&
                         !string.Equals(orderDisplay.OriginalFolderName, "KHACH_LE", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(orderDisplay.OriginalFolderName, "khách lẻ", StringComparison.OrdinalIgnoreCase))
                {
                    guestName = orderDisplay.OriginalFolderName;
                }
                else
                {
                    guestName = "Khách lẻ";
                }
                StatusMessage = $"Đang chuẩn bị hóa đơn khách lẻ cho {guestName}...";

                var result = await _customerBillingService!.BuildGuestBillDraftAsync(new[] { fullFolderPath }, guestName);

                var vm = new CustomerBillReviewViewModel(
                    result.Draft,
                    _customerBillingService,
                    _jpegBillExporter!,
                    _excelBillExporter,
                    result.Warnings,
                    result.BlockingIssues,
                    _settingsRepository,
                    result.SuggestedCustomer
                );

                var win = new CustomerBillReviewWindow(vm)
                {
                    Owner = Application.Current?.MainWindow
                };

                win.ShowDialog();

                await LoadOrdersForSelectedDateAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tính bill: {ex.Message}";
            MessageBox.Show($"Lỗi tính bill: {ex.Message}", "Lỗi Tính Bill", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task RescanSpecificationAsync(OrderItemDisplayModel? itemDisplay)
    {
        if (itemDisplay == null || IsScanning) return;

        try
        {
            StatusMessage = $"Đang quét lại quy cách {itemDisplay.SpecName}...";
            var updated = await _scanService.ScanSpecificationAsync(itemDisplay.SpecRelativePath);
            if (updated != null)
            {
                itemDisplay.PrintCount = updated.PrintCount;
                itemDisplay.MismatchCount = updated.MismatchCount;
                itemDisplay.PrintFolderStatus = updated.PrintFolderStatus;
                itemDisplay.SelectedPrintFolderRelativePath = updated.SelectedPrintFolderRelativePath;
                itemDisplay.BillQuantity = updated.BillQuantity;
                itemDisplay.ResolutionMode = updated.QuantityResolutionMode;
                itemDisplay.ParentOrder.NotifyTotalsChanged();
                UpdateSummary();
                StatusMessage = $"Đã quét lại quy cách {itemDisplay.SpecName}.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi quét lại quy cách: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ResolveQuantityAsync(object? param)
    {
        if (param is ValueTuple<OrderItemDisplayModel, QuantityResolutionMode, int, string?> tuple)
        {
            var (item, mode, quantity, note) = tuple;
            await _orderRepository.UpdateOrderItemResolutionAsync(item.Id, quantity, mode, note);
            item.UpdateResolution(quantity, mode, note);
            UpdateSummary();
        }
    }

    [RelayCommand]
    private async Task SelectCandidatePrintFolderAsync(object? param)
    {
        if (param is (OrderItemDisplayModel item, string candidateRelativePath))
        {
            var settings = await _settingsRepository.GetSettingsAsync();
            string fullPath = _fileSystem.Combine(settings.RootFolder, candidateRelativePath);
            var supported = new HashSet<string>(settings.SupportedExtensions, StringComparer.OrdinalIgnoreCase);

            int count = 0;
            if (_fileSystem.DirectoryExists(fullPath))
            {
                foreach (var file in _fileSystem.EnumerateFiles(fullPath))
                {
                    if (supported.Contains(_fileSystem.GetExtension(file)))
                        count++;
                }
            }

            await _orderRepository.UpdateOrderItemPrintFolderAsync(item.Id, candidateRelativePath, count);
            item.UpdatePrintFolder(candidateRelativePath, count);
            UpdateSummary();
        }
    }

    [RelayCommand]
    private async Task OpenDateFolderAsync()
    {
        var settings = await _settingsRepository.GetSettingsAsync();
        string dateFolder = _fileSystem.Combine(settings.RootFolder, FormattedDateString);
        if (_fileSystem.DirectoryExists(dateFolder))
        {
            _fileSystem.OpenDirectoryInShell(dateFolder);
        }
        else
        {
            StatusMessage = $"Thư mục ngày chưa tồn tại: '{dateFolder}'";
        }
    }

    [RelayCommand]
    private async Task OpenOrderFolderAsync(OrderDisplayModel? order)
    {
        if (order == null) return;
        var settings = await _settingsRepository.GetSettingsAsync();
        string fullPath = _fileSystem.Combine(settings.RootFolder, order.RelativePath);
        _fileSystem.OpenDirectoryInShell(fullPath);
    }

    [RelayCommand]
    private async Task OpenSourceFolderAsync(OrderItemDisplayModel? item)
    {
        if (item == null) return;
        var settings = await _settingsRepository.GetSettingsAsync();
        string fullPath = _fileSystem.Combine(settings.RootFolder, item.SpecRelativePath);
        _fileSystem.OpenDirectoryInShell(fullPath);
    }

    [RelayCommand]
    private async Task OpenPrintFolderAsync(OrderItemDisplayModel? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.SelectedPrintFolderRelativePath)) return;
        var settings = await _settingsRepository.GetSettingsAsync();
        string fullPath = _fileSystem.Combine(settings.RootFolder, item.SelectedPrintFolderRelativePath);
        _fileSystem.OpenDirectoryInShell(fullPath);
    }

    private void ApplyFilter()
    {
        var filtered = AllOrders.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string s = SearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(o =>
                o.OriginalFolderName.ToLowerInvariant().Contains(s) ||
                (!string.IsNullOrWhiteSpace(o.OrderCode) && o.OrderCode.ToLowerInvariant().Contains(s)) ||
                (!string.IsNullOrWhiteSpace(o.OrderName) && o.OrderName.ToLowerInvariant().Contains(s)) ||
                (o.CanonicalCustomerName != null && o.CanonicalCustomerName.ToLowerInvariant().Contains(s)) ||
                o.Items.Any(i => i.SpecName.ToLowerInvariant().Contains(s)));
        }

        if (CurrentFilter == "NeedsReview")
        {
            filtered = filtered.Where(o => o.HasIssues);
        }
        else if (CurrentFilter == "Ready")
        {
            filtered = filtered.Where(o => o.Status == OrderStatus.Ready);
        }
        else if (CurrentFilter == "Billed" || CurrentFilter == "Locked")
        {
            filtered = filtered.Where(o => o.Status == OrderStatus.Locked || o.Status == OrderStatus.Billed || o.IsBilled);
        }

        FilteredOrders.Clear();
        foreach (var order in filtered)
        {
            FilteredOrders.Add(order);
        }

        HasOrders = FilteredOrders.Count > 0;
    }

    private void UpdateSummary()
    {
        TotalOrders = AllOrders.Count;
        TotalCustomers = AllOrders.Select(o => o.CanonicalCustomerName ?? o.OriginalFolderName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        NeedsReviewCount = AllOrders.Count(o => o.HasIssues);
        ReadyCount = AllOrders.Count(o => o.Status == OrderStatus.Ready && !o.HasIssues);
        BilledCount = AllOrders.Count(o => o.Status == OrderStatus.Locked || o.Status == OrderStatus.Billed || o.IsBilled);
    }
}
