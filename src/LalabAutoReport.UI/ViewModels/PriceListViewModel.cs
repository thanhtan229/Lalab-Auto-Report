using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.UI.ViewModels;

public partial class PriceListViewModel : ObservableObject
{
    private readonly IPrintSpecificationRepository _specificationRepository;

    [ObservableProperty]
    private ObservableCollection<PrintSpecification> _specifications = new();

    [ObservableProperty]
    private PrintSpecification? _selectedSpec;

    [ObservableProperty]
    private string _newSpecName = string.Empty;

    [ObservableProperty]
    private long _newSpecPrice = 5000;

    [ObservableProperty]
    private string _newSpecAlias = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public PriceListViewModel(IPrintSpecificationRepository specificationRepository)
    {
        _specificationRepository = specificationRepository;
    }

    public async Task LoadSpecificationsAsync()
    {
        var specs = await _specificationRepository.GetAllAsync(includeInactive: true);
        Specifications.Clear();
        foreach (var s in specs)
        {
            Specifications.Add(s);
        }
        if (SelectedSpec != null)
        {
            SelectedSpec = Specifications.FirstOrDefault(s => s.Id == SelectedSpec.Id) ?? Specifications.FirstOrDefault();
        }
        else
        {
            SelectedSpec = Specifications.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task CreateSpecificationAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSpecName))
        {
            StatusMessage = "Vui lòng nhập tên quy cách in!";
            return;
        }

        var spec = new PrintSpecification
        {
            CanonicalName = NewSpecName.Trim(),
            UnitPrice = Math.Max(0, NewSpecPrice),
            IsActive = true
        };

        var created = await _specificationRepository.CreateSpecificationAsync(spec);
        NewSpecName = string.Empty;
        StatusMessage = $"Đã thêm quy cách: {created.CanonicalName} - {created.UnitPrice:N0} đ";
        await LoadSpecificationsAsync();
        SelectedSpec = Specifications.FirstOrDefault(s => s.Id == created.Id);
    }

    [RelayCommand]
    private async Task UpdatePriceAsync(object? param)
    {
        if (param is (PrintSpecification spec, long newPrice))
        {
            spec.UnitPrice = Math.Max(0, newPrice);
            await _specificationRepository.UpdateSpecificationAsync(spec);
            StatusMessage = $"Đã cập nhật giá {spec.CanonicalName} thành {spec.UnitPrice:N0} đ";
            await LoadSpecificationsAsync();
        }
    }

    [RelayCommand]
    private async Task AddAliasAsync()
    {
        if (SelectedSpec == null)
        {
            StatusMessage = "Vui lòng chọn quy cách!";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewSpecAlias))
        {
            StatusMessage = "Vui lòng nhập alias quy cách!";
            return;
        }

        await _specificationRepository.AddAliasAsync(SelectedSpec.Id, NewSpecAlias.Trim());
        StatusMessage = $"Đã thêm alias '{NewSpecAlias.Trim()}' cho {SelectedSpec.CanonicalName}";
        NewSpecAlias = string.Empty;
        await LoadSpecificationsAsync();
    }
}
