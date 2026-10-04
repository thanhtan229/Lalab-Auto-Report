using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.UI.Views;

namespace LalabAutoReport.UI.ViewModels;

public partial class InvoicesViewModel : ObservableObject
{
    private readonly ICustomerBillRepository _customerBillRepository;
    private readonly ICustomerBillingService _customerBillingService;
    private readonly IJpegBillExporter _jpegBillExporter;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ICustomerRepository? _customerRepository;
    private readonly IExcelBillExporter? _excelBillExporter;
    private readonly IShippingLabelExporter? _shippingLabelExporter;

    [ObservableProperty]
    private ObservableCollection<CustomerBill> _allInvoices = new();

    [ObservableProperty]
    private ObservableCollection<CustomerBill> _filteredInvoices = new();

    // Filters
    [ObservableProperty]
    private string _dateScopeFilter = "AllTime"; // "AllTime", "ThisMonth", "Today", "Custom"

    [ObservableProperty]
    private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _paymentFilter = "All"; // "All", "Unpaid", "Paid"

    [ObservableProperty]
    private string _billTypeFilter = "All"; // "All", "Customer", "Guest"

    public record CustomerFilterOption(long? CustomerId, string DisplayName, bool IsGuest = false)
    {
        public override string ToString() => DisplayName;
    }

    [ObservableProperty]
    private ObservableCollection<CustomerFilterOption> _customerFilterOptions = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomerFilterActive))]
    private CustomerFilterOption? _selectedCustomerFilter;

    public bool IsCustomerFilterActive => SelectedCustomerFilter != null && (SelectedCustomerFilter.CustomerId != null || SelectedCustomerFilter.IsGuest);

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Financial KPI Metrics
    [ObservableProperty]
    private long _totalRevenue;

    [ObservableProperty]
    private long _totalDebtAmount;

    [ObservableProperty]
    private long _totalPaidAmount;

    [ObservableProperty]
    private int _unpaidBillsCount;

    [ObservableProperty]
    private int _paidBillsCount;

    public int TotalBillsCount => FilteredInvoices.Count;
    public string FormattedTotalRevenue => $"{TotalRevenue:N0} đ";
    public string FormattedTotalDebtAmount => $"{TotalDebtAmount:N0} đ";
    public string FormattedTotalPaidAmount => $"{TotalPaidAmount:N0} đ";
    public bool HasNoInvoicesFound => FilteredInvoices.Count == 0 && !IsLoading;

    public InvoicesViewModel(
        ICustomerBillRepository customerBillRepository,
        ICustomerBillingService customerBillingService,
        IJpegBillExporter jpegBillExporter,
        ISettingsRepository settingsRepository,
        ICustomerRepository? customerRepository = null,
        IExcelBillExporter? excelBillExporter = null,
        IShippingLabelExporter? shippingLabelExporter = null)
    {
        _customerBillRepository = customerBillRepository;
        _customerBillingService = customerBillingService;
        _jpegBillExporter = jpegBillExporter;
        _settingsRepository = settingsRepository;
        _customerRepository = customerRepository;
        _excelBillExporter = excelBillExporter;
        _shippingLabelExporter = shippingLabelExporter;
    }

    partial void OnDateScopeFilterChanged(string value)
    {
        _ = LoadInvoicesAsync();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (DateScopeFilter == "Custom") _ = LoadInvoicesAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (DateScopeFilter == "Custom") _ = LoadInvoicesAsync();
    }

    partial void OnPaymentFilterChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnBillTypeFilterChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedCustomerFilterChanged(CustomerFilterOption? value)
    {
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    public async Task LoadCustomerFilterOptionsAsync()
    {
        long? prevId = SelectedCustomerFilter?.CustomerId;
        bool prevGuest = SelectedCustomerFilter?.IsGuest ?? false;

        var options = new List<CustomerFilterOption>
        {
            new(null, "Tất cả khách hàng"),
            new(-1, "⚡ Tất cả khách lẻ", IsGuest: true)
        };

        if (_customerRepository != null)
        {
            try
            {
                var customers = await _customerRepository.GetAllAsync();
                foreach (var c in customers.OrderBy(c => c.CanonicalName))
                {
                    options.Add(new CustomerFilterOption(c.Id, $"👤 {c.CanonicalName}"));
                }
            }
            catch { }
        }

        CustomerFilterOptions.Clear();
        foreach (var opt in options)
        {
            CustomerFilterOptions.Add(opt);
        }

        if (prevGuest)
        {
            SelectedCustomerFilter = CustomerFilterOptions.FirstOrDefault(o => o.IsGuest);
        }
        else if (prevId.HasValue)
        {
            SelectedCustomerFilter = CustomerFilterOptions.FirstOrDefault(o => o.CustomerId == prevId.Value) 
                ?? CustomerFilterOptions.FirstOrDefault();
        }
        else
        {
            SelectedCustomerFilter ??= CustomerFilterOptions.FirstOrDefault();
        }
    }

    [RelayCommand]
    public async Task LoadInvoicesAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Đang tải danh sách hóa đơn...";

            if (CustomerFilterOptions.Count == 0)
            {
                await LoadCustomerFilterOptionsAsync();
            }

            IReadOnlyList<CustomerBill> bills;
            if (DateScopeFilter == "ThisMonth")
            {
                string yearMonth = $"{DateTime.Today.Year:D4}-{DateTime.Today.Month:D2}";
                bills = await _customerBillRepository.GetBillsByMonthAsync(yearMonth);
            }
            else if (DateScopeFilter == "Today")
            {
                string todayStr = $"{DateTime.Today:yyyy-MM-dd}";
                bills = await _customerBillRepository.GetBillsByDateAsync(todayStr);
            }
            else if (DateScopeFilter == "Custom")
            {
                string start = StartDate.ToString("yyyy-MM-dd");
                string end = EndDate.ToString("yyyy-MM-dd");
                bills = await _customerBillRepository.GetBillsByDateRangeAsync(start, end);
            }
            else
            {
                // AllTime
                bills = await _customerBillRepository.GetAllBillsAsync();
            }

            AllInvoices.Clear();
            foreach (var b in bills.OrderByDescending(b => b.CreatedAt))
            {
                AllInvoices.Add(b);
            }

            ApplyFilters();
            StatusMessage = $"Đã tải {AllInvoices.Count} hóa đơn.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải hóa đơn: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoInvoicesFound));
        }
    }

    public void ApplyFilters()
    {
        var query = AllInvoices.AsEnumerable();

        // 1. Customer filter
        if (SelectedCustomerFilter != null)
        {
            if (SelectedCustomerFilter.IsGuest || SelectedCustomerFilter.CustomerId == -1)
            {
                query = query.Where(b => b.BillType == BillType.Guest || b.CustomerId == null);
            }
            else if (SelectedCustomerFilter.CustomerId.HasValue)
            {
                query = query.Where(b => b.CustomerId == SelectedCustomerFilter.CustomerId.Value);
            }
        }

        // 2. Payment status filter
        if (PaymentFilter == "Unpaid")
        {
            query = query.Where(b => !b.IsPaid);
        }
        else if (PaymentFilter == "Paid")
        {
            query = query.Where(b => b.IsPaid);
        }

        // 3. Bill type filter
        if (BillTypeFilter == "Customer")
        {
            query = query.Where(b => b.BillType == BillType.Customer);
        }
        else if (BillTypeFilter == "Guest")
        {
            query = query.Where(b => b.BillType == BillType.Guest);
        }

        // 4. Search query
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string term = SearchText.Trim().ToLowerInvariant();
            query = query.Where(b =>
                (!string.IsNullOrEmpty(b.BillNumber) && b.BillNumber.ToLowerInvariant().Contains(term)) ||
                (!string.IsNullOrEmpty(b.CustomerNameSnapshot) && b.CustomerNameSnapshot.ToLowerInvariant().Contains(term)) ||
                (!string.IsNullOrEmpty(b.PhoneSnapshot) && b.PhoneSnapshot.Contains(term))
            );
        }

        FilteredInvoices.Clear();
        long totalRevenue = 0;
        long totalDebt = 0;
        long totalPaid = 0;
        int unpaidCount = 0;
        int paidCount = 0;

        foreach (var b in query)
        {
            FilteredInvoices.Add(b);
            totalRevenue += b.GrandTotal;
            if (b.IsPaid)
            {
                totalPaid += b.GrandTotal;
                paidCount++;
            }
            else
            {
                totalDebt += b.GrandTotal;
                unpaidCount++;
            }
        }

        TotalRevenue = totalRevenue;
        TotalDebtAmount = totalDebt;
        TotalPaidAmount = totalPaid;
        UnpaidBillsCount = unpaidCount;
        PaidBillsCount = paidCount;

        OnPropertyChanged(nameof(TotalBillsCount));
        OnPropertyChanged(nameof(FormattedTotalRevenue));
        OnPropertyChanged(nameof(FormattedTotalDebtAmount));
        OnPropertyChanged(nameof(FormattedTotalPaidAmount));
        OnPropertyChanged(nameof(HasNoInvoicesFound));
    }

    public void FilterByCustomer(long? customerId, bool isGuest = false)
    {
        if (isGuest || customerId == -1)
        {
            SelectedCustomerFilter = CustomerFilterOptions.FirstOrDefault(o => o.IsGuest);
        }
        else if (customerId.HasValue)
        {
            SelectedCustomerFilter = CustomerFilterOptions.FirstOrDefault(o => o.CustomerId == customerId.Value);
        }
        else
        {
            SelectedCustomerFilter = CustomerFilterOptions.FirstOrDefault(o => o.CustomerId == null);
        }
        ApplyFilters();
    }

    [RelayCommand]
    public void QuickFilterByCustomer(CustomerBill? bill)
    {
        if (bill == null) return;
        if (bill.BillType == BillType.Guest || !bill.CustomerId.HasValue)
        {
            FilterByCustomer(null, isGuest: true);
        }
        else
        {
            FilterByCustomer(bill.CustomerId.Value, isGuest: false);
        }
    }

    [RelayCommand]
    public void ClearCustomerFilter()
    {
        SelectedCustomerFilter = CustomerFilterOptions.FirstOrDefault(o => o.CustomerId == null);
    }

    [RelayCommand]
    public async Task TogglePaymentStatusAsync(CustomerBill? bill)
    {
        if (bill == null) return;

        try
        {
            bool newStatus = !bill.IsPaid;
            DateTimeOffset? paidAt = newStatus ? DateTimeOffset.Now : null;

            await _customerBillRepository.SetPaymentStatusAsync(bill.Id, newStatus, paidAt);

            bill.HasPendingCloudChanges = (await _customerBillRepository.GetByIdAsync(bill.Id))?.HasPendingCloudChanges == true;
            bill.IsPaid = newStatus;
            bill.PaidAt = paidAt;

            // Recalculate metrics
            ApplyFilters();

            StatusMessage = newStatus
                ? $"Đã ghi nhận thanh toán cho bill {bill.BillNumber}."
                : $"Đã chuyển bill {bill.BillNumber} sang Chưa thanh toán.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi cập nhật thanh toán: {ex.Message}";
            MessageBox.Show($"Lỗi cập nhật thanh toán: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task OpenBillAsync(CustomerBill? bill)
    {
        if (bill == null) return;

        try
        {
            var fullBill = await _customerBillRepository.GetByIdAsync(bill.Id);
            if (fullBill == null) return;

            var vm = new CustomerBillReviewViewModel(
                fullBill,
                _customerBillingService,
                _jpegBillExporter,
                _excelBillExporter,
                settingsRepository: _settingsRepository,
                shippingLabelExporter: _shippingLabelExporter
            );

            var win = new CustomerBillReviewWindow(vm)
            {
                Owner = Application.Current?.MainWindow
            };

            win.ShowDialog();

            // Refresh after edit / lock / reopen
            await LoadInvoicesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở bill: {ex.Message}";
            MessageBox.Show($"Lỗi mở bill: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ReExportJpegAsync(CustomerBill? bill)
    {
        if (bill == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = $"Đang xuất lại bill {bill.BillNumber} (JPEG + Excel)...";
            var fullBill = await _customerBillRepository.GetByIdAsync(bill.Id);
            if (fullBill == null) return;

            string path = await _jpegBillExporter.ExportBillToJpegAsync(fullBill);

            string? excelError = null;
            if (_excelBillExporter != null)
            {
                try
                {
                    await _excelBillExporter.ExportBillToExcelAsync(fullBill);
                }
                catch (Exception ex)
                {
                    excelError = ex.Message;
                }
            }

            StatusMessage = $"Đã xuất bill thành công: {path}";

            if (File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                catch { }
            }

            if (excelError != null)
            {
                MessageBox.Show($"Đã xuất file JPEG thành công:\n{path}\n\nTuy nhiên lỗi ghi file Excel (.xlsx):\n{excelError}", "Cảnh Báo Xuất Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show($"Đã xuất lại bill thành công (JPEG + Excel):\n\n• Ảnh: {path}\n• Excel: {Path.ChangeExtension(path, ".xlsx")}", "Xuất Bill Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await LoadInvoicesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xuất lại bill: {ex.Message}";
            MessageBox.Show($"Lỗi xuất lại bill: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task OpenFileAsync(CustomerBill? bill)
    {
        if (bill == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Đang tạo bản xem trước hóa đơn...";
            var fullBill = await _customerBillRepository.GetByIdAsync(bill.Id);
            if (fullBill == null) return;

            var renderer = App.Current != null
                ? (App.Current as App)?.Services?.GetService(typeof(LalabAutoReport.Infrastructure.Reporting.IBillVisualRenderer)) as LalabAutoReport.Infrastructure.Reporting.IBillVisualRenderer
                : null;

            if (renderer != null)
            {
                var bitmaps = await renderer.RenderBillBitmapsAsync(fullBill);
                if (bitmaps.Count > 0)
                {
                    Window? ownerWindow = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                          ?? Application.Current?.MainWindow;

                    var win = new Views.BillPreviewWindow(fullBill, bitmaps, renderer)
                    {
                        Owner = ownerWindow
                    };
                    win.Show();
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(bill.ExportFilePath) && File.Exists(bill.ExportFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = bill.ExportFilePath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("Tệp hóa đơn JPEG chưa được tạo hoặc không tồn tại trên ổ đĩa.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở tệp: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task OpenFolderAsync(CustomerBill? bill)
    {
        if (bill == null || string.IsNullOrWhiteSpace(bill.ExportFilePath))
        {
            await OpenBillsFolderAsync();
            return;
        }

        string? dir = Path.GetDirectoryName(bill.ExportFilePath);
        if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        else
        {
            await OpenBillsFolderAsync();
        }
    }

    [RelayCommand]
    public async Task PrintShippingLabelAsync(CustomerBill? bill)
    {
        if (bill == null) return;

        var exporter = _shippingLabelExporter ??
            (Application.Current as App)?.Services?.GetService(typeof(IShippingLabelExporter)) as IShippingLabelExporter;

        if (exporter == null)
        {
            MessageBox.Show("Dịch vụ in tem vận chuyển chưa khả dụng.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var fullBill = await _customerBillRepository.GetByIdAsync(bill.Id);
            if (fullBill == null) return;

            await exporter.PrintShippingLabelAsync(fullBill);
            StatusMessage = $"Đã gửi lệnh in tem cho bill {bill.BillNumber}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi in tem: {ex.Message}";
            MessageBox.Show($"Lỗi in tem vận chuyển: {ex.Message}", "Lỗi In Tem", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }




    [RelayCommand]
    public async Task OpenBillsFolderAsync()
    {
        try
        {
            var settings = await _settingsRepository.GetSettingsAsync();
            string billsDir = !string.IsNullOrWhiteSpace(settings.RootFolder)
                ? Path.Combine(settings.RootFolder, "Bills")
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bills");

            if (!Directory.Exists(billsDir))
            {
                Directory.CreateDirectory(billsDir);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = billsDir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở thư mục Bills: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
