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
    public string OriginalFolderName => Order.OriginalFolderName;
    public string RelativePath => Order.RelativePath;
    public string WorkDate => Order.WorkDate;

    [ObservableProperty]
    private OrderStatus _status;

    [ObservableProperty]
    private ObservableCollection<OrderItemDisplayModel> _items = new();

    public int TotalSourceCount => Items.Sum(i => i.SourceCount);
    public int TotalPrintCount => Items.Sum(i => i.PrintCount ?? 0);
    public int TotalBillQuantity => Items.Sum(i => i.BillQuantity ?? 0);

    public bool HasIssues => Status == OrderStatus.NeedsReview || Status == OrderStatus.Error;

    public string StatusText => Status switch
    {
        OrderStatus.Unscanned => "Chưa quét",
        OrderStatus.Scanning => "Đang quét...",
        OrderStatus.Scanned => "Đã quét",
        OrderStatus.NeedsReview => "Cần xử lý",
        OrderStatus.Ready => "Sẵn sàng",
        OrderStatus.Billed => "Đã tạo bill",
        OrderStatus.Locked => "Đã khóa",
        OrderStatus.Error => "Lỗi",
        _ => Status.ToString()
    };

    public string StatusBadgeColor => Status switch
    {
        OrderStatus.Ready => "#2E7D32",        // Green
        OrderStatus.NeedsReview => "#ED6C02",  // Amber / Warning
        OrderStatus.Locked => "#1565C0",       // Blue
        OrderStatus.Error => "#D32F2F",        // Red
        _ => "#757575"                         // Gray
    };

    public OrderDisplayModel(Order order)
    {
        Order = order;
        _status = order.Status;
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
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeColor));
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

    public bool IsAmbiguous => PrintFolderStatus == PrintFolderResolutionStatus.AmbiguousPrintFolder;
    public bool HasMismatch => MismatchCount.HasValue && MismatchCount.Value != 0;
    public IReadOnlyList<string> CandidateFolders => Item.CandidatePrintFolderRelativePaths;

    public string MismatchText
    {
        get
        {
            if (PrintFolderStatus == PrintFolderResolutionStatus.NoPrintFolder)
                return "Không có thư mục in";
            if (PrintFolderStatus == PrintFolderResolutionStatus.AmbiguousPrintFolder)
                return "Trùng/Đa nhánh in";
            if (!MismatchCount.HasValue || MismatchCount.Value == 0)
                return "Khớp";
            return MismatchCount > 0 ? $"+{MismatchCount}" : $"{MismatchCount}";
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
            if (HasMismatch)
            {
                if (BillQuantity.HasValue)
                    return $"Đã chọn SL: {BillQuantity} ({ResolutionMode})";
                return "Lệch số lượng!";
            }
            return "Khớp";
        }
    }

    public string StatusBadgeColor
    {
        get
        {
            if (PrintFolderStatus == PrintFolderResolutionStatus.NoPrintFolder ||
                PrintFolderStatus == PrintFolderResolutionStatus.AmbiguousPrintFolder)
                return "#ED6C02"; // Orange
            if (HasMismatch && !BillQuantity.HasValue)
                return "#D32F2F"; // Red
            if (HasMismatch && BillQuantity.HasValue)
                return "#0288D1"; // Blue resolved
            return "#2E7D32"; // Green
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
        _billQuantity = item.BillQuantity;
        _resolutionMode = item.QuantityResolutionMode;
        _resolutionNote = item.QuantityResolutionNote;
    }

    public void UpdateResolution(int quantity, QuantityResolutionMode mode, string? note)
    {
        BillQuantity = quantity;
        ResolutionMode = mode;
        ResolutionNote = note;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeColor));
        ParentOrder.NotifyTotalsChanged();
    }

    public void UpdatePrintFolder(string selectedPath, int count)
    {
        SelectedPrintFolderRelativePath = selectedPath;
        PrintCount = count;
        PrintFolderStatus = PrintFolderResolutionStatus.Resolved;
        MismatchCount = count - SourceCount;
        if (count == SourceCount)
        {
            BillQuantity = count;
            ResolutionMode = QuantityResolutionMode.AutoMatch;
        }
        else
        {
            BillQuantity = null;
            ResolutionMode = null;
        }
        OnPropertyChanged(nameof(IsAmbiguous));
        OnPropertyChanged(nameof(MismatchText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeColor));
        ParentOrder.NotifyTotalsChanged();
    }
}
