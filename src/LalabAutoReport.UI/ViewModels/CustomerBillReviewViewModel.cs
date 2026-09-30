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
using LalabAutoReport.Infrastructure.Reporting;
using LalabAutoReport.UI.Views;

namespace LalabAutoReport.UI.ViewModels;

public partial class CustomerBillReviewViewModel : ObservableObject
{
    private readonly ICustomerBillingService _customerBillingService;
    private readonly IJpegBillExporter _jpegBillExporter;
    private readonly IExcelBillExporter? _excelBillExporter;
    private readonly ISettingsRepository? _settingsRepository;

    [ObservableProperty]
    private CustomerBill _bill;

    [ObservableProperty]
    private string _windowTitle = "Duyệt & Xuất Hóa Đơn Khách Hàng";

    [ObservableProperty]
    private long _productSubtotal;

    [ObservableProperty]
    private long _adjustmentsTotal;

    [ObservableProperty]
    private long _grandTotal;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private bool _hasBlockingIssues;

    [ObservableProperty]
    private bool _isExportSuccess;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanViewExportedBill))]
    [NotifyPropertyChangedFor(nameof(ViewBillToolTip))]
    [NotifyPropertyChangedFor(nameof(FolderToolTip))]
    [NotifyPropertyChangedFor(nameof(ExportButtonText))]
    private string _exportedFilePath = string.Empty;

    public bool CanViewExportedBill => !string.IsNullOrWhiteSpace(ExportedFilePath) && File.Exists(ExportedFilePath);

    public string ViewBillToolTip => CanViewExportedBill
        ? "Mở xem tệp ảnh JPEG của hóa đơn này"
        : "Hóa đơn chưa được xuất hoặc chưa có tệp ảnh JPEG";

    public string FolderToolTip => CanViewExportedBill
        ? "Mở thư mục chứa hóa đơn"
        : "Chưa có thư mục tệp đã xuất";

    public string ExportButtonText => CanViewExportedBill || (Bill != null && Bill.Status == CustomerBillStatus.Exported)
        ? "XUẤT LẠI BILL"
        : "XUẤT BILL";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(HasSuggestedCustomer))]
    private bool _isLocked;

    public bool IsEditable => !IsLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestedCustomer))]
    private Customer? _suggestedCustomer;

    public bool HasSuggestedCustomer => SuggestedCustomer != null && IsGuestBill && IsEditable;

    [ObservableProperty]
    private bool _isGuestBill;

    [ObservableProperty]
    private string _guestCustomerName = string.Empty;

    [ObservableProperty]
    private string _sourceFoldersSummary = string.Empty;

    [ObservableProperty]
    private string _newCustomLabel = string.Empty;

    [ObservableProperty]
    private long _newCustomAmount;

    [ObservableProperty]
    private AdjustmentDirection _newCustomDirection = AdjustmentDirection.Add;

    public ObservableCollection<CustomerBillOrderDisplayModel> Orders { get; } = new();
    public ObservableCollection<CustomerBillAdjustmentDisplayModel> Adjustments { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<string> BlockingIssues { get; } = new();

    public event Action? RequestClose;

    public CustomerBillReviewViewModel(
        CustomerBill bill,
        ICustomerBillingService customerBillingService,
        IJpegBillExporter jpegBillExporter,
        IExcelBillExporter? excelBillExporter,
        IReadOnlyList<string>? initialWarnings = null,
        IReadOnlyList<string>? initialBlockingIssues = null,
        ISettingsRepository? settingsRepository = null,
        Customer? suggestedCustomer = null)
    {
        _bill = bill;
        _customerBillingService = customerBillingService;
        _jpegBillExporter = jpegBillExporter;
        _excelBillExporter = excelBillExporter;
        _settingsRepository = settingsRepository;
        _suggestedCustomer = suggestedCustomer;

        LoadFromBill(bill, initialWarnings, initialBlockingIssues, suggestedCustomer);
    }

    public CustomerBillReviewViewModel(
        CustomerBill bill,
        ICustomerBillingService customerBillingService,
        IJpegBillExporter jpegBillExporter,
        IReadOnlyList<string>? initialWarnings = null,
        IReadOnlyList<string>? initialBlockingIssues = null)
        : this(bill, customerBillingService, jpegBillExporter, null, initialWarnings, initialBlockingIssues, null, null)
    {
    }

    public void LoadFromBill(CustomerBill bill, IReadOnlyList<string>? warnings = null, IReadOnlyList<string>? blockingIssues = null, Customer? suggestedCustomer = null)
    {
        Bill = bill;
        IsLocked = bill.Status == CustomerBillStatus.Locked || bill.Status == CustomerBillStatus.Exported;
        IsGuestBill = bill.BillType == BillType.Guest;
        GuestCustomerName = bill.CustomerNameSnapshot;
        SuggestedCustomer = suggestedCustomer;
        Note = bill.Note ?? string.Empty;
        WindowTitle = $"Duyệt & Xuất Hóa Đơn — {bill.BillNumber} ({bill.CustomerNameSnapshot})";

        if (bill.SourceFolders != null && bill.SourceFolders.Count > 0)
        {
            SourceFoldersSummary = string.Join("; ", bill.SourceFolders.Select(sf => sf.FolderPath));
        }
        else
        {
            SourceFoldersSummary = string.Empty;
        }

        Orders.Clear();
        foreach (var order in bill.Orders)
        {
            Orders.Add(new CustomerBillOrderDisplayModel(order, OnDataChanged));
        }

        Adjustments.Clear();
        foreach (var adj in bill.Adjustments)
        {
            Adjustments.Add(new CustomerBillAdjustmentDisplayModel(adj, OnDataChanged));
        }

        Warnings.Clear();
        if (warnings != null)
        {
            foreach (var w in warnings) Warnings.Add(w);
        }
        HasWarnings = Warnings.Count > 0;

        BlockingIssues.Clear();
        if (blockingIssues != null)
        {
            foreach (var b in blockingIssues) BlockingIssues.Add(b);
        }
        HasBlockingIssues = BlockingIssues.Count > 0;

        if (!string.IsNullOrEmpty(bill.ExportFilePath) && File.Exists(bill.ExportFilePath))
        {
            ExportedFilePath = bill.ExportFilePath;
            IsExportSuccess = true;
        }
        else
        {
            TryResolveExportFileFallback(bill);
        }

        OnPropertyChanged(nameof(CanViewExportedBill));
        OnPropertyChanged(nameof(ViewBillToolTip));
        OnPropertyChanged(nameof(FolderToolTip));
        OnPropertyChanged(nameof(ExportButtonText));

        RecalculateTotals();
    }

    private void TryResolveExportFileFallback(CustomerBill bill)
    {
        try
        {
            var candidateDirs = new List<string>();

            if (!string.IsNullOrWhiteSpace(bill.ExportFilePath))
            {
                string? dir = Path.GetDirectoryName(bill.ExportFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    candidateDirs.Add(dir);
                }
            }

            string defaultDocsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LalabReports", "Bills");
            if (Directory.Exists(defaultDocsDir)) candidateDirs.Add(defaultDocsDir);

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localTestBills = Path.Combine(appDir, "TEST_DATA_LALAB", "Bills");
            if (Directory.Exists(localTestBills)) candidateDirs.Add(localTestBills);

            string localBills = Path.Combine(appDir, "Bills");
            if (Directory.Exists(localBills)) candidateDirs.Add(localBills);

            string currentDir = Directory.GetCurrentDirectory();
            string curTestBills = Path.Combine(currentDir, "TEST_DATA_LALAB", "Bills");
            if (Directory.Exists(curTestBills)) candidateDirs.Add(curTestBills);

            string curBills = Path.Combine(currentDir, "Bills");
            if (Directory.Exists(curBills)) candidateDirs.Add(curBills);

            if (_settingsRepository != null)
            {
                try
                {
                    var settings = Task.Run(() => _settingsRepository.GetSettingsAsync()).GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(settings.BillExportFolder) && Directory.Exists(settings.BillExportFolder))
                    {
                        candidateDirs.Add(settings.BillExportFolder);
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            string deterministicName = WpfJpegBillExporter.GenerateDeterministicFileName(bill);

            foreach (var dir in candidateDirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(dir)) continue;

                string exactPath = Path.Combine(dir, deterministicName);
                if (File.Exists(exactPath))
                {
                    ExportedFilePath = exactPath;
                    IsExportSuccess = true;
                    bill.ExportFilePath = exactPath;
                    _ = _customerBillingService.RecordBillExportedAsync(bill.Id, exactPath);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(bill.BillNumber))
                {
                    var matches = Directory.GetFiles(dir, $"{bill.BillNumber}*.jpg");
                    if (matches.Length > 0 && File.Exists(matches[0]))
                    {
                        ExportedFilePath = matches[0];
                        IsExportSuccess = true;
                        bill.ExportFilePath = matches[0];
                        _ = _customerBillingService.RecordBillExportedAsync(bill.Id, matches[0]);
                        return;
                    }
                }
            }
        }
        catch
        {
            // Fallback search is best effort
        }
    }

    private void OnDataChanged()
    {
        RecalculateTotals();
    }

    partial void OnNoteChanged(string value)
    {
        Bill.Note = value;
    }

    public void RecalculateTotals()
    {
        // Subtotal of included orders and lines
        var includedOrderIds = Orders.Where(o => o.IsIncluded).Select(o => o.DomainOrder.OrderId).ToHashSet();
        ProductSubtotal = Orders
            .Where(o => o.IsIncluded)
            .SelectMany(o => o.Lines)
            .Where(l => l.IsIncluded)
            .Sum(l => l.LineTotal);

        long addTotal = Adjustments.Where(a => a.Direction == AdjustmentDirection.Add).Sum(a => a.Amount);
        long deductTotal = Adjustments.Where(a => a.Direction == AdjustmentDirection.Deduct).Sum(a => a.Amount);

        AdjustmentsTotal = addTotal - deductTotal;
        GrandTotal = Math.Max(0, ProductSubtotal + AdjustmentsTotal);

        Bill.ProductSubtotal = ProductSubtotal;
        Bill.AdjustmentsTotal = AdjustmentsTotal;
        Bill.GrandTotal = GrandTotal;

        // Recalculate dynamic blocking issues from currently included items
        BlockingIssues.Clear();
        var activeIssues = Orders
            .Where(o => o.IsIncluded)
            .SelectMany(o => o.Lines)
            .Where(l => l.IsIncluded && l.HasIssue)
            .Select(l =>
            {
                string folder = !string.IsNullOrWhiteSpace(l.SpecificationFolderName) ? l.SpecificationFolderName : l.ProductName;
                return $"[{folder}] {l.ProductName}: {l.IssueMessage}";
            })
            .Distinct()
            .ToList();

        foreach (var issue in activeIssues)
        {
            BlockingIssues.Add(issue);
        }
        HasBlockingIssues = BlockingIssues.Count > 0;
    }

    [RelayCommand]
    private void AddCustomAdjustment()
    {
        if (string.IsNullOrWhiteSpace(NewCustomLabel))
        {
            StatusMessage = "Vui lòng nhập tên khoản điều chỉnh!";
            return;
        }

        if (NewCustomAmount <= 0)
        {
            StatusMessage = "Số tiền phải lớn hơn 0!";
            return;
        }

        var domainAdj = new BillAdjustment
        {
            BillId = Bill.Id,
            Type = AdjustmentType.Custom,
            Label = NewCustomLabel.Trim(),
            Direction = NewCustomDirection,
            Amount = NewCustomAmount,
            SortOrder = Adjustments.Count + 1
        };

        Bill.Adjustments.Add(domainAdj);
        Adjustments.Add(new CustomerBillAdjustmentDisplayModel(domainAdj, OnDataChanged));

        NewCustomLabel = string.Empty;
        NewCustomAmount = 0;
        StatusMessage = "Đã thêm khoản điều chỉnh!";
        RecalculateTotals();
    }

    [RelayCommand]
    private void RemoveAdjustment(CustomerBillAdjustmentDisplayModel? adj)
    {
        if (adj == null) return;
        Adjustments.Remove(adj);
        Bill.Adjustments.Remove(adj.DomainAdjustment);
        RecalculateTotals();
    }

    [RelayCommand]
    private async Task RescanDraftAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Đang quét lại dữ liệu trên ổ đĩa...";
            CustomerBillDraftResult result;
            if (Bill.BillType == BillType.Guest || !Bill.CustomerId.HasValue)
            {
                var folderPaths = Bill.SourceFolders.Select(sf => sf.FolderPath).ToList();
                result = await _customerBillingService.BuildGuestBillDraftAsync(folderPaths, Bill.CustomerNameSnapshot, Bill.Id);
            }
            else
            {
                result = await _customerBillingService.BuildOrRefreshDraftAsync(Bill.CustomerId.Value, forceRescan: true);
            }
            LoadFromBill(result.Draft, result.Warnings, result.BlockingIssues);
            StatusMessage = "Đã quét lại và cập nhật hóa đơn!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi quét lại: {ex.Message}";
            MessageBox.Show($"Lỗi quét lại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void EditGuestName()
    {
        var inputDlg = new InputDialog("Đổi Tên Khách Lẻ", "Nhập tên mới hiển thị trên hóa đơn và file ảnh JPEG:", Bill.CustomerNameSnapshot)
        {
            Owner = Application.Current?.MainWindow
        };

        if (inputDlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDlg.InputText))
        {
            string newName = inputDlg.InputText.Trim();
            Bill.CustomerNameSnapshot = newName;
            GuestCustomerName = newName;
            WindowTitle = $"Duyệt & Xuất Hóa Đơn — {Bill.BillNumber} ({newName})";
            OnDataChanged();
        }
    }

    [RelayCommand]
    private async Task ConvertGuestToCustomerAsync()
    {
        if (!IsGuestBill) return;

        string promptDefault = !string.IsNullOrWhiteSpace(GuestCustomerName) ? GuestCustomerName : Bill.CustomerNameSnapshot;
        var inputDlg = new InputDialog("Chuyển thành Khách Hàng", "Nhập tên khách hàng để lưu vào danh mục của xưởng:", promptDefault)
        {
            Owner = Application.Current?.MainWindow
        };

        if (inputDlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDlg.InputText))
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Đang chuyển thành khách hàng chính thức...";
                var customer = await _customerBillingService.ConvertGuestBillToCustomerAsync(Bill.Id, inputDlg.InputText.Trim());
                Bill.CustomerId = customer.Id;
                Bill.BillType = BillType.Customer;
                Bill.CustomerNameSnapshot = customer.CanonicalName;
                IsGuestBill = false;
                GuestCustomerName = customer.CanonicalName;
                WindowTitle = $"Duyệt & Xuất Hóa Đơn — {Bill.BillNumber} ({customer.CanonicalName})";
                StatusMessage = $"Đã chuyển thành khách hàng '{customer.CanonicalName}' thành công!";
                MessageBox.Show($"Đã chuyển hóa đơn sang khách hàng '{customer.CanonicalName}' thành công.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Lỗi chuyển khách hàng: {ex.Message}";
                MessageBox.Show($"Lỗi chuyển khách hàng: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task ConvertGuestToSuggestedCustomerAsync()
    {
        if (SuggestedCustomer == null) return;
        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn gán hóa đơn này vào khách quen '{SuggestedCustomer.CanonicalName}' không?",
            "Xác nhận gán khách hàng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Đang chuyển đổi hóa đơn sang khách quen '{SuggestedCustomer.CanonicalName}'...";
            await _customerBillingService.ConvertGuestBillToCustomerAsync(Bill.Id, SuggestedCustomer.CanonicalName);
            Bill.BillType = BillType.Customer;
            Bill.CustomerId = SuggestedCustomer.Id;
            Bill.CustomerNameSnapshot = SuggestedCustomer.CanonicalName;
            Bill.PhoneSnapshot = SuggestedCustomer.Phone;
            IsGuestBill = false;
            GuestCustomerName = SuggestedCustomer.CanonicalName;
            var assignedCustName = SuggestedCustomer.CanonicalName;
            SuggestedCustomer = null;
            OnPropertyChanged(nameof(HasSuggestedCustomer));
            WindowTitle = $"Duyệt & Xuất Hóa Đơn — {Bill.BillNumber} ({Bill.CustomerNameSnapshot})";
            StatusMessage = $"Đã gán hóa đơn sang khách quen '{assignedCustName}' thành công!";
            MessageBox.Show($"Đã chuyển đổi sang khách quen '{assignedCustName}' thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi chuyển đổi: {ex.Message}";
            MessageBox.Show($"Lỗi chuyển đổi sang khách quen: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportBillAsync()
    {
        RecalculateTotals();

        // Check blocking issues
        var issues = Orders
            .Where(o => o.IsIncluded)
            .SelectMany(o => o.Lines)
            .Where(l => l.IsIncluded && l.HasIssue)
            .Select(l => l.IssueMessage)
            .Distinct()
            .ToList();

        if (issues.Count > 0)
        {
            MessageBox.Show($"Không thể xuất bill khi còn mục chưa xử lý:\n\n• {string.Join("\n• ", issues)}\n\nVui lòng xử lý hoặc bỏ chọn các mục bị lỗi trước khi xuất.", "Không Thể Xuất Bill", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int includedLineCount = Orders.Where(o => o.IsIncluded).SelectMany(o => o.Lines).Count(l => l.IsIncluded);
        if (includedLineCount == 0)
        {
            MessageBox.Show("Vui lòng chọn ít nhất một sản phẩm trong đơn để xuất bill.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Đang khóa hóa đơn và tạo snapshot...";

            // 1. Lock bill if still draft
            if (Bill.Status == CustomerBillStatus.Draft)
            {
                Bill = await _customerBillingService.LockBillAsync(Bill);
                IsLocked = true;
            }

            // 2. Export to JPEG & Excel
            StatusMessage = "Đang xuất hóa đơn (JPEG + Excel)...";
            string jpegPath = await _jpegBillExporter.ExportBillToJpegAsync(Bill);

            string? excelError = null;
            if (_excelBillExporter != null)
            {
                try
                {
                    await _excelBillExporter.ExportBillToExcelAsync(Bill);
                }
                catch (Exception ex)
                {
                    excelError = ex.Message;
                }
            }

            // Persist export record to database
            try
            {
                await _customerBillingService.RecordBillExportedAsync(Bill.Id, jpegPath);
            }
            catch
            {
                // Non-fatal if recording in DB encounters issue
            }

            ExportedFilePath = jpegPath;
            IsExportSuccess = true;
            OnPropertyChanged(nameof(CanViewExportedBill));
            OnPropertyChanged(nameof(ViewBillToolTip));
            OnPropertyChanged(nameof(FolderToolTip));
            OnPropertyChanged(nameof(ExportButtonText));

            if (excelError != null)
            {
                StatusMessage = $"Đã xuất file JPEG nhưng lỗi xuất Excel: {excelError}";
                MessageBox.Show($"Đã xuất file ảnh JPEG thành công:\n{jpegPath}\n\nTuy nhiên không thể ghi file Excel (.xlsx):\n{excelError}\n\n(Nếu tệp Excel đang được mở bởi ứng dụng khác, vui lòng đóng lại và thử lại)", "Cảnh Báo Xuất Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                StatusMessage = "Xuất bill thành công (JPEG + Excel)!";
            }

            // Tự động mở DUY NHẤT file Jpeg vừa xuất (không mở file Excel)
            OpenExportedFile();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xuất hóa đơn: {ex.Message}";
            MessageBox.Show($"Lỗi xuất hóa đơn: {ex.Message}", "Lỗi Xuất Bill", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenExportedFile()
    {
        if (string.IsNullOrWhiteSpace(ExportedFilePath) || !File.Exists(ExportedFilePath))
        {
            MessageBox.Show("Tệp hình ảnh không tồn tại.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ExportedFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở tệp: {ex.Message}", "Lỗi mở file", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void OpenExportedFolder()
    {
        if (string.IsNullOrWhiteSpace(ExportedFilePath)) return;
        try
        {
            if (File.Exists(ExportedFilePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{ExportedFilePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                string? dir = Path.GetDirectoryName(ExportedFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
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
                    MessageBox.Show("Thư mục lưu hóa đơn không tồn tại.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở thư mục: {ex.Message}", "Lỗi mở thư mục", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task ReopenBillAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Đang mở khóa hóa đơn...";
            await _customerBillingService.ReopenBillAsync(Bill.Id, "Người dùng mở khóa từ giao diện duyệt");
            Bill.Status = CustomerBillStatus.Draft;
            IsLocked = false;
            IsExportSuccess = false;
            ExportedFilePath = string.Empty;
            OnPropertyChanged(nameof(CanViewExportedBill));
            OnPropertyChanged(nameof(ViewBillToolTip));
            OnPropertyChanged(nameof(FolderToolTip));
            OnPropertyChanged(nameof(ExportButtonText));
            StatusMessage = "Hóa đơn đã được mở khóa về bản nháp (Draft)!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở khóa: {ex.Message}";
            MessageBox.Show($"Lỗi mở khóa: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseWindow()
    {
        RequestClose?.Invoke();
    }
}
