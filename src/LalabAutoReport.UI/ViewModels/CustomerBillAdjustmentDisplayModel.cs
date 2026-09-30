using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.UI.ViewModels;

public partial class CustomerBillAdjustmentDisplayModel : ObservableObject
{
    public BillAdjustment DomainAdjustment { get; }
    private readonly Action? _onDataChanged;

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private AdjustmentDirection _direction;

    [ObservableProperty]
    private long _amount;

    public AdjustmentType Type => DomainAdjustment.Type;
    public bool IsRemovable => DomainAdjustment.Type == AdjustmentType.Custom;
    public bool IsAdd => Direction == AdjustmentDirection.Add;
    public string DirectionSign => Direction == AdjustmentDirection.Deduct ? "-" : "+";

    public CustomerBillAdjustmentDisplayModel(BillAdjustment domainAdjustment, Action? onDataChanged = null)
    {
        DomainAdjustment = domainAdjustment;
        _onDataChanged = onDataChanged;
        _label = domainAdjustment.Label;
        _direction = domainAdjustment.Direction;
        _amount = domainAdjustment.Amount;
    }

    partial void OnLabelChanged(string value)
    {
        DomainAdjustment.Label = value;
        _onDataChanged?.Invoke();
    }

    partial void OnDirectionChanged(AdjustmentDirection value)
    {
        DomainAdjustment.Direction = value;
        OnPropertyChanged(nameof(IsAdd));
        OnPropertyChanged(nameof(DirectionSign));
        _onDataChanged?.Invoke();
    }

    partial void OnAmountChanged(long value)
    {
        if (value < 0) value = 0;
        DomainAdjustment.Amount = value;
        _onDataChanged?.Invoke();
    }
}
