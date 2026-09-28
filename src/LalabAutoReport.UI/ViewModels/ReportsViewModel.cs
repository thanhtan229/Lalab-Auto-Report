using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.UI.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private readonly IReportService _reportService;
    private readonly IScanService _scanService;

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

    // Progress for scanning missing days
    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanProgressText = string.Empty;

    public ReportsViewModel(IReportService reportService, IScanService scanService)
    {
        _reportService = reportService;
        _scanService = scanService;
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
}
