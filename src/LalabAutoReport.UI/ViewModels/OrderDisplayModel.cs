using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.UI.ViewModels;

public partial class OrderDisplayModel : ObservableObject
{
    public Order Order { get; }

    public long Id => Order.Id;
    public string OrderCode => Order.OrderCode;
    public string DisplayOrderCode => !string.IsNullOrWhiteSpace(OrderCode) ? OrderCode : $"#{Id}";
    public string OriginalFolderName => Order.OriginalFolderName;
    public string RelativePath => Order.RelativePath;
    public string WorkDate => Order.WorkDate;

    public OrderKind Kind => Order.OrderKind;
    public string? OrderName => Order.OrderName;
    public string FullOrderDisplayName => Kind == OrderKind.Explicit && !string.IsNullOrWhiteSpace(OrderName)
        ? $"{OriginalFolderName} / {OrderName}"
        : OriginalFolderName;
    public string OrderTypeBadge => Kind == OrderKind.Explicit
        ? (!string.IsNullOrWhiteSpace(OrderName) ? $"Đơn: {OrderName}" : "Đơn con")
        : "Đơn trực tiếp";

    public Customer? Customer => Order.Customer;
    public long? CustomerId => Order.CustomerId;
    public string? CanonicalCustomerName => Order.Customer?.CanonicalName;
    public bool HasCustomer => !string.IsNullOrWhiteSpace(CanonicalCustomerName);
    public bool IsAliasDiffering => HasCustomer && !string.Equals(OriginalFolderName, CanonicalCustomerName, StringComparison.OrdinalIgnoreCase);

    public long? RootFolderId => Order.RootFolderId;
    public string? RootFolderName => Order.RootFolderName;
    public bool HasRootFolder => !string.IsNullOrWhiteSpace(RootFolderName);
    public string RootBadgeText => !string.IsNullOrWhiteSpace(RootFolderName) ? $"📁 {RootFolderName}" : "📁 Kho";

    public bool IsGuest => !HasCustomer;

    public string CustomerBadgeText
    {
        get
        {
            if (HasCustomer)
            {
                return $"👤 {CanonicalCustomerName}";
            }

            string guestName = !string.IsNullOrWhiteSpace(OrderName) ? OrderName : OriginalFolderName;
            if (string.IsNullOrWhiteSpace(guestName) ||
                string.Equals(guestName, "khach_le", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(guestName, "khách lẻ", StringComparison.OrdinalIgnoreCase))
            {
                return "⚡ Khách lẻ";
            }

            return $"⚡ Khách lẻ: {guestName}";
        }
    }

    public string CustomerBadgeBg => HasCustomer ? "#E0F2FE" : "#FEF3C7";

    public string CustomerBadgeBorder => HasCustomer ? "#7DD3FC" : "#FCD34D";

    public string CustomerBadgeFg => HasCustomer ? "#0369A1" : "#B45309";

    public string CustomerMappingTooltip
    {
        get
        {
            if (IsLocked || Status == OrderStatus.Billed)
            {
                return "Đơn hàng đã được tính vào hóa đơn. Vui lòng kiểm tra tab KHÁCH HÀNG nếu muốn điều chỉnh.";
            }

            if (HasCustomer)
            {
                if (IsAliasDiffering)
                {
                    return $"Thư mục '{OriginalFolderName}' được nhận diện thuộc khách hàng '{CanonicalCustomerName}' (qua biệt danh / alias).\n(Nhấn vào đây để đổi khách hàng)";
                }
                return $"Khách hàng chuẩn: {CanonicalCustomerName}\n(Nhấn vào đây để đổi khách hàng)";
            }

            return $"Thư mục '{OriginalFolderName}' được xếp vào phân loại Khách lẻ (áp dụng bảng giá lẻ).\n(Nhấn vào đây để gán vào khách quen nếu cần)";
        }
    }

    [ObservableProperty]
    private OrderStatus _status;

    partial void OnStatusChanged(OrderStatus value)
    {
        OnPropertyChanged(nameof(IsBilled));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(CanLock));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(DeleteOrderTooltip));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeBg));
        OnPropertyChanged(nameof(StatusBadgeFg));
        OnPropertyChanged(nameof(StatusBadgeBorder));
    }

    [ObservableProperty]
    private bool _filesystemChangedAfterLock;

    [ObservableProperty]
    private bool _isPrinted;

    [ObservableProperty]
    private PrintStatus _printProgress;

    partial void OnIsPrintedChanged(bool value)
    {
        Order.IsPrinted = value;
        NotifyPrintProgressChanged();
    }

    partial void OnPrintProgressChanged(PrintStatus value)
    {
        Order.PrintProgress = value;
        NotifyPrintProgressChanged();
    }

    public int PrintedItemsCount => Items.Count(i => i.IsPrinted);
    public int TotalItemsCount => Items.Count;

    public string PrintedStatusText
    {
        get
        {
            if (Items.Count > 1)
            {
                int printed = PrintedItemsCount;
                int total = TotalItemsCount;
                if (printed == 0) return $"⚪ Chưa in (0/{total})";
                if (printed < total) return $"🔵 Đang in ({printed}/{total})";
                return $"🟢 Đã in đủ ({total}/{total})";
            }

            if (IsPrinted || PrintProgress == PrintStatus.Printed)
                return "🏷️ ĐÃ IN";

            return "Chưa in";
        }
    }

    public string PrintedStatusBadgeBg
    {
        get
        {
            if (Items.Count > 1)
            {
                int printed = PrintedItemsCount;
                int total = TotalItemsCount;
                if (printed == 0) return "#F3F4F6";
                if (printed < total) return "#DBEAFE"; // Blue 100
                return "#DFF2E7"; // Green 100
            }
            return (IsPrinted || PrintProgress == PrintStatus.Printed) ? "#FEE2E2" : "#F3F4F6";
        }
    }

    public string PrintedStatusBadgeBorder
    {
        get
        {
            if (Items.Count > 1)
            {
                int printed = PrintedItemsCount;
                int total = TotalItemsCount;
                if (printed == 0) return "#E5E7EB";
                if (printed < total) return "#93C5FD"; // Blue 300
                return "#B8E4CC"; // Green 300
            }
            return (IsPrinted || PrintProgress == PrintStatus.Printed) ? "#F87171" : "#E5E7EB";
        }
    }

    public string PrintedStatusBadgeFg
    {
        get
        {
            if (Items.Count > 1)
            {
                int printed = PrintedItemsCount;
                int total = TotalItemsCount;
                if (printed == 0) return "#6B7280";
                if (printed < total) return "#1D4ED8"; // Blue 700
                return "#124548"; // Green 700
            }
            return (IsPrinted || PrintProgress == PrintStatus.Printed) ? "#DC2626" : "#6B7280";
        }
    }

    public void NotifyPrintProgressChanged()
    {
        OnPropertyChanged(nameof(PrintedItemsCount));
        OnPropertyChanged(nameof(TotalItemsCount));
        OnPropertyChanged(nameof(PrintedStatusText));
        OnPropertyChanged(nameof(PrintedStatusBadgeBg));
        OnPropertyChanged(nameof(PrintedStatusBadgeBorder));
        OnPropertyChanged(nameof(PrintedStatusBadgeFg));
    }

    [ObservableProperty]
    private bool _isDelivered;

    [ObservableProperty]
    private DateTimeOffset? _deliveredAt;

    [ObservableProperty]
    private string? _deliveredBy;

    [ObservableProperty]
    private string? _note;

    partial void OnIsDeliveredChanged(bool value)
    {
        Order.IsDelivered = value;
        OnPropertyChanged(nameof(DeliveredStatusText));
        OnPropertyChanged(nameof(DeliveredStatusBadgeBg));
        OnPropertyChanged(nameof(DeliveredStatusBadgeBorder));
        OnPropertyChanged(nameof(DeliveredStatusBadgeFg));
        OnPropertyChanged(nameof(DeliveredDetailText));
        OnPropertyChanged(nameof(DeliveredButtonTooltip));
    }

    partial void OnDeliveredAtChanged(DateTimeOffset? value)
    {
        Order.DeliveredAt = value;
        OnPropertyChanged(nameof(DeliveredDetailText));
        OnPropertyChanged(nameof(DeliveredButtonTooltip));
    }

    partial void OnDeliveredByChanged(string? value)
    {
        Order.DeliveredBy = value;
        OnPropertyChanged(nameof(DeliveredDetailText));
        OnPropertyChanged(nameof(DeliveredButtonTooltip));
    }

    partial void OnNoteChanged(string? value)
    {
        Order.Note = value;
        OnPropertyChanged(nameof(HasNote));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeliveredStatusText))]
    private bool _hasPendingCloudChanges;
    partial void OnHasPendingCloudChangesChanged(bool value) => Order.HasPendingCloudChanges = value;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public string DeliveredStatusText => (IsDelivered ? "🚚 ĐÃ GIAO" : "Chưa giao") + (Order.HasPendingCloudChanges ? " · Chờ Cloud" : "");
    public string DeliveredStatusBadgeBg => IsDelivered ? "#DBEAFE" : "#F3F4F6";
    public string DeliveredStatusBadgeBorder => IsDelivered ? "#93C5FD" : "#E5E7EB";
    public string DeliveredStatusBadgeFg => IsDelivered ? "#1D4ED8" : "#6B7280";

    public string DeliveredDetailText
    {
        get
        {
            if (!IsDelivered) return "Chưa giao hàng";
            var timeStr = DeliveredAt?.ToString("dd/MM/yyyy HH:mm") ?? "";
            return !string.IsNullOrWhiteSpace(timeStr) ? $"Đã giao lúc {timeStr}" : "Đã giao hàng";
        }
    }

    public string DeliveredButtonTooltip
    {
        get
        {
            if (!IsDelivered) return "Chưa giao hàng (Bấm để chuyển sang ĐÃ GIAO)";
            var timeStr = DeliveredAt?.ToString("dd/MM/yyyy HH:mm") ?? "";
            var info = !string.IsNullOrWhiteSpace(timeStr) ? $"Đã giao lúc {timeStr}" : "Đã giao hàng";
            return $"{info}\n(Bấm để hoàn tác về Chưa giao)";
        }
    }

    [ObservableProperty]
    private ObservableCollection<OrderItemDisplayModel> _items = new();

    public int TotalSourceCount => Items.Sum(i => i.SourceCount);
    public int TotalPrintCount => Items.Sum(i => i.PrintCount ?? 0);
    public int TotalBillQuantity => Items.Sum(i => i.BillQuantity ?? 0);
    public int TotalAlbums => Items.Count(i => i.IsAlbum);
    public int TotalPhotos => Items.Where(i => !i.IsAlbum).Sum(i => i.PrintCount ?? 0);
    public long TotalAmount => Items.Sum(i => i.LineTotal ?? 0);
    public string FormattedTotalAmount => TotalAmount > 0 ? $"{TotalAmount:N0} đ" : "-";

    public bool HasIssues => Status == OrderStatus.NeedsReview || Status == OrderStatus.Error || FilesystemChangedAfterLock;
    public bool IsLocked => Status == OrderStatus.Locked;
    public bool IsBilled => Status == OrderStatus.Locked || Status == OrderStatus.Billed;
    public bool CanLock => (Status == OrderStatus.Ready || Status == OrderStatus.Billed) && !HasIssues;
    public bool CanDelete => !IsBilled && Status != OrderStatus.Locked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BillExportStatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(BillExportStatusBadgeBg))]
    [NotifyPropertyChangedFor(nameof(BillExportStatusBadgeBorder))]
    [NotifyPropertyChangedFor(nameof(BillExportStatusBadgeFg))]
    [NotifyPropertyChangedFor(nameof(BillExportStatusTooltip))]
    private bool _isBillExported;

    public string BillExportStatusBadgeText => IsBillExported ? "📄 Đã xuất bill" : "⏳ Chưa xuất bill";
    public string BillExportStatusBadgeBg => IsBillExported ? "#D1FAE5" : "#FEF3C7";
    public string BillExportStatusBadgeBorder => IsBillExported ? "#6EE7B7" : "#FCD34D";
    public string BillExportStatusBadgeFg => IsBillExported ? "#065F46" : "#B45309";
    public string BillExportStatusTooltip => IsBillExported 
        ? "Đơn hàng này đã được lập và xuất hóa đơn JPEG thành công" 
        : "Đơn hàng này chưa được xuất hóa đơn";
    public string DeleteOrderTooltip => CanDelete
        ? "Xóa đơn hàng này khỏi danh sách (chỉ xóa bản ghi trong phần mềm, không xóa ảnh trên ổ đĩa)"
        : "Đơn hàng đã tính bill hoặc đã khóa, không thể xóa";

    public string StatusText
    {
        get
        {
            if (FilesystemChangedAfterLock)
                return "⚠️ Đã tính Bill (File thay đổi!)";

            return Status switch
            {
                OrderStatus.Unscanned => "Chưa quét",
                OrderStatus.Scanning => "Đang quét...",
                OrderStatus.Scanned => "Đã quét",
                OrderStatus.NeedsReview => "Cần xử lý",
                OrderStatus.Ready => "Sẵn sàng",
                OrderStatus.Billed => "Đã tính Bill",
                OrderStatus.Locked => "Đã tính Bill",
                OrderStatus.Error => "Lỗi",
                _ => Status.ToString()
            };
        }
    }

    public string StatusBadgeColor => StatusBadgeFg;

    public string StatusBadgeBg
    {
        get
        {
            if (FilesystemChangedAfterLock) return "#F7DFDC";
            return Status switch
            {
                OrderStatus.Ready => "#DFF2E7",
                OrderStatus.NeedsReview => "#FFF0C9",
                OrderStatus.Locked or OrderStatus.Billed => "#CAEBFF",
                OrderStatus.Error => "#F7DFDC",
                _ => "#EAE8E4"
            };
        }
    }

    public string StatusBadgeFg
    {
        get
        {
            if (FilesystemChangedAfterLock) return "#D9002B";
            return Status switch
            {
                OrderStatus.Ready => "#124548",
                OrderStatus.NeedsReview => "#8A5700",
                OrderStatus.Locked or OrderStatus.Billed => "#15295A",
                OrderStatus.Error => "#D9002B",
                _ => "#5F6368"
            };
        }
    }

    public string StatusBadgeBorder
    {
        get
        {
            if (FilesystemChangedAfterLock) return "#E8A4A1";
            return Status switch
            {
                OrderStatus.Ready => "#B8E4CC",
                OrderStatus.NeedsReview => "#E9C66E",
                OrderStatus.Locked or OrderStatus.Billed => "#A6DCFF",
                OrderStatus.Error => "#E8A4A1",
                _ => "#CFCCCB"
            };
        }
    }

    public OrderDisplayModel(Order order)
    {
        Order = order;
        _status = order.Status;
        _filesystemChangedAfterLock = order.FilesystemChangedAfterLock;
        _isPrinted = order.IsPrinted;
        _printProgress = order.PrintProgress;
        _hasPendingCloudChanges = order.HasPendingCloudChanges;
        _isDelivered = order.IsDelivered;
        _deliveredAt = order.DeliveredAt;
        _deliveredBy = order.DeliveredBy;
        _note = order.Note;
        foreach (var item in order.Items)
        {
            _items.Add(new OrderItemDisplayModel(item, this));
        }
    }

    public void NotifyTotalsChanged()
    {
        OnPropertyChanged(nameof(TotalSourceCount));
        OnPropertyChanged(nameof(TotalPrintCount));
        OnPropertyChanged(nameof(TotalBillQuantity));
        OnPropertyChanged(nameof(TotalAlbums));
        OnPropertyChanged(nameof(TotalPhotos));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(FormattedTotalAmount));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsBilled));
        OnPropertyChanged(nameof(CanLock));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeColor));
        OnPropertyChanged(nameof(CanonicalCustomerName));
        OnPropertyChanged(nameof(HasCustomer));
        OnPropertyChanged(nameof(IsAliasDiffering));
        OnPropertyChanged(nameof(IsGuest));
        OnPropertyChanged(nameof(CustomerBadgeText));
        OnPropertyChanged(nameof(CustomerBadgeBg));
        OnPropertyChanged(nameof(CustomerBadgeBorder));
        OnPropertyChanged(nameof(CustomerBadgeFg));
        OnPropertyChanged(nameof(CustomerMappingTooltip));
    }

    public string? ThumbnailCandidateRelativePath => Order.ThumbnailCandidateRelativePath;
    public string? FullThumbnailCandidatePath { get; private set; }

    [ObservableProperty]
    private ImageSource? _thumbnailImage;

    [ObservableProperty]
    private bool _isThumbnailLoading;

    [ObservableProperty]
    private bool _isThumbnailLoaded;

    [ObservableProperty]
    private string? _thumbnailFormatExtension;

    public bool HasThumbnailImage => ThumbnailImage != null;
    public bool IsUnsupportedFormat => !string.IsNullOrEmpty(ThumbnailFormatExtension);
    public bool IsThumbnailPlaceholderVisible => !HasThumbnailImage && !IsUnsupportedFormat;
    public string ThumbnailTooltip => !string.IsNullOrEmpty(FullThumbnailCandidatePath)
        ? $"Ảnh đại diện: {System.IO.Path.GetFileName(FullThumbnailCandidatePath)}\nNhấn để xem trước (phóng to)"
        : "Chưa có ảnh đại diện";

    partial void OnThumbnailImageChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(HasThumbnailImage));
        OnPropertyChanged(nameof(IsThumbnailPlaceholderVisible));
    }

    partial void OnThumbnailFormatExtensionChanged(string? value)
    {
        OnPropertyChanged(nameof(IsUnsupportedFormat));
        OnPropertyChanged(nameof(IsThumbnailPlaceholderVisible));
    }

    public async Task LoadThumbnailAsync(IThumbnailService thumbnailService, string? rootFolderPath, CancellationToken cancellationToken = default)
    {
        if (IsThumbnailLoaded || HasThumbnailImage || IsUnsupportedFormat) return;

        string? candRelPath = ThumbnailCandidateRelativePath;
        if (string.IsNullOrEmpty(candRelPath) && Items.Count > 0)
        {
            candRelPath = Items.FirstOrDefault(i => !string.IsNullOrEmpty(i.Item.ThumbnailCandidateRelativePath))
                ?.Item.ThumbnailCandidateRelativePath;
        }

        if (string.IsNullOrEmpty(candRelPath) || string.IsNullOrEmpty(rootFolderPath))
        {
            IsThumbnailLoaded = true;
            return;
        }

        FullThumbnailCandidatePath = System.IO.Path.Combine(rootFolderPath, candRelPath);

        IsThumbnailLoading = true;
        try
        {
            var result = await thumbnailService.GetThumbnailAsync(FullThumbnailCandidatePath, cancellationToken);
            if (result.Status == ThumbnailStatus.Success && !string.IsNullOrEmpty(result.CachedFilePath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(result.CachedFilePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                ThumbnailImage = bitmap;
                IsThumbnailLoaded = true;
            }
            else if (result.Status == ThumbnailStatus.UnsupportedFormat)
            {
                ThumbnailFormatExtension = result.FormatExtension ?? "IMG";
                IsThumbnailLoaded = true;
            }
            else
            {
                IsThumbnailLoaded = true;
            }
        }
        catch
        {
            IsThumbnailLoaded = true;
        }
        finally
        {
            IsThumbnailLoading = false;
            OnPropertyChanged(nameof(ThumbnailTooltip));
        }
    }
}

public partial class OrderItemDisplayModel : ObservableObject
{
    public OrderItemScan Item { get; }
    public OrderDisplayModel ParentOrder { get; }

    public long Id => Item.Id;
    public string SpecName => Item.SpecificationFolderName;
    public string SpecRelativePath => Item.SpecificationRelativePath;
    public int SourceCount => Item.SourceCount;

    [ObservableProperty]
    private int? _printCount;

    [ObservableProperty]
    private int? _mismatchCount;

    [ObservableProperty]
    private PrintFolderResolutionStatus _printFolderStatus;

    [ObservableProperty]
    private string? _selectedPrintFolderRelativePath;

    [ObservableProperty]
    private int? _billQuantity;

    [ObservableProperty]
    private QuantityResolutionMode? _resolutionMode;

    [ObservableProperty]
    private string? _resolutionNote;

    [ObservableProperty]
    private BillingFolderResolutionMode _folderResolutionMode;

    [ObservableProperty]
    private bool _isPrinted;

    [ObservableProperty]
    private DateTimeOffset? _printedAt;

    partial void OnIsPrintedChanged(bool value)
    {
        Item.IsPrinted = value;
        OnPropertyChanged(nameof(ItemPrintedBadgeText));
        OnPropertyChanged(nameof(ItemPrintedBadgeBg));
        OnPropertyChanged(nameof(ItemPrintedBadgeBorder));
        OnPropertyChanged(nameof(ItemPrintedBadgeFg));
        ParentOrder.NotifyPrintProgressChanged();
    }

    public string ItemPrintedBadgeText => IsPrinted ? "✓ Đã in" : "🖨️ Chưa in";
    public string ItemPrintedBadgeBg => IsPrinted ? "#DFF2E7" : "#F3F4F6";
    public string ItemPrintedBadgeBorder => IsPrinted ? "#B8E4CC" : "#E5E7EB";
    public string ItemPrintedBadgeFg => IsPrinted ? "#124548" : "#6B7280";

    public string FolderResolutionModeText => FolderResolutionMode == BillingFolderResolutionMode.ManuallySelected ? "Thủ công" : "Tự động";
    public string EffectiveBillingFolderPath => SelectedPrintFolderRelativePath ?? "-";
    public string EffectiveBillingFolderTooltip => $"Thư mục tính số lượng: {EffectiveBillingFolderPath} ({FolderResolutionModeText})";

    public ProductCategory Category => Item.PrintSpecification?.Category ?? ProductCategory.PhotoPrint;
    public BillingMethod BillingMethod => Item.PrintSpecification?.BillingMethod ?? BillingMethod.FileCount;
    public bool IsAlbum => Category == ProductCategory.Album || BillingMethod == BillingMethod.AlbumBasePlusExtra;

    public int PrintableFileCount => Item.PrintableFileCount > 0 ? Item.PrintableFileCount.Value : (PrintCount ?? 0);
    public int SheetCount => PrintableFileCount;
    public int IncludedSheets => Item.PrintSpecification?.IncludedSheets ?? 10;
    public int ExtraSheets => Math.Max(0, SheetCount - IncludedSheets);
    public long BasePrice => Item.PrintSpecification?.BasePrice ?? 0;
    public long ExtraSheetPrice => Item.PrintSpecification?.ExtraSheetPrice ?? 0;

    public bool IsAmbiguous => PrintFolderStatus == PrintFolderResolutionStatus.AmbiguousPrintFolder;
    public bool HasMismatch => MismatchCount.HasValue && MismatchCount.Value != 0;
    public IReadOnlyList<string> CandidateFolders => Item.CandidatePrintFolderRelativePaths;

    public long? UnitPrice => Item.PrintSpecification?.UnitPrice;

    public string FormattedUnitPrice
    {
        get
        {
            if (Item.PrintSpecification == null) return "--";
            if (IsAlbum)
            {
                return $"{BasePrice:N0} đ({IncludedSheets} tờ)(+{FormatExtraSheetPrice(ExtraSheetPrice)}/tờ)";
            }
            return UnitPrice.HasValue ? $"{UnitPrice.Value:N0} đ/ảnh" : "--";
        }
    }

    private static string FormatExtraSheetPrice(long price)
    {
        if (price >= 1000 && price % 1000 == 0)
            return $"{price / 1000}k";
        if (price >= 1000 && price % 100 == 0)
            return $"{(double)price / 1000:0.#}k";
        if (price == 0)
            return "0k";
        return $"{price:N0}đ";
    }

    public string FormattedQuantityText
    {
        get
        {
            if (IsAlbum)
            {
                if (!PrintCount.HasValue || PrintCount.Value == 0)
                    return "0 tờ (0 album)";
                return $"{SheetCount} tờ (1 album)";
            }
            return PrintCount.HasValue ? $"{PrintCount.Value} ảnh" : "--";
        }
    }

    public long? LineTotal
    {
        get
        {
            if (Item.PrintSpecification == null) return null;
            if (IsAlbum)
            {
                if (PrintCount == 0) return null;
                return BasePrice + ((long)ExtraSheets * ExtraSheetPrice);
            }
            return BillQuantity.HasValue && UnitPrice.HasValue ? (long)BillQuantity.Value * UnitPrice.Value : null;
        }
    }

    public string FormattedLineTotal => LineTotal.HasValue ? $"{LineTotal.Value:N0} đ" : "--";

    public string MismatchText
    {
        get
        {
            if (PrintFolderStatus == PrintFolderResolutionStatus.NoPrintFolder)
                return "Không có thư mục in";
            if (PrintFolderStatus == PrintFolderResolutionStatus.AmbiguousPrintFolder)
                return "Trùng/Đa nhánh in";
            if (IsAlbum)
                return SheetCount < IncludedSheets ? "Chưa đủ tờ chuẩn" : (ExtraSheets > 0 ? $"+{ExtraSheets} tờ thêm" : "Đủ tờ chuẩn");
            return "Chuẩn";
        }
    }

    public string StatusText
    {
        get
        {
            if (PrintFolderStatus == PrintFolderResolutionStatus.NoPrintFolder)
                return "Chưa có ảnh in";
            if (PrintFolderStatus == PrintFolderResolutionStatus.AmbiguousPrintFolder)
                return "Cần chọn nhánh in";
            if (Item.PrintSpecification == null)
                return "Chưa cấu hình giá";
            if (IsAlbum)
            {
                if (!PrintCount.HasValue || PrintCount.Value == 0)
                    return "Album không có tệp in!";
                if (SheetCount < IncludedSheets)
                    return $"Album {SheetCount}/{IncludedSheets} tờ (chưa đủ tờ chuẩn, tính giá gói)";
                if (ExtraSheets > 0)
                    return $"Album {SheetCount} tờ ({ExtraSheets} tờ thêm: +{(long)ExtraSheets * ExtraSheetPrice:N0} đ)";
                return $"Album {SheetCount} tờ chuẩn ({BasePrice:N0} đ)";
            }
            return $"In {PrintCount ?? 0} ảnh";
        }
    }

    public string StatusBadgeColor => StatusBadgeFg;

    public string StatusBadgeBg
    {
        get
        {
            if (PrintFolderStatus == PrintFolderResolutionStatus.NoPrintFolder ||
                PrintFolderStatus == PrintFolderResolutionStatus.AmbiguousPrintFolder ||
                Item.PrintSpecification == null)
                return "#FFF0C9"; // Amber bg
            if (IsAlbum && (!PrintCount.HasValue || PrintCount.Value == 0))
                return "#F7DFDC"; // Danger bg
            if (IsAlbum && SheetCount < IncludedSheets)
                return "#CAEBFF"; // Info bg
            return "#DFF2E7"; // Success bg
        }
    }

    public string StatusBadgeFg
    {
        get
        {
            if (PrintFolderStatus == PrintFolderResolutionStatus.NoPrintFolder ||
                PrintFolderStatus == PrintFolderResolutionStatus.AmbiguousPrintFolder ||
                Item.PrintSpecification == null)
                return "#8A5700"; // Amber fg
            if (IsAlbum && (!PrintCount.HasValue || PrintCount.Value == 0))
                return "#D9002B"; // Danger fg
            if (IsAlbum && SheetCount < IncludedSheets)
                return "#15295A"; // Info fg
            return "#124548"; // Success fg
        }
    }

    public string StatusBadgeBorder
    {
        get
        {
            if (PrintFolderStatus == PrintFolderResolutionStatus.NoPrintFolder ||
                PrintFolderStatus == PrintFolderResolutionStatus.AmbiguousPrintFolder ||
                Item.PrintSpecification == null)
                return "#E9C66E"; // Amber border
            if (IsAlbum && (!PrintCount.HasValue || PrintCount.Value == 0))
                return "#E8A4A1"; // Danger border
            if (IsAlbum && SheetCount < IncludedSheets)
                return "#A6DCFF"; // Info border
            return "#B8E4CC"; // Success border
        }
    }

    public OrderItemDisplayModel(OrderItemScan item, OrderDisplayModel parentOrder)
    {
        Item = item;
        ParentOrder = parentOrder;
        _printCount = item.PrintCount;
        _mismatchCount = item.MismatchCount;
        _printFolderStatus = item.PrintFolderStatus;
        _selectedPrintFolderRelativePath = item.SelectedPrintFolderRelativePath;
        _folderResolutionMode = item.FolderResolutionMode;
        _billQuantity = item.BillQuantity;
        _resolutionMode = item.QuantityResolutionMode;
        _resolutionNote = item.QuantityResolutionNote;
        _isPrinted = item.IsPrinted;
        _printedAt = item.PrintedAt;
    }

    public void UpdateResolution(int quantity, QuantityResolutionMode mode, string? note)
    {
        BillQuantity = quantity;
        ResolutionMode = mode;
        ResolutionNote = note;
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(FormattedLineTotal));
        OnPropertyChanged(nameof(FormattedQuantityText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeColor));
        ParentOrder.NotifyTotalsChanged();
    }

    public void UpdatePrintFolder(string selectedPath, int count)
    {
        SelectedPrintFolderRelativePath = selectedPath;
        PrintCount = count;
        PrintFolderStatus = PrintFolderResolutionStatus.Resolved;
        FolderResolutionMode = BillingFolderResolutionMode.ManuallySelected;
        MismatchCount = 0;
        if (IsAlbum)
        {
            BillQuantity = 1;
            ResolutionMode = QuantityResolutionMode.UsePrint;
        }
        else
        {
            BillQuantity = count;
            ResolutionMode = QuantityResolutionMode.UsePrint;
        }
        OnPropertyChanged(nameof(PrintableFileCount));
        OnPropertyChanged(nameof(SheetCount));
        OnPropertyChanged(nameof(ExtraSheets));
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(FormattedLineTotal));
        OnPropertyChanged(nameof(FormattedQuantityText));
        OnPropertyChanged(nameof(IsAmbiguous));
        OnPropertyChanged(nameof(MismatchText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeColor));
        OnPropertyChanged(nameof(StatusBadgeBg));
        OnPropertyChanged(nameof(StatusBadgeFg));
        OnPropertyChanged(nameof(StatusBadgeBorder));
        OnPropertyChanged(nameof(EffectiveBillingFolderPath));
        OnPropertyChanged(nameof(FolderResolutionModeText));
        OnPropertyChanged(nameof(EffectiveBillingFolderTooltip));
        ParentOrder.NotifyTotalsChanged();
    }
}
