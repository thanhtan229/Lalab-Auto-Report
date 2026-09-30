using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.UI.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;

    [ObservableProperty]
    private UpdateInfo _updateInfo = new();

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public UpdateViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    public void Initialize(UpdateInfo updateInfo)
    {
        UpdateInfo = updateInfo;
    }

    [RelayCommand]
    private async Task StartUpdateAsync()
    {
        IsDownloading = true;
        ErrorMessage = string.Empty;
        ProgressText = "Đang kết nối tải bản cập nhật mới...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p * 100.0;
                if (UpdateInfo.FileSizeBytes > 0)
                {
                    var downloadedMb = (UpdateInfo.FileSizeBytes * p) / (1024.0 * 1024.0);
                    var totalMb = UpdateInfo.FileSizeBytes / (1024.0 * 1024.0);
                    ProgressText = $"Đang tải: {DownloadProgress:F0}% ({downloadedMb:F1} / {totalMb:F1} MB)...";
                }
                else
                {
                    ProgressText = $"Đang tải: {DownloadProgress:F0}%...";
                }
            });

            await _updateService.DownloadAndApplyUpdateAsync(UpdateInfo, progress);
        }
        catch (Exception ex)
        {
            IsDownloading = false;
            ErrorMessage = $"Lỗi cập nhật: {ex.Message}";
        }
    }
}
