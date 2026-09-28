using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Win32;

namespace LalabAutoReport.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepository;

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

    public event Action? SettingsSaved;

    public SettingsViewModel(ISettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DatabasePath = Path.Combine(appData, "LalabAutoReport", "lalab_autoreport.db");
    }

    public async Task LoadSettingsAsync()
    {
        var settings = await _settingsRepository.GetSettingsAsync();
        RootFolder = settings.RootFolder;
        SupportedExtensions = string.Join(", ", settings.SupportedExtensions);
        AutoScanStartup = settings.AutoScanStartup;
        StartupIncludeYesterday = settings.StartupIncludeYesterday;
        StatusMessage = string.Empty;
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
}
