using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.UI.ViewModels;
using LalabAutoReport.UI.Views;
using Serilog;

namespace LalabAutoReport.UI.Services;

public class QuickBillLauncher
{
    private readonly ICustomerBillingService _customerBillingService;
    private readonly IJpegBillExporter _jpegBillExporter;
    private readonly IExcelBillExporter? _excelBillExporter;
    private readonly ICustomerResolver? _customerResolver;
    private readonly ISettingsRepository? _settingsRepository;

    public QuickBillLauncher(
        ICustomerBillingService customerBillingService,
        IJpegBillExporter jpegBillExporter,
        IExcelBillExporter? excelBillExporter = null,
        ICustomerResolver? customerResolver = null,
        ISettingsRepository? settingsRepository = null)
    {
        _customerBillingService = customerBillingService;
        _jpegBillExporter = jpegBillExporter;
        _excelBillExporter = excelBillExporter;
        _customerResolver = customerResolver;
        _settingsRepository = settingsRepository;
    }

    public async Task LaunchQuickBillForFolderAsync(string rawFolderPath)
    {
        if (string.IsNullOrWhiteSpace(rawFolderPath))
        {
            return;
        }

        // Sanitize path (strip quotes, trailing slashes)
        string folderPath = rawFolderPath.Trim().Trim('\"', '\'').TrimEnd('\\', '/');

        if (!Directory.Exists(folderPath))
        {
            MessageBox.Show($"Thư mục không tồn tại trên ổ đĩa:\n\n{folderPath}", "Thư Mục Không Tồn Tại", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            string folderName = Path.GetFileName(folderPath);
            string guestName = !string.IsNullOrWhiteSpace(folderName) ? folderName : "Khách lẻ";

            if (_settingsRepository != null)
            {
                var settings = await _settingsRepository.GetSettingsAsync();
                if (!string.IsNullOrWhiteSpace(settings.RootFolder))
                {
                    string normRoot = Path.GetFullPath(settings.RootFolder.TrimEnd('\\', '/'));
                    string normTarget = Path.GetFullPath(folderPath);

                    if (string.Equals(normTarget, normRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            "Thư mục bạn vừa chọn là THƯ MỤC GỐC của xưởng in.\n\nQuick Bill chỉ áp dụng cho thư mục chứa đơn hàng của khách cụ thể (hoặc thư mục khách lẻ).\nVui lòng chọn đúng thư mục đơn hàng của khách.",
                            "Không Thể Tạo Quick Bill",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    string rel = Path.GetRelativePath(normRoot, normTarget).Replace('/', '\\').Trim('\\');
                    string[] parts = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 1 && System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^\d{4}[-_.]\d{2}[-_.]\d{2}$"))
                    {
                        MessageBox.Show(
                            $"Thư mục '{parts[0]}' là THƯ MỤC NGÀY của xưởng in.\n\nQuick Bill chỉ áp dụng cho thư mục của từng khách cụ thể (bên trong thư mục ngày).\nVui lòng mở thư mục ngày và chọn thư mục đơn hàng của khách.",
                            "Không Thể Tạo Quick Bill",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    if (parts.Length >= 2 && System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^\d{4}[-_.]\d{2}[-_.]\d{2}$"))
                    {
                        if (string.Equals(parts[1], "KHACH_LE", StringComparison.OrdinalIgnoreCase))
                        {
                            guestName = parts.Length >= 3 ? parts[2] : "Khách lẻ";
                        }
                        else
                        {
                            guestName = parts[1];
                        }
                    }
                    else if (parts.Length >= 2 && string.Equals(parts[0], "KHACH_LE", StringComparison.OrdinalIgnoreCase))
                    {
                        guestName = parts[1];
                    }
                }
            }

            if (_customerResolver != null && !string.IsNullOrWhiteSpace(guestName))
            {
                var match = await _customerResolver.ResolveCustomerAsync(guestName);
                if (match.Status == CustomerResolutionStatus.ExactMatch || match.Status == CustomerResolutionStatus.NormalizedMatch)
                {
                    guestName = match.ResolvedCustomer!.CanonicalName;
                    Log.Information("Quick Bill nhận diện khách quen '{Customer}' (ID {Id}) cho thư mục '{Folder}'",
                        guestName, match.ResolvedCustomer.Id, folderPath);
                }
            }

            Log.Information("Bắt đầu Quick Bill cho thư mục '{Folder}' (Tên khách: '{GuestName}')", folderPath, guestName);

            var result = await _customerBillingService.BuildGuestBillDraftAsync(new[] { folderPath }, guestName, persistDraft: false);

            var vm = new CustomerBillReviewViewModel(
                result.Draft,
                _customerBillingService,
                _jpegBillExporter,
                _excelBillExporter,
                result.Warnings,
                result.BlockingIssues,
                _settingsRepository,
                result.SuggestedCustomer
            );

            var win = new CustomerBillReviewWindow(vm);

            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow != null && mainWindow.IsVisible)
            {
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
                mainWindow.Activate();
                win.Owner = mainWindow;
            }

            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            win.Topmost = true;
            win.Show();
            win.Topmost = false;
            win.Focus();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Lỗi tạo bill khách lẻ cho thư mục {Folder}", folderPath);
            MessageBox.Show($"Lỗi tạo hóa đơn khách lẻ từ thư mục:\n{ex.Message}", "Lỗi Quick Bill", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
