using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IScanService _scanService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IFileSystemAdapter _fileSystem;
    private readonly ILockingService _lockingService;

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
    private int _lockedCount;

    public DashboardViewModel(
        IScanService scanService,
        ISettingsRepository settingsRepository,
        IOrderRepository orderRepository,
        IFileSystemAdapter fileSystem,
        ILockingService lockingService)
    {
        _scanService = scanService;
        _settingsRepository = settingsRepository;
        _orderRepository = orderRepository;
        _fileSystem = fileSystem;
        _lockingService = lockingService;

        SelectedDate = DateTime.Today;
        FormattedDateString = SelectedDate.ToString("yyyy-MM-dd");
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
            AllOrders.Clear();
            foreach (var order in scannedOrders)
            {
                AllOrders.Add(new OrderDisplayModel(order));
            }
            ApplyFilter();
            UpdateSummary();
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
    private async Task VerifyAndLockOrderAsync(OrderDisplayModel? orderDisplay)
    {
        if (orderDisplay == null || IsScanning) return;

        try
        {
            StatusMessage = $"Đang xác minh & khóa đơn {orderDisplay.OriginalFolderName}...";
            var lockedBill = await _lockingService.VerifyAndLockOrderAsync(orderDisplay.Id);

            var updated = await _orderRepository.GetOrderByIdAsync(orderDisplay.Id);
            if (updated != null)
            {
                int index = AllOrders.IndexOf(orderDisplay);
                var newDisplay = new OrderDisplayModel(updated);
                if (index >= 0)
                {
                    AllOrders[index] = newDisplay;
                }
                ApplyFilter();
                UpdateSummary();
            }

            StatusMessage = $"Đã khóa đơn '{orderDisplay.OriginalFolderName}' thành công! Tổng tiền: {lockedBill.Subtotal:N0} đ.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không thể khóa đơn: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReopenOrderAsync(OrderDisplayModel? orderDisplay)
    {
        if (orderDisplay == null || IsScanning) return;

        try
        {
            StatusMessage = $"Đang mở khóa đơn {orderDisplay.OriginalFolderName}...";
            await _lockingService.ReopenOrderAsync(orderDisplay.Id, "Mở lại từ Dashboard");

            var updated = await _orderRepository.GetOrderByIdAsync(orderDisplay.Id);
            if (updated != null)
            {
                int index = AllOrders.IndexOf(orderDisplay);
                var newDisplay = new OrderDisplayModel(updated);
                if (index >= 0)
                {
                    AllOrders[index] = newDisplay;
                }
                ApplyFilter();
                UpdateSummary();
            }

            StatusMessage = $"Đã mở lại đơn '{orderDisplay.OriginalFolderName}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở khóa đơn: {ex.Message}";
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
        else if (CurrentFilter == "Locked")
        {
            filtered = filtered.Where(o => o.Status == OrderStatus.Locked);
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
        TotalCustomers = AllOrders.Select(o => o.OriginalFolderName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        NeedsReviewCount = AllOrders.Count(o => o.HasIssues);
        ReadyCount = AllOrders.Count(o => o.Status == OrderStatus.Ready);
        LockedCount = AllOrders.Count(o => o.Status == OrderStatus.Locked);
    }
}
