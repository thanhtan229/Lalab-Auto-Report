using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Win32;

namespace LalabAutoReport.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly IDatabaseBackupService _backupService;
    private readonly IContextMenuIntegrationService _contextMenuService;
    private readonly IDatabaseResetService? _resetService;
    private readonly IUpdateService? _updateService;

    [ObservableProperty]
    private string _rootFolder = string.Empty;

    [ObservableProperty]
    private string _billExportFolder = string.Empty;

    [ObservableProperty]
    private string _supportedExtensions = string.Empty;

    [ObservableProperty]
    private bool _autoScanStartup;

    [ObservableProperty]
    private bool _startupIncludeYesterday;

    [ObservableProperty]
    private bool _enableIdleScan = true;

    [ObservableProperty]
    private int _idleThresholdMinutes = 30;

    [ObservableProperty]
    private int _idleScanWindowDays = 7;

    public int[] IdleThresholdOptions { get; } = new[] { 15, 30, 45, 60 };

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _backupStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBackingUp;

    [ObservableProperty]
    private bool _isContextMenuRegistered;

    [ObservableProperty]
    private bool _autoRegisterContextMenu = true;

    [ObservableProperty]
    private string _contextMenuStatusMessage = string.Empty;

    [ObservableProperty]
    private string _appVersion = "1.0.0";

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private string _updateStatusMessage = string.Empty;

    private bool _isLoadingSettings;

    public ObservableCollection<DatabaseBackupInfo> RecentBackups { get; } = new();

    public event Action? SettingsSaved;
    public event Action? DataResetCompleted;

    public SettingsViewModel(
        ISettingsRepository settingsRepository,
        IDatabaseBackupService backupService,
        IContextMenuIntegrationService contextMenuService,
        IDatabaseResetService? resetService = null,
        IUpdateService? updateService = null)
    {
        _settingsRepository = settingsRepository;
        _backupService = backupService;
        _contextMenuService = contextMenuService;
        _resetService = resetService;
        _updateService = updateService;

        DatabasePath = _backupService.GetDatabasePath();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            AppVersion = $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public async Task LoadSettingsAsync()
    {
        _isLoadingSettings = true;
        try
        {
            var settings = await _settingsRepository.GetSettingsAsync();
            RootFolder = settings.RootFolder;
            BillExportFolder = settings.BillExportFolder;
            SupportedExtensions = string.Join(", ", settings.SupportedExtensions);
            AutoScanStartup = settings.AutoScanStartup;
            StartupIncludeYesterday = settings.StartupIncludeYesterday;
            AutoRegisterContextMenu = settings.AutoRegisterContextMenu;
            EnableIdleScan = settings.EnableIdleScan;
            IdleThresholdMinutes = settings.IdleThresholdMinutes;
            IdleScanWindowDays = settings.IdleScanWindowDays;
            StatusMessage = string.Empty;
            BackupStatusMessage = string.Empty;
            ContextMenuStatusMessage = string.Empty;

            IsContextMenuRegistered = _contextMenuService.IsContextMenuRegistered();
            if (AutoRegisterContextMenu && !IsContextMenuRegistered)
            {
                try
                {
                    _contextMenuService.EnsureContextMenuRegistered();
                    IsContextMenuRegistered = _contextMenuService.IsContextMenuRegistered();
                }
                catch { }
            }

            await LoadBackupsAsync();
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    public async Task LoadBackupsAsync()
    {
        try
        {
            var backups = await _backupService.GetBackupsAsync();
            RecentBackups.Clear();
            foreach (var b in backups)
            {
                RecentBackups.Add(b);
            }
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Không thể tải danh sách sao lưu: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseRootFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn Thư Mục Gốc Chứa Các Ngày (Root Folder)",
            InitialDirectory = Directory.Exists(RootFolder) ? RootFolder : string.Empty
        };

        if (dialog.ShowDialog() == true)
        {
            RootFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseBillExportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn Thư Mục Xuất Bill",
            InitialDirectory = Directory.Exists(BillExportFolder) ? BillExportFolder : string.Empty
        };

        if (dialog.ShowDialog() == true)
        {
            BillExportFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var exts = SupportedExtensions
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToList();

        var settings = new AppSettings
        {
            RootFolder = RootFolder.Trim(),
            BillExportFolder = BillExportFolder.Trim(),
            SupportedExtensions = exts,
            AutoScanStartup = AutoScanStartup,
            StartupIncludeYesterday = StartupIncludeYesterday,
            AutoRegisterContextMenu = AutoRegisterContextMenu,
            EnableIdleScan = EnableIdleScan,
            IdleThresholdMinutes = IdleThresholdMinutes,
            IdleScanWindowDays = IdleScanWindowDays
        };

        await _settingsRepository.SaveSettingsAsync(settings);
        StatusMessage = "Đã lưu cài đặt thành công!";
        SettingsSaved?.Invoke();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        string dir = Path.GetDirectoryName(DatabasePath) ?? string.Empty;
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

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        try
        {
            IsBackingUp = true;
            BackupStatusMessage = "Đang sao lưu cơ sở dữ liệu...";
            var result = await _backupService.CreateBackupAsync();
            BackupStatusMessage = $"Sao lưu thành công: {result.FileName} ({result.FileSizeBytes / 1024:N0} KB)";
            await LoadBackupsAsync();
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Sao lưu thất bại: {ex.Message}";
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(DatabaseBackupInfo? backup)
    {
        if (backup == null) return;

        var result = MessageBox.Show(
            $"Bạn có chắc chắn muốn khôi phục dữ liệu từ bản sao lưu:\n\n{backup.FileName}\n\n(Dữ liệu hiện tại sẽ được tự động sao lưu an toàn trước khi khôi phục)?",
            "Xác nhận Khôi Phục Dữ Liệu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result != MessageBoxResult.Yes) return;

        try
        {
            IsBackingUp = true;
            BackupStatusMessage = "Đang khôi phục dữ liệu...";
            await _backupService.RestoreBackupAsync(backup.BackupPath);
            BackupStatusMessage = $"Khôi phục thành công từ {backup.FileName}! Khởi động lại ứng dụng hoặc tải lại màn hình để cập nhật.";
            await LoadBackupsAsync();
            MessageBox.Show("Khôi phục cơ sở dữ liệu thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Khôi phục thất bại: {ex.Message}";
            MessageBox.Show($"Khôi phục thất bại: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    private async Task BrowseAndRestoreAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file sao lưu SQLite (*.db)",
            Filter = "SQLite Database (*.db)|*.db|Tất cả tệp (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            var info = new DatabaseBackupInfo(
                BackupPath: dialog.FileName,
                FileName: Path.GetFileName(dialog.FileName),
                FileSizeBytes: new FileInfo(dialog.FileName).Length,
                CreatedAtUtc: File.GetCreationTimeUtc(dialog.FileName)
            );
            await RestoreBackupAsync(info);
        }
    }

    [RelayCommand]
    private async Task ResetDataAsync()
    {
        if (_resetService == null) return;

        var ownerWindow = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                          ?? Application.Current?.MainWindow;
        var (confirmed, scope) = Views.ResetConfirmationDialog.Show(ownerWindow);
        if (!confirmed) return;

        try
        {
            IsBackingUp = true;
            BackupStatusMessage = "Đang tạo bản sao lưu an toàn và tiến hành đặt lại dữ liệu...";

            var backupInfo = await _resetService.ResetDataAsync(scope);

            await LoadSettingsAsync();
            await LoadBackupsAsync();

            BackupStatusMessage = $"Đã đặt lại dữ liệu thành công! Bản sao lưu an toàn: {backupInfo.FileName}";

            DataResetCompleted?.Invoke();

            string scopeName = scope == ResetDataScope.OperationalOnly
                ? "Dữ liệu vận hành (đơn hàng, lịch sử quét, hóa đơn, trạng thái đã in) đã được xóa sạch. Danh mục Khách hàng và Bảng giá được giữ nguyên 100%."
                : "Toàn bộ cơ sở dữ liệu đã được đưa về trạng thái xuất xưởng ban đầu.";

            MessageBox.Show(
                $"Đã hoàn tất đặt lại dữ liệu!\n\n{scopeName}\n\n🛡️ Bản sao lưu an toàn đã được lưu tại:\n{backupInfo.FileName}\n\nỨng dụng đã tự động làm mới toàn bộ dữ liệu.",
                "Đặt Lại Dữ Liệu Thành Công",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Đặt lại dữ liệu thất bại: {ex.Message}";
            MessageBox.Show($"Đặt lại dữ liệu thất bại: {ex.Message}", "Lỗi Đặt Lại Dữ Liệu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    partial void OnAutoRegisterContextMenuChanged(bool value)
    {
        if (_isLoadingSettings) return;

        if (value)
        {
            try
            {
                _contextMenuService.RegisterContextMenu();
                IsContextMenuRegistered = _contextMenuService.IsContextMenuRegistered();
                ContextMenuStatusMessage = "Đã kích hoạt menu chuột phải '⚡ QUICK BILL (Lalab)'!";
            }
            catch (Exception ex)
            {
                ContextMenuStatusMessage = $"Lỗi kích hoạt menu chuột phải: {ex.Message}";
            }
        }
        else
        {
            try
            {
                _contextMenuService.UnregisterContextMenu();
                IsContextMenuRegistered = _contextMenuService.IsContextMenuRegistered();
                ContextMenuStatusMessage = "Đã tắt và gỡ bỏ menu chuột phải khỏi Windows Explorer.";
            }
            catch (Exception ex)
            {
                ContextMenuStatusMessage = $"Lỗi gỡ bỏ menu chuột phải: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private void RegisterContextMenu()
    {
        try
        {
            _contextMenuService.RegisterContextMenu();
            IsContextMenuRegistered = _contextMenuService.IsContextMenuRegistered();
            if (!AutoRegisterContextMenu)
            {
                AutoRegisterContextMenu = true;
            }
            ContextMenuStatusMessage = "Đã kích hoạt thành công menu chuột phải '⚡ QUICK BILL (Lalab)'!";
        }
        catch (Exception ex)
        {
            ContextMenuStatusMessage = $"Lỗi kích hoạt menu chuột phải: {ex.Message}";
            MessageBox.Show(ContextMenuStatusMessage, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void UnregisterContextMenu()
    {
        try
        {
            _contextMenuService.UnregisterContextMenu();
            IsContextMenuRegistered = _contextMenuService.IsContextMenuRegistered();
            if (AutoRegisterContextMenu)
            {
                AutoRegisterContextMenu = false;
            }
            ContextMenuStatusMessage = "Đã gỡ bỏ menu chuột phải khỏi File Explorer.";
        }
        catch (Exception ex)
        {
            ContextMenuStatusMessage = $"Lỗi gỡ bỏ menu chuột phải: {ex.Message}";
            MessageBox.Show(ContextMenuStatusMessage, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ExportRegFile()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Xuất file Registry cài đặt Menu Chuột Phải",
                Filter = "Registry Files (*.reg)|*.reg",
                FileName = "Lalab_QuickBill_ContextMenu.reg"
            };

            if (dialog.ShowDialog() == true)
            {
                var content = _contextMenuService.GenerateRegFileContent();
                File.WriteAllText(dialog.FileName, content, System.Text.Encoding.Unicode);
                MessageBox.Show($"Đã xuất file .reg thành công:\n{dialog.FileName}\n\nBạn có thể copy file này sang máy tính khác trong xưởng và double-click để cài đặt nhanh!", "Xuất File Registry", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi xuất file .reg: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task CheckForUpdateAsync()
    {
        if (_updateService == null)
        {
            UpdateStatusMessage = "Chức năng cập nhật chưa được khởi tạo.";
            return;
        }

        IsCheckingUpdate = true;
        UpdateStatusMessage = "Đang kiểm tra bản cập nhật mới trên GitHub...";

        try
        {
            var updateInfo = await _updateService.CheckForUpdateAsync();
            if (updateInfo.IsUpdateAvailable)
            {
                UpdateStatusMessage = $"Đã có bản cập nhật mới: {updateInfo.LatestVersion}!";
                var updateVm = new UpdateViewModel(_updateService);
                updateVm.Initialize(updateInfo);
                var dialog = new Views.UpdateDialog(updateVm)
                {
                    Owner = Application.Current.MainWindow
                };
                dialog.ShowDialog();
            }
            else
            {
                UpdateStatusMessage = $"Hệ thống đang ở phiên bản mới nhất ({updateInfo.CurrentVersion}).";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Lỗi kiểm tra cập nhật: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }
}
