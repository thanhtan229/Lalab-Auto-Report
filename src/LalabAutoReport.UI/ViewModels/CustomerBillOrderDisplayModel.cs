using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.UI.ViewModels;

public partial class CustomerBillOrderDisplayModel : ObservableObject
{
    public CustomerBillOrder DomainOrder { get; }
    private readonly Action? _onDataChanged;

    [ObservableProperty]
    private bool _isIncluded;

    [ObservableProperty]
    private long _subtotal;

    [ObservableProperty]
    private string _orderName = string.Empty;

    public string? OrderCode => DomainOrder.OrderCodeSnapshot;
    public string DisplayOrderCode => !string.IsNullOrWhiteSpace(OrderCode) ? OrderCode : $"#{DomainOrder.OrderId}";
    public string OrderDate => DomainOrder.OrderDateSnapshot;
    public string? SourceFolderPath => DomainOrder.SourceFolderPath;
    public bool HasSourceFolder => !string.IsNullOrWhiteSpace(SourceFolderPath);
    public bool IsFromPreviousPeriod => DomainOrder.IsFromPreviousPeriod;
    public ObservableCollection<CustomerBillLineDisplayModel> Lines { get; } = new();

    public CustomerBillOrderDisplayModel(CustomerBillOrder domainOrder, Action? onDataChanged = null)
    {
        DomainOrder = domainOrder;
        _onDataChanged = onDataChanged;
        _isIncluded = domainOrder.IsIncluded;
        _subtotal = domainOrder.Subtotal;
        _orderName = domainOrder.OrderNameSnapshot;

        foreach (var l in domainOrder.Lines)
        {
            Lines.Add(new CustomerBillLineDisplayModel(l, OnChildLineChanged));
        }

        RecalculateSubtotal();
    }

    partial void OnOrderNameChanged(string value)
    {
        DomainOrder.OrderNameSnapshot = value;
        _onDataChanged?.Invoke();
    }

    partial void OnIsIncludedChanged(bool value)
    {
        DomainOrder.IsIncluded = value;
        foreach (var line in Lines)
        {
            line.IsIncluded = value;
        }
        RecalculateSubtotal();
    }

    private void OnChildLineChanged()
    {
        RecalculateSubtotal();
    }

    public void RecalculateSubtotal()
    {
        Subtotal = Lines.Where(l => l.IsIncluded).Sum(l => l.LineTotal);
        DomainOrder.Subtotal = Subtotal;
        _onDataChanged?.Invoke();
    }
}
