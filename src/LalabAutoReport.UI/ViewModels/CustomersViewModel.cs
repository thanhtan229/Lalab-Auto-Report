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

public partial class CustomersViewModel : ObservableObject
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICustomerBillingService _customerBillingService;
    private readonly ICustomerBillRepository _customerBillRepository;
    private readonly IJpegBillExporter _jpegBillExporter;
    private readonly IExcelBillExporter? _excelBillExporter;
    private readonly ISettingsRepository _settingsRepository;

    [ObservableProperty]
    private ObservableCollection<Customer> _allCustomers = new();

    [ObservableProperty]
    private ObservableCollection<Customer> _filteredCustomers = new();

    [ObservableProperty]
    private Customer? _selectedCustomer;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _newCustomerName = string.Empty;

    [ObservableProperty]
    private string _newAliasText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _editCustomerName = string.Empty;

    [ObservableProperty]
    private string _editCustomerPhone = string.Empty;

    [ObservableProperty]
    private string _editCustomerNote = string.Empty;

    [ObservableProperty]
    private CustomerUnbilledSummary? _unbilledSummary;

    [ObservableProperty]
    private bool _hasUnbilledOrders;

    [ObservableProperty]
    private bool _isLoadingBilling;

    public int CustomerTotalBillsCount => CustomerBills.Count;
    public long CustomerTotalSpentAmount => CustomerBills.Sum(b => b.GrandTotal);
    public long CustomerTotalDebtAmount => CustomerBills.Where(b => !b.IsPaid).Sum(b => b.GrandTotal);
    public bool HasCustomerHistory => CustomerTotalBillsCount > 0;

    public event Action<long>? ViewCustomerInvoicesRequested;

    [RelayCommand]
    private void ViewCustomerInvoices()
    {
        if (SelectedCustomer != null && SelectedCustomer.Id > 0)
        {
            ViewCustomerInvoicesRequested?.Invoke(SelectedCustomer.Id);
        }
    }

    public ObservableCollection<CustomerBill> CustomerBills { get; } = new();

    public CustomersViewModel(
        ICustomerRepository customerRepository,
        ICustomerBillingService customerBillingService,
        ICustomerBillRepository customerBillRepository,
        IJpegBillExporter jpegBillExporter,
        ISettingsRepository settingsRepository,
        IExcelBillExporter? excelBillExporter = null)
    {
        _customerRepository = customerRepository;
        _customerBillingService = customerBillingService;
        _customerBillRepository = customerBillRepository;
        _jpegBillExporter = jpegBillExporter;
        _excelBillExporter = excelBillExporter;
        _settingsRepository = settingsRepository;
    }

    public async Task LoadCustomersAsync()
    {
        var customers = await _customerRepository.GetAllAsync();

        AllCustomers.Clear();
        foreach (var c in customers)
        {
            AllCustomers.Add(c);
        }

        ApplyFilter();

        if (SelectedCustomer != null)
        {
            SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == SelectedCustomer.Id) ?? AllCustomers.FirstOrDefault();
        }
        else
        {
            SelectedCustomer = AllCustomers.FirstOrDefault();
        }
    }

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        if (value != null)
        {
            EditCustomerName = value.CanonicalName;
            EditCustomerPhone = value.Phone ?? string.Empty;
            EditCustomerNote = value.Note ?? string.Empty;
        }
        else
        {
            EditCustomerName = string.Empty;
            EditCustomerPhone = string.Empty;
            EditCustomerNote = string.Empty;
        }

        _ = LoadCustomerBillingInfoAsync(value);
    }

    [RelayCommand]
    public async Task SaveCustomerInfoAsync()
    {
        if (SelectedCustomer == null)
        {
            StatusMessage = "Vui lòng chọn khách hàng!";
            return;
        }

        string trimmedName = EditCustomerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            StatusMessage = "Tên khách hàng không được để trống!";
            return;
        }

        string oldName = SelectedCustomer.CanonicalName;
        string? trimmedPhone = string.IsNullOrWhiteSpace(EditCustomerPhone) ? null : EditCustomerPhone.Trim();
        string? trimmedNote = string.IsNullOrWhiteSpace(EditCustomerNote) ? null : EditCustomerNote.Trim();

        try
        {
            SelectedCustomer.CanonicalName = trimmedName;
            SelectedCustomer.Phone = trimmedPhone;
            SelectedCustomer.Note = trimmedNote;

            await _customerRepository.UpdateCustomerAsync(SelectedCustomer);

            // Tự động lưu tên cũ làm alias nếu tên thay đổi và chưa có trong danh sách
            if (!string.IsNullOrWhiteSpace(oldName) && !string.Equals(oldName, trimmedName, StringComparison.OrdinalIgnoreCase))
            {
                if (!SelectedCustomer.Aliases.Any(a => string.Equals(a.AliasText, oldName, StringComparison.OrdinalIgnoreCase)))
                {
                    await _customerRepository.AddAliasAsync(SelectedCustomer.Id, oldName);
                }
            }

            StatusMessage = $"Đã cập nhật thông tin cho khách hàng '{trimmedName}'";
            long currentId = SelectedCustomer.Id;
            await LoadCustomersAsync();
            SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == currentId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi cập nhật khách hàng: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task<bool> DeleteCustomerAsync()
    {
        if (SelectedCustomer == null)
        {
            StatusMessage = "Vui lòng chọn khách hàng cần xóa!";
            return false;
        }

        string customerName = SelectedCustomer.CanonicalName;
        long customerId = SelectedCustomer.Id;

        try
        {
            bool hasHistory = await _customerRepository.HasCustomerHistoryAsync(customerId);
            if (hasHistory)
            {
                StatusMessage = $"Không thể xóa khách hàng '{customerName}' vì đã có lịch sử đơn hàng hoặc hóa đơn trong hệ thống.";
                return false;
            }

            await _customerRepository.DeleteCustomerAsync(customerId);
            StatusMessage = $"Đã xóa khách hàng '{customerName}' thành công.";
            SelectedCustomer = null;
            await LoadCustomersAsync();
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xóa khách hàng: {ex.Message}";
            return false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredCustomers.Clear();

        var filtered = AllCustomers.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string s = SearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(c =>
                c.CanonicalName.ToLowerInvariant().Contains(s) ||
                c.Aliases.Any(a => a.AliasText.ToLowerInvariant().Contains(s)));
        }

        foreach (var c in filtered.OrderBy(c => c.CanonicalName))
        {
            FilteredCustomers.Add(c);
        }
    }

    [RelayCommand]
    private async Task CreateCustomerAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCustomerName))
        {
            StatusMessage = "Vui lòng nhập tên khách hàng!";
            return;
        }

        var customer = new Customer
        {
            CanonicalName = NewCustomerName.Trim()
        };

        var created = await _customerRepository.CreateCustomerAsync(customer);
        NewCustomerName = string.Empty;
        StatusMessage = $"Đã thêm khách hàng: {created.CanonicalName}";
        await LoadCustomersAsync();
        SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == created.Id);
    }

    [RelayCommand]
    private async Task AddAliasAsync()
    {
        if (SelectedCustomer == null)
        {
            StatusMessage = "Vui lòng chọn khách hàng trước khi thêm alias!";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewAliasText))
        {
            StatusMessage = "Vui lòng nhập tên alias!";
            return;
        }

        string trimmed = NewAliasText.Trim();

        await _customerRepository.AddAliasAsync(SelectedCustomer.Id, trimmed);
        StatusMessage = $"Đã thêm alias '{trimmed}' cho {SelectedCustomer.CanonicalName}";
        NewAliasText = string.Empty;
        long currentId = SelectedCustomer.Id;
        await LoadCustomersAsync();
        SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == currentId);
    }

    [RelayCommand]
    public async Task UpdateAliasAsync(object? param)
    {
        if (param is (CustomerAlias alias, string newAliasText))
        {
            await UpdateCustomerAliasAsync(alias, newAliasText);
        }
    }

    public async Task<bool> UpdateCustomerAliasAsync(CustomerAlias alias, string newAliasText)
    {
        if (alias == null || SelectedCustomer == null)
        {
            StatusMessage = "Vui lòng chọn khách hàng và alias!";
            return false;
        }

        if (string.IsNullOrWhiteSpace(newAliasText))
        {
            StatusMessage = "Vui lòng nhập tên alias!";
            return false;
        }

        string trimmed = newAliasText.Trim();
        if (string.Equals(alias.AliasText, trimmed, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            await _customerRepository.UpdateAliasAsync(alias.Id, trimmed);
            StatusMessage = $"Đã cập nhật alias '{alias.AliasText}' thành '{trimmed}'";
            long currentId = SelectedCustomer.Id;
            await LoadCustomersAsync();
            SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == currentId);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi cập nhật alias: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    public async Task RemoveAliasAsync(CustomerAlias? alias)
    {
        if (alias == null || SelectedCustomer == null) return;

        try
        {
            await _customerRepository.RemoveAliasAsync(alias.Id);
            StatusMessage = $"Đã xóa alias '{alias.AliasText}'";
            long currentId = SelectedCustomer.Id;
            await LoadCustomersAsync();
            SelectedCustomer = AllCustomers.FirstOrDefault(c => c.Id == currentId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xóa alias: {ex.Message}";
        }
    }

    public async Task RefreshBillsAsync()
    {
        try
        {
            IsLoadingBilling = true;
            CustomerBills.Clear();

            IReadOnlyList<CustomerBill> bills;
            if (SelectedCustomer != null && SelectedCustomer.Id > 0)
            {
                bills = await _customerBillRepository.GetBillsByCustomerIdAsync(SelectedCustomer.Id);
            }
            else
            {
                bills = Array.Empty<CustomerBill>();
            }

            foreach (var b in bills)
            {
                CustomerBills.Add(b);
            }

            OnPropertyChanged(nameof(CustomerTotalBillsCount));
            OnPropertyChanged(nameof(CustomerTotalSpentAmount));
            OnPropertyChanged(nameof(CustomerTotalDebtAmount));
            OnPropertyChanged(nameof(HasCustomerHistory));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải lịch sử hóa đơn: {ex.Message}";
        }
        finally
        {
            IsLoadingBilling = false;
        }
    }

    public async Task LoadCustomerBillingInfoAsync(Customer? customer)
    {
        if (customer == null)
        {
            UnbilledSummary = null;
            HasUnbilledOrders = false;
            await RefreshBillsAsync();
            return;
        }

        try
        {
            IsLoadingBilling = true;

            var summary = await _customerBillingService.GetCustomerUnbilledSummaryAsync(customer.Id);
            UnbilledSummary = summary;
            HasUnbilledOrders = summary.UnbilledOrderCount > 0;

            await RefreshBillsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải thông tin hóa đơn: {ex.Message}";
        }
        finally
        {
            IsLoadingBilling = false;
        }
    }

    [RelayCommand]
    private async Task ComputeBillAsync()
    {
        if (SelectedCustomer == null)
        {
            StatusMessage = "Vui lòng chọn khách hàng!";
            return;
        }

        try
        {
            IsLoadingBilling = true;
            StatusMessage = $"Đang quét và tính bill cho {SelectedCustomer.CanonicalName}...";
            var result = await _customerBillingService.BuildOrRefreshDraftAsync(SelectedCustomer.Id, forceRescan: true);

            var vm = new CustomerBillReviewViewModel(
                result.Draft,
                _customerBillingService,
                _jpegBillExporter,
                _excelBillExporter,
                result.Warnings,
                result.BlockingIssues
            );

            var win = new CustomerBillReviewWindow(vm)
            {
                Owner = Application.Current?.MainWindow
            };

            win.ShowDialog();

            // Refresh billing info after dialog closes
            await LoadCustomerBillingInfoAsync(SelectedCustomer);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tính bill: {ex.Message}";
            MessageBox.Show($"Lỗi tính bill: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoadingBilling = false;
        }
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

            if (SelectedCustomer != null)
            {
                await LoadCustomerBillingInfoAsync(SelectedCustomer);
            }
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
            IsLoadingBilling = true;
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

            // Tự động mở DUY NHẤT file Jpeg vừa xuất (không mở file Excel)
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

            if (SelectedCustomer != null)
            {
                await LoadCustomerBillingInfoAsync(SelectedCustomer);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xuất lại bill: {ex.Message}";
            MessageBox.Show($"Lỗi xuất lại bill: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoadingBilling = false;
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
