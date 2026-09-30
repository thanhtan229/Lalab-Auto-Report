using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.UI.Views;
using Microsoft.Win32;

namespace LalabAutoReport.UI.ViewModels;

public partial class QuickBillSetupViewModel : ObservableObject
{
    private readonly ICustomerBillingService _customerBillingService;
    private readonly ICustomerBillRepository _customerBillRepository;
    private readonly IJpegBillExporter _jpegBillExporter;
    private readonly IExcelBillExporter? _excelBillExporter;
    private readonly ISettingsRepository _settingsRepository;

    [ObservableProperty]
    private string _guestName = string.Empty;

    [ObservableProperty]
    private bool _hasFolders;

    [ObservableProperty]
    private bool _hasDuplicateWarnings;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> SourceFolders { get; } = new();
    public ObservableCollection<DuplicateFolderWarning> DuplicateWarnings { get; } = new();

    public event Action? RequestClose;

    public QuickBillSetupViewModel(
        ICustomerBillingService customerBillingService,
        ICustomerBillRepository customerBillRepository,
        IJpegBillExporter jpegBillExporter,
        ISettingsRepository settingsRepository,
        IExcelBillExporter? excelBillExporter = null)
    {
        _customerBillingService = customerBillingService;
        _customerBillRepository = customerBillRepository;
        _jpegBillExporter = jpegBillExporter;
        _excelBillExporter = excelBillExporter;
        _settingsRepository = settingsRepository;

        SourceFolders.CollectionChanged += (s, e) => HasFolders = SourceFolders.Count > 0;
    }

    [RelayCommand]
    private async Task AddFoldersAsync()
    {
        try
        {
            var settings = await _settingsRepository.GetSettingsAsync();
            string initialDir = Directory.Exists(settings.RootFolder) ? settings.RootFolder : string.Empty;

            var dialog = new OpenFolderDialog
            {
                Title = "Chọn thư mục nguồn tính bill khách lẻ (có thể chọn nhiều thư mục)",
                Multiselect = true,
                InitialDirectory = initialDir
            };

            if (dialog.ShowDialog() == true)
            {
                var folderNames = dialog.FolderNames.Length > 0 ? dialog.FolderNames : new[] { dialog.FolderName };
                string normRoot = !string.IsNullOrWhiteSpace(settings.RootFolder) ? Path.GetFullPath(settings.RootFolder.TrimEnd('\\', '/')) : string.Empty;

                foreach (var folder in folderNames)
                {
                    if (string.IsNullOrWhiteSpace(folder)) continue;

                    string normFolder = Path.GetFullPath(folder.TrimEnd('\\', '/'));

                    // Block root folder
                    if (!string.IsNullOrEmpty(normRoot) && string.Equals(normFolder, normRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"Thư mục '{folder}' là THƯ MỤC GỐC của xưởng in.\n\nVui lòng chọn thư mục đơn hàng của khách cụ thể.", "Thư mục không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }

                    // Block date folder under root
                    if (!string.IsNullOrEmpty(normRoot) && normFolder.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        string rel = Path.GetRelativePath(normRoot, normFolder).Replace('/', '\\').Trim('\\');
                        string[] parts = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 1 && System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^\d{4}[-_.]\d{2}[-_.]\d{2}$"))
                        {
                            MessageBox.Show($"Thư mục '{parts[0]}' là THƯ MỤC NGÀY của xưởng in.\n\nVui lòng mở thư mục ngày và chọn thư mục đơn hàng của khách bên trong.", "Thư mục không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                            continue;
                        }
                    }

                    if (!SourceFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    {
                        SourceFolders.Add(folder);
                        if (string.IsNullOrWhiteSpace(GuestName))
                        {
                            GuestName = Path.GetFileName(folder.TrimEnd('\\', '/'));
                        }
                    }
                }

                HasFolders = SourceFolders.Count > 0;
                await CheckDuplicatesAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi chọn thư mục: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        SourceFolders.Remove(folder);
        HasFolders = SourceFolders.Count > 0;
        await CheckDuplicatesAsync();
    }

    [RelayCommand]
    public async Task CheckDuplicatesAsync()
    {
        DuplicateWarnings.Clear();
        HasDuplicateWarnings = false;

        if (SourceFolders.Count == 0) return;

        try
        {
            var warnings = await _customerBillingService.CheckDuplicateSourceFoldersAsync(SourceFolders);
            foreach (var w in warnings)
            {
                DuplicateWarnings.Add(w);
            }
            HasDuplicateWarnings = DuplicateWarnings.Count > 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi kiểm tra trùng lặp: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenPreviousBillAsync(long billId)
    {
        try
        {
            var bill = await _customerBillRepository.GetByIdAsync(billId);
            if (bill == null)
            {
                MessageBox.Show($"Không tìm thấy hóa đơn ID {billId}.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var vm = new CustomerBillReviewViewModel(bill, _customerBillingService, _jpegBillExporter, _excelBillExporter);
            var win = new CustomerBillReviewWindow(vm)
            {
                Owner = Application.Current?.MainWindow
            };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi mở hóa đơn cũ: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task CreateBillAsync()
    {
        if (SourceFolders.Count == 0)
        {
            MessageBox.Show("Vui lòng chọn ít nhất một thư mục nguồn để tạo bill.", "Chưa chọn thư mục", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Đang quét dữ liệu thư mục và tạo hóa đơn khách lẻ...";

            var result = await _customerBillingService.BuildGuestBillDraftAsync(SourceFolders.ToList(), GuestName);

            RequestClose?.Invoke();

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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tạo bill khách lẻ: {ex.Message}";
            MessageBox.Show($"Lỗi tạo bill khách lẻ: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke();
    }
}
