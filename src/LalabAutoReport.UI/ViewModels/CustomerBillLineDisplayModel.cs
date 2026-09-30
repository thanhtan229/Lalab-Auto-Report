using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.UI.ViewModels;

public partial class CustomerBillLineDisplayModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = new("vi-VN");
    public CustomerBillLine DomainLine { get; }
    private readonly Action? _onDataChanged;

    [ObservableProperty]
    private bool _isIncluded;

    [ObservableProperty]
    private int _billedQuantity;

    [ObservableProperty]
    private long _billedUnitPrice;

    [ObservableProperty]
    private long _lineTotal;

    [ObservableProperty]
    private string? _note;

    public int ScannedQuantity => DomainLine.ScannedQuantity;
    public long ConfiguredUnitPrice => DomainLine.ConfiguredUnitPrice;
    public string ProductName => DomainLine.ProductNameSnapshot;
    public string? Variant => DomainLine.VariantSnapshot;
    public bool IsAlbum => DomainLine.BillingMethodSnapshot == BillingMethod.AlbumBasePlusExtra;
    public string? IssueMessage => DomainLine.IssueMessage;
    public bool HasIssue => !string.IsNullOrEmpty(IssueMessage);
    public string SpecificationFolderName => DomainLine.SpecificationFolderName ?? string.Empty;

    public string FolderBadgeText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SpecificationFolderName))
                return string.Empty;

            if (string.Equals(SpecificationFolderName.Trim(), ProductName.Trim(), StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return $"[Thư mục: {SpecificationFolderName}]";
        }
    }

    public string AlbumDetails
    {
        get
        {
            if (!IsAlbum) return string.Empty;
            int sheetCount = DomainLine.SheetCount ?? DomainLine.ScannedQuantity;
            int inc = DomainLine.IncludedSheetsSnapshot ?? 10;
            int extra = DomainLine.ExtraSheetCount ?? Math.Max(0, sheetCount - inc);
            long extraP = DomainLine.ExtraSheetPriceSnapshot ?? 0;
            if (extra > 0)
            {
                return $"{sheetCount} tờ (chuẩn {inc} tờ + {extra} tờ thêm × {extraP:#,##0} đ)";
            }
            if (sheetCount < inc)
            {
                return $"{sheetCount} tờ";
            }
            return $"{sheetCount} tờ (chuẩn {inc} tờ)";
        }
    }

    public CustomerBillLineDisplayModel(CustomerBillLine domainLine, Action? onDataChanged = null)
    {
        DomainLine = domainLine;
        _onDataChanged = onDataChanged;
        _isIncluded = domainLine.IsIncluded;
        _billedQuantity = domainLine.BilledQuantity;
        _billedUnitPrice = domainLine.BilledUnitPrice;
        _lineTotal = domainLine.LineTotal;
        _note = domainLine.Note;
    }

    partial void OnNoteChanged(string? value)
    {
        DomainLine.Note = value;
        _onDataChanged?.Invoke();
    }

    partial void OnIsIncludedChanged(bool value)
    {
        DomainLine.IsIncluded = value;
        _onDataChanged?.Invoke();
    }

    partial void OnBilledQuantityChanged(int value)
    {
        if (value < 0) value = 0;
        DomainLine.BilledQuantity = value;
        if (value != DomainLine.ScannedQuantity)
        {
            DomainLine.QuantityOverrideReason = "Chỉnh sửa thủ công";
        }
        RecalculateLineTotal();
    }

    partial void OnBilledUnitPriceChanged(long value)
    {
        if (value < 0) value = 0;
        DomainLine.BilledUnitPrice = value;
        if (value != DomainLine.ConfiguredUnitPrice)
        {
            DomainLine.PriceOverrideReason = "Chỉnh sửa thủ công";
        }
        RecalculateLineTotal();
    }

    private void RecalculateLineTotal()
    {
        if (IsAlbum)
        {
            LineTotal = BilledUnitPrice;
        }
        else
        {
            LineTotal = BilledQuantity * BilledUnitPrice;
        }

        DomainLine.LineTotal = LineTotal;
        _onDataChanged?.Invoke();
    }
}
