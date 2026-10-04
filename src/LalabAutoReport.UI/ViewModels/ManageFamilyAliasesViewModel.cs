using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;

namespace LalabAutoReport.UI.ViewModels;

public partial class FamilyCardViewModel : ObservableObject
{
    public long FamilyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ProductCategory Category { get; set; }
    public string DisplayIcon { get; set; } = "📁";
    public string Description { get; set; } = string.Empty;
    public bool CanDelete { get; set; } = true;
    public bool CanRename { get; set; } = true;

    [ObservableProperty]
    private ObservableCollection<ProductFamilyAlias> _aliases = new();

    [ObservableProperty]
    private string _newAliasInput = string.Empty;
}

public partial class ManageFamilyAliasesViewModel : ObservableObject
{
    private readonly IProductRepository _productRepository;

    [ObservableProperty]
    private ObservableCollection<FamilyCardViewModel> _familyCards = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSuccessStatus = true;

    [ObservableProperty]
    private bool _isBusy;

    public ManageFamilyAliasesViewModel(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task InitializeAsync()
    {
        await LoadFamiliesAsync();
    }

    public async Task LoadFamiliesAsync()
    {
        IsBusy = true;
        try
        {
            var families = await _productRepository.GetAllFamiliesAsync();
            FamilyCards.Clear();

            foreach (var fam in families)
            {
                string icon = "📁";
                string desc = "Từ khóa dùng chung cho mọi quy cách thuộc dòng này.";

                if (fam.Category == ProductCategory.Album || fam.Name.Contains("Album", StringComparison.OrdinalIgnoreCase))
                {
                    icon = "📖";
                    desc = "Dùng chung cho tất cả các size Album (ví dụ: 'ab', 'alb', 'cuon').";
                }
                else if (fam.Name.Contains("Mica", StringComparison.OrdinalIgnoreCase))
                {
                    icon = "🎨";
                    desc = "Dùng chung cho tất cả tranh mica (ví dụ: 'mica', 'tranh mica').";
                }
                else if (fam.Name.Contains("Plastic", StringComparison.OrdinalIgnoreCase) || fam.Name.Contains("ép", StringComparison.OrdinalIgnoreCase))
                {
                    icon = "📑";
                    desc = "Dùng chung cho ảnh in ép plastic (ví dụ: 'ep', 'plastic').";
                }
                else if (fam.Name.Contains("Da", StringComparison.OrdinalIgnoreCase))
                {
                    icon = "👜";
                    desc = "Dùng chung cho bao da (ví dụ: 'baoda', 'da').";
                }
                else if (fam.Category == ProductCategory.PhotoPrint)
                {
                    icon = "🖼️";
                    desc = "Dùng chung cho tất cả các size Ảnh In (ví dụ: 'in', 'cp', 'anh', 'photo').";
                }

                bool isCoreSystemFamily = string.Equals(fam.Name, "Ảnh in", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(fam.Name, "Album", StringComparison.OrdinalIgnoreCase);

                var card = new FamilyCardViewModel
                {
                    FamilyId = fam.Id,
                    Name = fam.Name,
                    Category = fam.Category,
                    DisplayIcon = icon,
                    Description = desc,
                    CanDelete = !isCoreSystemFamily,
                    CanRename = !isCoreSystemFamily,
                    Aliases = new ObservableCollection<ProductFamilyAlias>(fam.Aliases ?? new())
                };

                FamilyCards.Add(card);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi khi tải danh sách dòng sản phẩm: {ex.Message}", false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddAliasAsync(FamilyCardViewModel card)
    {
        if (card == null) return;

        string raw = card.NewAliasInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            SetStatus("Vui lòng nhập từ khóa alias!", false);
            return;
        }

        string norm = CustomerNormalizer.Normalize(raw);

        // 1. Kiểm tra xem alias này đã tồn tại trong chính dòng này chưa
        if (card.Aliases.Any(a => string.Equals(a.NormalizedAlias, norm, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(CustomerNormalizer.Normalize(a.AliasText), norm, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"Từ khóa '{raw}' đã có trong dòng {card.Name}!", false);
            return;
        }

        // 2. Kiểm tra xem alias này có đang thuộc dòng sản phẩm khác không
        var otherFamily = FamilyCards.FirstOrDefault(c => c.FamilyId != card.FamilyId &&
            c.Aliases.Any(a => string.Equals(a.NormalizedAlias, norm, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(CustomerNormalizer.Normalize(a.AliasText), norm, StringComparison.OrdinalIgnoreCase)));
        if (otherFamily != null)
        {
            SetStatus($"Từ khóa '{raw}' đang được dùng cho dòng '{otherFamily.Name}'. Vui lòng xóa ở dòng kia trước nếu muốn chuyển!", false);
            return;
        }

        try
        {
            await _productRepository.AddFamilyAliasAsync(card.FamilyId, raw);
            card.NewAliasInput = string.Empty;
            await LoadFamiliesAsync();
            SetStatus($"Đã thêm alias '{raw}' cho dòng {card.Name} thành công!", true);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi khi lưu alias: {ex.Message}", false);
        }
    }

    public async Task<bool> UpdateAliasAsync(ProductFamilyAlias alias, string newAliasText)
    {
        if (alias == null) return false;

        string trimmed = newAliasText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SetStatus("Vui lòng nhập từ khóa alias!", false);
            return false;
        }

        if (string.Equals(alias.AliasText, trimmed, StringComparison.Ordinal))
        {
            return true;
        }

        string norm = CustomerNormalizer.Normalize(trimmed);

        // Kiểm tra xem alias mới có bị trùng ở bất kỳ dòng nào không
        var conflictingCard = FamilyCards.FirstOrDefault(c =>
            c.Aliases.Any(a => a.Id != alias.Id &&
                (string.Equals(a.NormalizedAlias, norm, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(CustomerNormalizer.Normalize(a.AliasText), norm, StringComparison.OrdinalIgnoreCase))));

        if (conflictingCard != null)
        {
            SetStatus($"Từ khóa '{trimmed}' đã tồn tại trong dòng '{conflictingCard.Name}'!", false);
            return false;
        }

        try
        {
            await _productRepository.UpdateFamilyAliasAsync(alias.Id, trimmed);
            await LoadFamiliesAsync();
            SetStatus($"Đã cập nhật từ khóa thành '{trimmed}' thành công!", true);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi khi cập nhật alias: {ex.Message}", false);
            return false;
        }
    }

    public async Task RemoveAliasAsync(ProductFamilyAlias alias)
    {
        if (alias == null) return;

        try
        {
            await _productRepository.RemoveFamilyAliasAsync(alias.Id);
            await LoadFamiliesAsync();
            SetStatus($"Đã xóa alias '{alias.AliasText}' khỏi dòng sản phẩm.", true);
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi khi xóa alias: {ex.Message}", false);
        }
    }

    public async Task<bool> CreateFamilyAsync(string name, ProductCategory category, BillingMethod billingMethod, IEnumerable<string>? initialAliases)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SetStatus("Vui lòng nhập tên dòng sản phẩm!", false);
            return false;
        }

        if (FamilyCards.Any(f => string.Equals(f.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"Dòng sản phẩm '{trimmed}' đã tồn tại!", false);
            return false;
        }

        try
        {
            var family = new ProductFamily
            {
                Name = trimmed,
                Category = category,
                BillingMethod = billingMethod
            };

            await _productRepository.CreateFamilyAsync(family, initialAliases);
            await LoadFamiliesAsync();
            SetStatus($"Đã tạo dòng sản phẩm '{trimmed}' thành công!", true);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi khi tạo dòng sản phẩm: {ex.Message}", false);
            return false;
        }
    }

    public async Task<bool> RenameFamilyAsync(FamilyCardViewModel card, string newName)
    {
        if (card == null) return false;

        string trimmed = newName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SetStatus("Vui lòng nhập tên mới cho dòng sản phẩm!", false);
            return false;
        }

        if (string.Equals(card.Name, trimmed, StringComparison.Ordinal))
        {
            return true;
        }

        if (FamilyCards.Any(f => f.FamilyId != card.FamilyId && string.Equals(f.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"Tên dòng '{trimmed}' đã bị trùng với một dòng khác!", false);
            return false;
        }

        try
        {
            var fam = await _productRepository.GetFamilyByIdAsync(card.FamilyId);
            if (fam != null)
            {
                fam.Name = trimmed;
                await _productRepository.UpdateFamilyAsync(fam);
                await LoadFamiliesAsync();
                SetStatus($"Đã đổi tên dòng thành '{trimmed}' thành công!", true);
                return true;
            }

            SetStatus("Không tìm thấy dòng sản phẩm trong cơ sở dữ liệu!", false);
            return false;
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi khi đổi tên dòng: {ex.Message}", false);
            return false;
        }
    }

    public async Task<bool> DeleteFamilyAsync(FamilyCardViewModel card)
    {
        if (card == null) return false;

        if (!card.CanDelete)
        {
            SetStatus("Không thể xóa dòng sản phẩm mặc định của hệ thống!", false);
            return false;
        }

        try
        {
            var variants = await _productRepository.GetVariantsByFamilyIdAsync(card.FamilyId, includeInactive: true);
            if (variants.Count > 0)
            {
                SetStatus($"Không thể xóa dòng '{card.Name}' vì đang có {variants.Count} sản phẩm/quy cách thuộc dòng này!", false);
                return false;
            }

            await _productRepository.DeleteFamilyAsync(card.FamilyId);
            await LoadFamiliesAsync();
            SetStatus($"Đã xóa dòng sản phẩm '{card.Name}' thành công.", true);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Lỗi khi xóa dòng: {ex.Message}", false);
            return false;
        }
    }

    private void SetStatus(string message, bool isSuccess)
    {
        StatusMessage = message;
        IsSuccessStatus = isSuccess;
    }
}
