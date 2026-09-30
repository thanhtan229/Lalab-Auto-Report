using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;

namespace LalabAutoReport.UI.ViewModels;

public partial class PriceListViewModel : ObservableObject
{
    private readonly IPrintSpecificationRepository _specificationRepository;

    [ObservableProperty]
    private ObservableCollection<PrintSpecification> _specifications = new();

    [ObservableProperty]
    private PrintSpecification? _selectedSpec;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreatingAlbum))]
    private ProductCategory _newCategory = ProductCategory.PhotoPrint;

    public bool IsCreatingAlbum => NewCategory == ProductCategory.Album;

    public IReadOnlyList<ProductCategory> AvailableCategories { get; } = new[]
    {
        ProductCategory.PhotoPrint,
        ProductCategory.Album
    };

    [ObservableProperty]
    private string _newSpecName = string.Empty;

    [ObservableProperty]
    private long _newSpecPrice = 5000;

    [ObservableProperty]
    private int _newIncludedSheets = 10;

    [ObservableProperty]
    private long _newBasePrice = 400000;

    [ObservableProperty]
    private long _newExtraSheetPrice = 20000;

    [ObservableProperty]
    private string _newSpecAlias = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private long _editUnitPrice;

    [ObservableProperty]
    private long _editBasePrice;

    [ObservableProperty]
    private int _editIncludedSheets;

    [ObservableProperty]
    private long _editExtraSheetPrice;

    partial void OnSelectedSpecChanged(PrintSpecification? value)
    {
        if (value != null)
        {
            EditUnitPrice = value.UnitPrice;
            EditBasePrice = value.BasePrice ?? 400000;
            EditIncludedSheets = value.IncludedSheets ?? 10;
            EditExtraSheetPrice = value.ExtraSheetPrice ?? 20000;
        }
    }

    private readonly IProductRepository _productRepository;

    [ObservableProperty]
    private ObservableCollection<ProductFamily> _families = new();

    [ObservableProperty]
    private ProductFamily? _selectedFamily;

    [ObservableProperty]
    private string _newFamilyAlias = string.Empty;

    public PriceListViewModel(IProductRepository productRepository)
    {
        _productRepository = productRepository;
        _specificationRepository = productRepository;
    }

    public async Task LoadSpecificationsAsync()
    {
        var specs = await _specificationRepository.GetAllAsync(includeInactive: false);
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

        var fams = await _productRepository.GetAllFamiliesAsync();
        Families.Clear();
        foreach (var f in fams)
        {
            Families.Add(f);
        }
        if (SelectedFamily != null)
        {
            SelectedFamily = Families.FirstOrDefault(f => f.Id == SelectedFamily.Id) ?? Families.FirstOrDefault();
        }
        else
        {
            SelectedFamily = Families.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task AddFamilyAliasAsync()
    {
        if (SelectedFamily == null)
        {
            StatusMessage = "Vui lòng chọn dòng sản phẩm (Ảnh in hoặc Album)!";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewFamilyAlias))
        {
            StatusMessage = "Vui lòng nhập alias cho dòng sản phẩm!";
            return;
        }

        try
        {
            await _productRepository.AddFamilyAliasAsync(SelectedFamily.Id, NewFamilyAlias.Trim());
            StatusMessage = $"Đã thêm family alias '{NewFamilyAlias.Trim()}' cho dòng {SelectedFamily.Name}";
            NewFamilyAlias = string.Empty;
            await LoadSpecificationsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi thêm family alias: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RemoveFamilyAliasAsync(ProductFamilyAlias? alias)
    {
        if (alias == null) return;
        try
        {
            await _productRepository.RemoveFamilyAliasAsync(alias.Id);
            StatusMessage = $"Đã xóa family alias '{alias.AliasText}'";
            await LoadSpecificationsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xóa family alias: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateSpecificationAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSpecName))
        {
            StatusMessage = "Vui lòng nhập tên sản phẩm/quy cách!";
            return;
        }

        string rawName = NewSpecName.Trim();
        string canonicalSize = SizeNormalizer.NormalizeSize(rawName);

        PrintSpecification spec;
        if (NewCategory == ProductCategory.Album)
        {
            spec = new PrintSpecification
            {
                CanonicalName = rawName,
                CanonicalSize = canonicalSize,
                Category = ProductCategory.Album,
                BillingMethod = BillingMethod.AlbumBasePlusExtra,
                IncludedSheets = Math.Max(1, NewIncludedSheets),
                BasePrice = Math.Max(0, NewBasePrice),
                ExtraSheetPrice = Math.Max(0, NewExtraSheetPrice),
                UnitPrice = 0,
                IsActive = true
            };
        }
        else
        {
            spec = new PrintSpecification
            {
                CanonicalName = rawName,
                CanonicalSize = canonicalSize,
                Category = ProductCategory.PhotoPrint,
                BillingMethod = BillingMethod.FileCount,
                UnitPrice = Math.Max(0, NewSpecPrice),
                IsActive = true
            };
        }

        try
        {
            var created = await _productRepository.CreateSpecificationAsync(spec);
            NewSpecName = string.Empty;
            StatusMessage = created.Category == ProductCategory.Album
                ? $"Đã thêm album: {created.CanonicalName} (Khổ chuẩn: {created.CanonicalSize}) - {created.BasePrice:N0} đ / {created.IncludedSheets} tờ"
                : $"Đã thêm quy cách: {created.CanonicalName} (Khổ chuẩn: {created.CanonicalSize}) - {created.UnitPrice:N0} đ";
            await LoadSpecificationsAsync();
            SelectedSpec = Specifications.FirstOrDefault(s => s.Id == created.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi: {ex.Message}";
        }
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

    [RelayCommand]
    public async Task UpdateAliasAsync(object? param)
    {
        if (param is (PrintSpecificationAlias alias, string newAliasText))
        {
            await UpdateSpecAliasAsync(alias, newAliasText);
        }
    }

    public async Task<bool> UpdateSpecAliasAsync(PrintSpecificationAlias alias, string newAliasText)
    {
        if (alias == null || SelectedSpec == null)
        {
            StatusMessage = "Vui lòng chọn quy cách và alias!";
            return false;
        }

        if (string.IsNullOrWhiteSpace(newAliasText))
        {
            StatusMessage = "Vui lòng nhập tên alias!";
            return false;
        }

        string trimmed = newAliasText.Trim();
        if (string.Equals(alias.AliasText, trimmed, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            await _specificationRepository.UpdateAliasAsync(alias.Id, trimmed);
            StatusMessage = $"Đã cập nhật alias '{alias.AliasText}' thành '{trimmed}'";
            await LoadSpecificationsAsync();
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi cập nhật alias: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    public async Task RemoveAliasAsync(PrintSpecificationAlias? alias)
    {
        if (alias == null || SelectedSpec == null) return;

        try
        {
            await _specificationRepository.RemoveAliasAsync(alias.Id);
            StatusMessage = $"Đã xóa alias '{alias.AliasText}'";
            await LoadSpecificationsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xóa alias: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSelectedSpecAsync()
    {
        if (SelectedSpec == null) return;
        SelectedSpec.UnitPrice = Math.Max(0, EditUnitPrice);
        SelectedSpec.BasePrice = Math.Max(0, EditBasePrice);
        SelectedSpec.IncludedSheets = Math.Max(1, EditIncludedSheets);
        SelectedSpec.ExtraSheetPrice = Math.Max(0, EditExtraSheetPrice);
        await _specificationRepository.UpdateSpecificationAsync(SelectedSpec);
        StatusMessage = $"Đã cập nhật thông tin cho '{SelectedSpec.CanonicalName}'";
        await LoadSpecificationsAsync();
    }

    public async Task<bool> RenameSpecificationAsync(PrintSpecification spec, string newName)
    {
        if (spec == null)
        {
            StatusMessage = "Vui lòng chọn quy cách!";
            return false;
        }

        string trimmed = newName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            StatusMessage = "Tên sản phẩm không được để trống!";
            return false;
        }

        if (string.Equals(spec.CanonicalName, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string oldName = spec.CanonicalName;
        string newCanonicalSize = SizeNormalizer.NormalizeSize(trimmed);

        try
        {
            spec.CanonicalName = trimmed;
            spec.CanonicalSize = newCanonicalSize;
            await _specificationRepository.UpdateSpecificationAsync(spec);

            // Tự động lưu tên cũ làm alias nếu chưa có trong danh sách
            if (!string.IsNullOrWhiteSpace(oldName) && !spec.Aliases.Any(a => string.Equals(a.AliasText, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                await _specificationRepository.AddAliasAsync(spec.Id, oldName);
            }

            StatusMessage = $"Đã đổi tên sản phẩm thành '{trimmed}' (Khổ: {newCanonicalSize}) và lưu alias '{oldName}'";
            long specId = spec.Id;
            await LoadSpecificationsAsync();
            SelectedSpec = Specifications.FirstOrDefault(s => s.Id == specId);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi đổi tên sản phẩm: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    public async Task<bool> DeleteSelectedSpecAsync()
    {
        if (SelectedSpec == null)
        {
            StatusMessage = "Vui lòng chọn quy cách cần xóa!";
            return false;
        }

        string name = SelectedSpec.CanonicalName;
        long id = SelectedSpec.Id;

        try
        {
            await _specificationRepository.DeleteSpecificationAsync(id);
            StatusMessage = $"Đã xóa quy cách '{name}'";
            SelectedSpec = null;
            await LoadSpecificationsAsync();
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xóa quy cách: {ex.Message}";
            return false;
        }
    }
}
