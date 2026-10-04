using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

public partial class ReportsViewModel : ObservableObject
{
    private readonly IReportService _reportService;
    private readonly IScanService _scanService;
    private readonly ICustomerBillRepository _customerBillRepository;
    private readonly ICustomerBillingService _customerBillingService;
    private readonly IJpegBillExporter _jpegBillExporter;
    private readonly IExcelBillExporter? _excelBillExporter;

    [ObservableProperty]
    private string _reportMode = "Monthly"; // "Monthly", "Daily", "DateRange"

    [ObservableProperty]
    private int _selectedYear = DateTime.Today.Year;

    [ObservableProperty]
    private int _selectedMonth = DateTime.Today.Month;

    [ObservableProperty]
    private DateTime _selectedDailyDate = DateTime.Today;

    [ObservableProperty]
    private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // KPI Metrics
    [ObservableProperty]
    private long _totalRevenue;

    [ObservableProperty]
    private int _totalBillQuantity;

    [ObservableProperty]
    private int _totalOrders;

    [ObservableProperty]
    private int _totalCustomers;

    [ObservableProperty]
    private int _unresolvedOrdersCount;

    [ObservableProperty]
    private int _lockedOrdersCount;

    public string FormattedTotalRevenue => $"{TotalRevenue:N0} đ";

    // Collections
    [ObservableProperty]
    private ObservableCollection<string> _missingScanDays = new();

    public bool HasMissingScanDays => MissingScanDays.Count > 0;

    [ObservableProperty]
    private ObservableCollection<CustomerMonthlySummary> _customerSummaries = new();

    [ObservableProperty]
    private ObservableCollection<SpecificationSummary> _specificationSummaries = new();

    [ObservableProperty]
    private ObservableCollection<DailySummary> _dailySummaries = new();

    // Bills History in Report
    [ObservableProperty]
    private ObservableCollection<CustomerBill> _allReportBills = new();

    [ObservableProperty]
    private ObservableCollection<CustomerBill> _filteredReportBills = new();

    [ObservableProperty]
    private bool _billScopeAllTime = false; // false = Theo kỳ báo cáo (mặc định), true = Toàn bộ lịch sử

    public bool BillScopeReportPeriod
    {
        get => !BillScopeAllTime;
        set
        {
            if (value) BillScopeAllTime = false;
        }
    }

    [ObservableProperty]
    private string _billTypeFilter = "All"; // "All", "Customer", "Guest"

    [ObservableProperty]
    private string _billSearchText = string.Empty;

    [ObservableProperty]
    private long _totalReportBillsAmount;

    public int TotalReportBillsCount => FilteredReportBills.Count;
    public string FormattedReportBillsAmount => $"{TotalReportBillsAmount:N0} đ";
    public bool HasNoBillsFound => FilteredReportBills.Count == 0;

    // Progress for scanning missing days
    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanProgressText = string.Empty;

    public ReportsViewModel(
        IReportService reportService,
        IScanService scanService,
        ICustomerBillRepository customerBillRepository,
        ICustomerBillingService customerBillingService,
        IJpegBillExporter jpegBillExporter,
        IExcelBillExporter? excelBillExporter = null)
    {
        _reportService = reportService;
        _scanService = scanService;
        _customerBillRepository = customerBillRepository;
        _customerBillingService = customerBillingService;
        _jpegBillExporter = jpegBillExporter;
        _excelBillExporter = excelBillExporter;
    }

    partial void OnReportModeChanged(string value)
    {
        _ = LoadReportAsync();
    }

    partial void OnSelectedYearChanged(int value)
    {
        if (ReportMode == "Monthly") _ = LoadReportAsync();
    }

    partial void OnSelectedMonthChanged(int value)
    {
        if (ReportMode == "Monthly") _ = LoadReportAsync();
    }

    partial void OnSelectedDailyDateChanged(DateTime value)
    {
        if (ReportMode == "Daily") _ = LoadReportAsync();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (ReportMode == "DateRange") _ = LoadReportAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (ReportMode == "DateRange") _ = LoadReportAsync();
    }

    partial void OnBillScopeAllTimeChanged(bool value)
    {
        OnPropertyChanged(nameof(BillScopeReportPeriod));
        _ = LoadReportBillsAsync();
    }

    partial void OnBillTypeFilterChanged(string value)
    {
        ApplyBillFilters();
    }

    partial void OnBillSearchTextChanged(string value)
    {
        ApplyBillFilters();
    }

    [RelayCommand]
    public async Task LoadReportAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = "Đang tải báo cáo từ cơ sở dữ liệu...";

        try
        {
            if (ReportMode == "Monthly")
            {
                var report = await _reportService.GetMonthlyReportAsync(SelectedYear, SelectedMonth);
                ApplyMonthlyReport(report);
            }
            else if (ReportMode == "Daily")
            {
                string dateStr = SelectedDailyDate.ToString("yyyy-MM-dd");
                var report = await _reportService.GetDailyReportAsync(dateStr);
                ApplyDailyReport(report);
            }
            else if (ReportMode == "DateRange")
            {
                string start = StartDate.ToString("yyyy-MM-dd");
                string end = EndDate.ToString("yyyy-MM-dd");
                var report = await _reportService.GetDateRangeReportAsync(start, end);
                ApplyDateRangeReport(report);
            }

            await LoadReportBillsAsync();

            StatusMessage = "Tải báo cáo hoàn tất.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi tạo báo cáo: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        if (SelectedMonth == 1)
        {
            SelectedMonth = 12;
            SelectedYear--;
        }
        else
        {
            SelectedMonth--;
        }
    }

    [RelayCommand]
    private void NextMonth()
    {
        if (SelectedMonth == 12)
        {
            SelectedMonth = 1;
            SelectedYear++;
        }
        else
        {
            SelectedMonth++;
        }
    }

    [RelayCommand]
    private void CurrentMonth()
    {
        SelectedYear = DateTime.Today.Year;
        SelectedMonth = DateTime.Today.Month;
    }

    [RelayCommand]
    private async Task ScanMissingDaysAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanProgressText = $"Đang quét các ngày còn thiếu trong tháng {SelectedMonth:D2}/{SelectedYear}...";
        StatusMessage = string.Empty;

        var progress = new Progress<ScanProgress>(p =>
        {
            ScanProgressText = p.CurrentStep;
        });

        try
        {
            var scanned = await _scanService.ScanMissingDaysAsync(SelectedYear, SelectedMonth, progress);
            StatusMessage = scanned.Count > 0
                ? $"Đã quét bổ sung thành công {scanned.Count} đơn hàng!"
                : "Không có ngày nào cần quét bổ sung.";

            await LoadReportAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi quét ngày thiếu: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ScanProgressText = string.Empty;
        }
    }

    private void ApplyMonthlyReport(MonthlyReport report)
    {
        TotalRevenue = report.TotalAmount;
        TotalBillQuantity = report.TotalBillQuantity;
        TotalOrders = report.TotalOrders;
        TotalCustomers = report.TotalCustomers;
        UnresolvedOrdersCount = report.UnresolvedOrdersCount;
        LockedOrdersCount = report.LockedOrdersCount;

        MissingScanDays.Clear();
        foreach (var day in report.MissingScanDays)
        {
            MissingScanDays.Add(day);
        }
        OnPropertyChanged(nameof(HasMissingScanDays));
        OnPropertyChanged(nameof(FormattedTotalRevenue));

        CustomerSummaries.Clear();
        foreach (var c in report.CustomerSummaries)
        {
            CustomerSummaries.Add(c);
        }

        SpecificationSummaries.Clear();
        foreach (var s in report.SpecificationSummaries)
        {
            SpecificationSummaries.Add(s);
        }

        DailySummaries.Clear();
        foreach (var d in report.DailySummaries)
        {
            DailySummaries.Add(d);
        }
    }

    private void ApplyDailyReport(DailyReport report)
    {
        TotalRevenue = report.TotalAmount;
        TotalBillQuantity = report.TotalBillQuantity;
        TotalOrders = report.TotalOrders;
        TotalCustomers = report.TotalCustomers;
        UnresolvedOrdersCount = report.UnresolvedOrdersCount;
        LockedOrdersCount = report.LockedOrdersCount;

        MissingScanDays.Clear();
        OnPropertyChanged(nameof(HasMissingScanDays));
        OnPropertyChanged(nameof(FormattedTotalRevenue));

        CustomerSummaries.Clear();
        foreach (var c in report.Customers)
        {
            CustomerSummaries.Add(new CustomerMonthlySummary(
                CustomerId: c.CustomerId,
                DisplayName: c.DisplayName,
                TotalOrders: c.Orders.Count,
                TotalBillQuantity: c.TotalBillQuantity,
                TotalAmount: c.TotalAmount,
                HasUnresolvedIssues: c.HasUnresolvedIssues
            ));
        }

        SpecificationSummaries.Clear();
        foreach (var s in report.Specifications)
        {
            SpecificationSummaries.Add(s);
        }

        DailySummaries.Clear();
        DailySummaries.Add(new DailySummary(
            Date: report.Date,
            TotalOrders: report.TotalOrders,
            TotalBillQuantity: report.TotalBillQuantity,
            TotalAmount: report.TotalAmount,
            HasUnresolvedIssues: report.UnresolvedOrdersCount > 0
        ));
    }

    private void ApplyDateRangeReport(DateRangeReport report)
    {
        TotalRevenue = report.TotalAmount;
        TotalBillQuantity = report.TotalBillQuantity;
        TotalOrders = report.TotalOrders;
        TotalCustomers = report.TotalCustomers;
        UnresolvedOrdersCount = report.UnresolvedOrdersCount;
        LockedOrdersCount = report.LockedOrdersCount;

        MissingScanDays.Clear();
        OnPropertyChanged(nameof(HasMissingScanDays));
        OnPropertyChanged(nameof(FormattedTotalRevenue));

        CustomerSummaries.Clear();
        foreach (var c in report.CustomerSummaries)
        {
            CustomerSummaries.Add(c);
        }

        SpecificationSummaries.Clear();
        foreach (var s in report.SpecificationSummaries)
        {
            SpecificationSummaries.Add(s);
        }

        DailySummaries.Clear();
        foreach (var d in report.DailySummaries)
        {
            DailySummaries.Add(d);
        }
    }

    [RelayCommand]
    public async Task LoadReportBillsAsync()
    {
        try
        {
            IReadOnlyList<CustomerBill> bills;
            if (BillScopeAllTime)
            {
                var all = await _customerBillRepository.GetAllBillsAsync(null);
                bills = all.Where(b => b.Status == CustomerBillStatus.Locked || b.Status == CustomerBillStatus.Exported).ToList();
            }
            else
            {
                if (ReportMode == "Monthly")
                {
                    string ym = $"{SelectedYear}-{SelectedMonth:D2}";
                    bills = await _customerBillRepository.GetBillsByMonthAsync(ym);
                }
                else if (ReportMode == "Daily")
                {
                    string d = SelectedDailyDate.ToString("yyyy-MM-dd");
                    bills = await _customerBillRepository.GetBillsByDateAsync(d);
                }
                else // DateRange
                {
                    string s = StartDate.ToString("yyyy-MM-dd");
                    string e = EndDate.ToString("yyyy-MM-dd");
                    bills = await _customerBillRepository.GetBillsByDateRangeAsync(s, e);
                }
            }

            AllReportBills.Clear();
            foreach (var b in bills.OrderByDescending(x => x.Id))
            {
                AllReportBills.Add(b);
            }

            ApplyBillFilters();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải danh sách hóa đơn: {ex.Message}";
        }
    }

    private void ApplyBillFilters()
    {
        var query = AllReportBills.AsEnumerable();

        if (BillTypeFilter == "Customer")
        {
            query = query.Where(b => b.BillType == BillType.Customer);
        }
        else if (BillTypeFilter == "Guest")
        {
            query = query.Where(b => b.BillType == BillType.Guest);
        }

        if (!string.IsNullOrWhiteSpace(BillSearchText))
        {
            string term = BillSearchText.Trim().ToLowerInvariant();
            query = query.Where(b =>
                (!string.IsNullOrEmpty(b.BillNumber) && b.BillNumber.ToLowerInvariant().Contains(term)) ||
                (!string.IsNullOrEmpty(b.CustomerNameSnapshot) && b.CustomerNameSnapshot.ToLowerInvariant().Contains(term)) ||
                (!string.IsNullOrEmpty(b.PhoneSnapshot) && b.PhoneSnapshot.Contains(term))
            );
        }

        FilteredReportBills.Clear();
        long totalAmt = 0;
        foreach (var b in query)
        {
            FilteredReportBills.Add(b);
            totalAmt += b.GrandTotal;
        }

        TotalReportBillsAmount = totalAmt;
        OnPropertyChanged(nameof(TotalReportBillsCount));
        OnPropertyChanged(nameof(FormattedReportBillsAmount));
        OnPropertyChanged(nameof(HasNoBillsFound));
    }

    [RelayCommand]
    private async Task OpenBillAsync(CustomerBill? bill)
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
                _excelBillExporter
            );

            var win = new CustomerBillReviewWindow(vm)
            {
                Owner = Application.Current?.MainWindow
            };

            win.ShowDialog();

            await LoadReportAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở bill: {ex.Message}";
            MessageBox.Show($"Lỗi mở bill: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ReExportJpegAsync(CustomerBill? bill)
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

            // Đồng bộ bill và ảnh JPEG vừa xuất lên Cloudflare
            _ = Task.Run(async () =>
            {
                try
                {
                    var syncService = (Application.Current as App)?.Services?.GetService(typeof(ICloudSyncService)) as ICloudSyncService;
                    if (syncService != null && fullBill.Id > 0)
                    {
                        await syncService.SyncBillAsync(fullBill.Id);
                    }
                }
                catch
                {
                    // Non-blocking fire-and-forget
                }
            });

            if (excelError != null)
            {
                MessageBox.Show($"Đã xuất file JPEG thành công:\n{path}\n\nTuy nhiên lỗi ghi file Excel (.xlsx):\n{excelError}", "Cảnh Báo Xuất Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show($"Đã xuất lại bill thành công (JPEG + Excel):\n\n• Ảnh: {path}\n• Excel: {Path.ChangeExtension(path, ".xlsx")}", "Xuất Bill Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await LoadReportBillsAsync();
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
    private void OpenFile(CustomerBill? bill)
    {
        if (bill == null || string.IsNullOrWhiteSpace(bill.ExportFilePath) || !File.Exists(bill.ExportFilePath))
        {
            MessageBox.Show("Tệp hóa đơn JPEG chưa được tạo hoặc không tồn tại.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = bill.ExportFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở tệp: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenFolder(CustomerBill? bill)
    {
        if (bill == null || string.IsNullOrWhiteSpace(bill.ExportFilePath)) return;
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
    }
}
