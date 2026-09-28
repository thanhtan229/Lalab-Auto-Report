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

    [ObservableProperty]
    private string _rootFolder = string.Empty;

    [ObservableProperty]
    private string _supportedExtensions = string.Empty;

    [ObservableProperty]
    private bool _autoScanStartup;

    [ObservableProperty]
    private bool _startupIncludeYesterday;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _backupStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBackingUp;

    [ObservableProperty]
    private string _appVersion = "1.0.0";

    public ObservableCollection<DatabaseBackupInfo> RecentBackups { get; } = new();

    public event Action? SettingsSaved;

    public SettingsViewModel(ISettingsRepository settingsRepository, IDatabaseBackupService backupService)
    {
        _settingsRepository = settingsRepository;
        _backupService = backupService;

        DatabasePath = _backupService.GetDatabasePath();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            AppVersion = $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public async Task LoadSettingsAsync()
    {
        var settings = await _settingsRepository.GetSettingsAsync();
        RootFolder = settings.RootFolder;
        SupportedExtensions = string.Join(", ", settings.SupportedExtensions);
        AutoScanStartup = settings.AutoScanStartup;
        StartupIncludeYesterday = settings.StartupIncludeYesterday;
        StatusMessage = string.Empty;
        BackupStatusMessage = string.Empty;

        await LoadBackupsAsync();
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
            SupportedExtensions = exts,
            AutoScanStartup = AutoScanStartup,
            StartupIncludeYesterday = StartupIncludeYesterday
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
}
