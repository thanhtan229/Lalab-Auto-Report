using CommunityToolkit.Mvvm.ComponentModel;

namespace LalabAutoReport.UI.ViewModels;

public partial class RootFolderDisplayItem : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathDisplay))]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(IsInactive))]
    [NotifyPropertyChangedFor(nameof(ToggleActiveButtonText))]
    private bool _isActive = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetDefault))]
    private bool _isDefault;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    private bool _isPathAccessible = true;

    public string PathDisplay => string.IsNullOrWhiteSpace(FullPath) ? "(Chưa đặt đường dẫn)" : FullPath;

    public bool IsInactive => !IsActive;

    public bool CanSetDefault => !IsDefault;

    public string ToggleActiveButtonText => IsActive ? "⏸️ Tạm ngưng quét" : "▶️ Bật quét kho";

    public string StatusBadgeText
    {
        get
        {
            if (!IsActive) return "⚪ Lưu trữ (Không quét)";
            if (!IsPathAccessible) return "🟡 Chưa kết nối ổ đĩa";
            return "🟢 Đang quét";
        }
    }
}
