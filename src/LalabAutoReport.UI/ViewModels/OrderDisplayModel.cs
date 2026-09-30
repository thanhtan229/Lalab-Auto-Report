using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LalabAutoReport.Core.Domain;

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

    public bool IsGuest => !HasCustomer && (Order.IsGuest || Order.IsGuestFolderName(OriginalFolderName, Order.OrderName));

    public string CustomerBadgeText
    {
        get
        {
            if (HasCustomer)
            {
                return $"👤 {CanonicalCustomerName}";
            }
            if (IsGuest)
            {
                return "⚡ Khách lẻ";
            }
            return "⚠️ Chưa gán khách";
        }
    }

    public string CustomerBadgeBg
    {
        get
        {
            if (HasCustomer) return "#E0F2FE";
            if (IsGuest) return "#FEF3C7";
            return "#FEE2E2";
        }
    }

    public string CustomerBadgeBorder
    {
        get
        {
            if (HasCustomer) return "#7DD3FC";
            if (IsGuest) return "#FCD34D";
            return "#FCA5A5";
        }
    }

    public string CustomerBadgeFg
    {
        get
        {
            if (HasCustomer) return "#0369A1";
            if (IsGuest) return "#B45309";
            return "#B91C1C";
        }
    }

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

            if (IsGuest)
            {
                return $"Thư mục '{OriginalFolderName}' được đánh dấu là Khách lẻ trực tiếp.\n(Nhấn vào đây để gán vào khách quen)";
            }

            return $"Thư mục '{OriginalFolderName}' chưa được gán vào khách hàng nào trong hệ thống.\n(Nhấn vào đây để chọn khách hàng)";
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

    partial void OnIsPrintedChanged(bool value)
    {
        Order.IsPrinted = value;
        OnPropertyChanged(nameof(PrintedStatusText));
        OnPropertyChanged(nameof(PrintedStatusBadgeBg));
        OnPropertyChanged(nameof(PrintedStatusBadgeBorder));
        OnPropertyChanged(nameof(PrintedStatusBadgeFg));
    }

    public string PrintedStatusText => IsPrinted ? "🏷️ ĐÃ IN" : "Chưa in";
    public string PrintedStatusBadgeBg => IsPrinted ? "#FEE2E2" : "#F3F4F6";
    public string PrintedStatusBadgeBorder => IsPrinted ? "#F87171" : "#E5E7EB";
    public string PrintedStatusBadgeFg => IsPrinted ? "#DC2626" : "#6B7280";

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
