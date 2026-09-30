using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;

namespace LalabAutoReport.Infrastructure.Data;

public class SqliteProductRepository : IProductRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteProductRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ==========================================
    // PRODUCT FAMILIES
    // ==========================================

    public async Task<ProductFamily?> GetFamilyByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var dto = await connection.QuerySingleOrDefaultAsync<FamilyDto>(
            "SELECT * FROM product_families WHERE id = @Id", new { Id = id });

        if (dto == null) return null;

        var family = MapFamily(dto);
        var aliases = await connection.QueryAsync<FamilyAliasDto>(
            "SELECT * FROM product_family_aliases WHERE family_id = @FamilyId", new { FamilyId = id });
        family.Aliases = aliases.Select(MapFamilyAlias).ToList();

        var variants = await GetVariantsByFamilyIdAsync(id, includeInactive: true, cancellationToken);
        family.Variants = variants.ToList();

        return family;
    }

    public async Task<ProductFamily?> GetFamilyByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var dto = await connection.QuerySingleOrDefaultAsync<FamilyDto>(
            "SELECT * FROM product_families WHERE name = @Name LIMIT 1", new { Name = name.Trim() });

        if (dto == null) return null;

        return await GetFamilyByIdAsync(dto.id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductFamily>> GetAllFamiliesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var dtos = (await connection.QueryAsync<FamilyDto>("SELECT * FROM product_families ORDER BY name")).ToList();
        var aliasDtos = (await connection.QueryAsync<FamilyAliasDto>("SELECT * FROM product_family_aliases")).ToList();
        var aliasLookup = aliasDtos.ToLookup(a => a.family_id);

        var allVariants = await GetAllVariantsAsync(includeInactive: true, cancellationToken);
        var variantLookup = allVariants.ToLookup(v => v.FamilyId);

        var list = new List<ProductFamily>();
        foreach (var dto in dtos)
        {
            var fam = MapFamily(dto);
            fam.Aliases = aliasLookup[dto.id].Select(MapFamilyAlias).ToList();
            fam.Variants = variantLookup[dto.id].ToList();
            list.Add(fam);
        }

        return list;
    }

    public async Task<ProductFamily> CreateFamilyAsync(ProductFamily family, IEnumerable<string>? initialAliases = null, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        long id = await connection.QuerySingleAsync<long>(@"
            INSERT INTO product_families (name, category, billing_method, created_at, updated_at)
            VALUES (@Name, @Category, @BillingMethod, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
        ", new
        {
            Name = family.Name.Trim(),
            Category = family.Category.ToString(),
            BillingMethod = family.BillingMethod.ToString(),
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        }, transaction: transaction);

        family.Id = id;

        // Add family aliases
        var aliasesToAdd = initialAliases?.ToList() ?? new List<string>();
        if (!aliasesToAdd.Any(a => string.Equals(CustomerNormalizer.Normalize(a), CustomerNormalizer.Normalize(family.Name), StringComparison.OrdinalIgnoreCase)))
        {
            aliasesToAdd.Add(family.Name);
        }

        foreach (var alias in aliasesToAdd.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            string norm = CustomerNormalizer.Normalize(alias);
            await connection.ExecuteAsync(@"
                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                VALUES (@FamilyId, @AliasText, @NormalizedAlias, @CreatedAt);
            ", new
            {
                FamilyId = id,
                AliasText = alias.Trim(),
                NormalizedAlias = norm,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o")
            }, transaction: transaction);
        }

        transaction.Commit();
        return (await GetFamilyByIdAsync(id, cancellationToken))!;
    }

    public async Task UpdateFamilyAsync(ProductFamily family, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(@"
            UPDATE product_families
            SET name = @Name,
                category = @Category,
                billing_method = @BillingMethod,
                updated_at = @UpdatedAt
            WHERE id = @Id;
        ", new
        {
            Id = family.Id,
            Name = family.Name.Trim(),
            Category = family.Category.ToString(),
            BillingMethod = family.BillingMethod.ToString(),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        });
    }

    public async Task DeleteFamilyAsync(long familyId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync("DELETE FROM product_families WHERE id = @Id", new { Id = familyId });
    }

    public async Task AddFamilyAliasAsync(long familyId, string aliasText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(aliasText)) return;

        using var connection = _connectionFactory.CreateConnection();
        string norm = CustomerNormalizer.Normalize(aliasText);

        await connection.ExecuteAsync(@"
            INSERT INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
            VALUES (@FamilyId, @AliasText, @NormalizedAlias, @CreatedAt)
            ON CONFLICT(normalized_alias) DO UPDATE SET family_id = excluded.family_id, alias_text = excluded.alias_text;
        ", new
        {
            FamilyId = familyId,
            AliasText = aliasText.Trim(),
            NormalizedAlias = norm,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o")
        });
    }

    public async Task RemoveFamilyAliasAsync(long aliasId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM product_family_aliases WHERE id = @Id", new { Id = aliasId });
    }

    public async Task<IReadOnlyList<ProductFamilyAlias>> GetAllFamilyAliasesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var dtos = await connection.QueryAsync<FamilyAliasDto>("SELECT * FROM product_family_aliases");
        return dtos.Select(MapFamilyAlias).ToList();
    }

    // ==========================================
    // PRODUCT VARIANTS
    // ==========================================

    public async Task<ProductVariant?> GetVariantByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var dto = await connection.QuerySingleOrDefaultAsync<VariantDto>(@"
            SELECT v.*, f.name AS family_name, f.category AS family_category, f.billing_method AS family_billing_method
            FROM product_variants v
            INNER JOIN product_families f ON v.family_id = f.id
            WHERE v.id = @Id
        ", new { Id = id });

        if (dto == null) return null;

        var variant = MapVariant(dto);
        var aliases = await connection.QueryAsync<SpecificAliasDto>(
            "SELECT * FROM product_specific_aliases WHERE variant_id = @VariantId", new { VariantId = id });
        variant.SpecificAliases = aliases.Select(MapSpecificAlias).ToList();

        return variant;
    }

    public async Task<IReadOnlyList<ProductVariant>> GetAllVariantsAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string query = @"
            SELECT v.*, f.name AS family_name, f.category AS family_category, f.billing_method AS family_billing_method
            FROM product_variants v
            INNER JOIN product_families f ON v.family_id = f.id
        ";
        if (!includeInactive)
        {
            query += " WHERE v.is_active = 1";
        }
        query += " ORDER BY f.name, v.canonical_size";

        var dtos = (await connection.QueryAsync<VariantDto>(query)).ToList();
        var aliasDtos = (await connection.QueryAsync<SpecificAliasDto>("SELECT * FROM product_specific_aliases")).ToList();
        var aliasLookup = aliasDtos.ToLookup(a => a.variant_id);

        var list = new List<ProductVariant>();
        foreach (var dto in dtos)
        {
            var variant = MapVariant(dto);
            variant.SpecificAliases = aliasLookup[dto.id].Select(MapSpecificAlias).ToList();
            list.Add(variant);
        }

        return list;
    }

    public async Task<IReadOnlyList<ProductVariant>> GetVariantsByFamilyIdAsync(long familyId, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string query = @"
            SELECT v.*, f.name AS family_name, f.category AS family_category, f.billing_method AS family_billing_method
            FROM product_variants v
            INNER JOIN product_families f ON v.family_id = f.id
            WHERE v.family_id = @FamilyId
        ";
        if (!includeInactive)
        {
            query += " AND v.is_active = 1";
        }
        query += " ORDER BY v.canonical_size";

        var dtos = (await connection.QueryAsync<VariantDto>(query, new { FamilyId = familyId })).ToList();
        var aliasDtos = (await connection.QueryAsync<SpecificAliasDto>("SELECT * FROM product_specific_aliases")).ToList();
        var aliasLookup = aliasDtos.ToLookup(a => a.variant_id);

        var list = new List<ProductVariant>();
        foreach (var dto in dtos)
        {
            var variant = MapVariant(dto);
            variant.SpecificAliases = aliasLookup[dto.id].Select(MapSpecificAlias).ToList();
            list.Add(variant);
        }

        return list;
    }

    public async Task<ProductVariant> CreateVariantAsync(ProductVariant variant, string? initialAlias = null, CancellationToken cancellationToken = default)
    {
        string canonicalSize = SizeNormalizer.NormalizeSize(variant.CanonicalSize);
        if (string.IsNullOrWhiteSpace(canonicalSize))
        {
            throw new ArgumentException("Kích thước variant không hợp lệ.", nameof(variant));
        }
        variant.CanonicalSize = canonicalSize;

        // Collision validation: prevent duplicate size in same family (e.g. 20x30 vs 30x20)
        if (await HasVariantCollisionAsync(variant.FamilyId, canonicalSize, null, cancellationToken))
        {
            throw new InvalidOperationException($"Kích thước '{canonicalSize}' đã tồn tại trong dòng sản phẩm này.");
        }

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        long id = await connection.QuerySingleAsync<long>(@"
            INSERT INTO product_variants (
                family_id, canonical_size, canonical_name, unit_price, included_sheets,
                base_price, extra_sheet_price, is_active, created_at, updated_at
            ) VALUES (
                @FamilyId, @CanonicalSize, @CanonicalName, @UnitPrice, @IncludedSheets,
                @BasePrice, @ExtraSheetPrice, @IsActive, @CreatedAt, @UpdatedAt
            );
            SELECT last_insert_rowid();
        ", new
        {
            FamilyId = variant.FamilyId,
            CanonicalSize = canonicalSize,
            CanonicalName = !string.IsNullOrWhiteSpace(variant.CanonicalName) ? variant.CanonicalName.Trim() : canonicalSize,
            UnitPrice = variant.UnitPrice,
            IncludedSheets = variant.IncludedSheets,
            BasePrice = variant.BasePrice,
            ExtraSheetPrice = variant.ExtraSheetPrice,
            IsActive = variant.IsActive ? 1 : 0,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        }, transaction: transaction);

        variant.Id = id;

        // Sync to print_specifications table for backward compatibility
        var family = await connection.QuerySingleOrDefaultAsync<FamilyDto>(
            "SELECT * FROM product_families WHERE id = @Id", new { Id = variant.FamilyId }, transaction: transaction);

        await connection.ExecuteAsync(@"
            INSERT INTO print_specifications (
                id, family_id, canonical_size, canonical_name, unit_price, category,
                billing_method, included_sheets, base_price, extra_sheet_price, is_active, created_at, updated_at
            ) VALUES (
                @Id, @FamilyId, @CanonicalSize, @CanonicalName, @UnitPrice, @Category,
                @BillingMethod, @IncludedSheets, @BasePrice, @ExtraSheetPrice, @IsActive, @CreatedAt, @UpdatedAt
            )
            ON CONFLICT(id) DO UPDATE SET
                family_id = excluded.family_id,
                canonical_size = excluded.canonical_size,
                canonical_name = excluded.canonical_name,
                unit_price = excluded.unit_price,
                category = excluded.category,
                billing_method = excluded.billing_method,
                included_sheets = excluded.included_sheets,
                base_price = excluded.base_price,
                extra_sheet_price = excluded.extra_sheet_price,
                is_active = excluded.is_active,
                updated_at = excluded.updated_at;
        ", new
        {
            Id = id,
            FamilyId = variant.FamilyId,
            CanonicalSize = canonicalSize,
            CanonicalName = variant.CanonicalName,
            UnitPrice = variant.UnitPrice,
            Category = family?.category ?? "PhotoPrint",
            BillingMethod = family?.billing_method ?? "FileCount",
            IncludedSheets = variant.IncludedSheets,
            BasePrice = variant.BasePrice,
            ExtraSheetPrice = variant.ExtraSheetPrice,
            IsActive = variant.IsActive ? 1 : 0,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        }, transaction: transaction);

        // Add initial alias if provided
        if (!string.IsNullOrWhiteSpace(initialAlias))
        {
            string normAlias = CustomerNormalizer.Normalize(initialAlias);
            await connection.ExecuteAsync(@"
                INSERT OR IGNORE INTO product_specific_aliases (variant_id, alias_text, normalized_alias, created_at)
                VALUES (@VariantId, @AliasText, @NormalizedAlias, @CreatedAt);
            ", new
            {
                VariantId = id,
                AliasText = initialAlias.Trim(),
                NormalizedAlias = normAlias,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o")
            }, transaction: transaction);

            // Also sync to print_specification_aliases
            await connection.ExecuteAsync(@"
                INSERT OR IGNORE INTO print_specification_aliases (print_specification_id, alias_text, normalized_alias)
                VALUES (@SpecId, @AliasText, @NormalizedAlias);
            ", new
            {
                SpecId = id,
                AliasText = initialAlias.Trim(),
                NormalizedAlias = normAlias
            }, transaction: transaction);
        }

        transaction.Commit();
        return (await GetVariantByIdAsync(id, cancellationToken))!;
    }

    public async Task UpdateVariantAsync(ProductVariant variant, CancellationToken cancellationToken = default)
    {
        string canonicalSize = SizeNormalizer.NormalizeSize(variant.CanonicalSize);
        if (string.IsNullOrWhiteSpace(canonicalSize))
        {
            throw new ArgumentException("Kích thước variant không hợp lệ.", nameof(variant));
        }
        variant.CanonicalSize = canonicalSize;

        // Collision check
        if (await HasVariantCollisionAsync(variant.FamilyId, canonicalSize, variant.Id, cancellationToken))
        {
            throw new InvalidOperationException($"Kích thước '{canonicalSize}' đã tồn tại trong dòng sản phẩm này.");
        }

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(@"
            UPDATE product_variants
            SET canonical_size = @CanonicalSize,
                canonical_name = @CanonicalName,
                unit_price = @UnitPrice,
                included_sheets = @IncludedSheets,
                base_price = @BasePrice,
                extra_sheet_price = @ExtraSheetPrice,
                is_active = @IsActive,
                updated_at = @UpdatedAt
            WHERE id = @Id;
        ", new
        {
            Id = variant.Id,
            CanonicalSize = canonicalSize,
            CanonicalName = variant.CanonicalName.Trim(),
            UnitPrice = variant.UnitPrice,
            IncludedSheets = variant.IncludedSheets,
            BasePrice = variant.BasePrice,
            ExtraSheetPrice = variant.ExtraSheetPrice,
            IsActive = variant.IsActive ? 1 : 0,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        }, transaction: transaction);

        // Sync to print_specifications
        await connection.ExecuteAsync(@"
            UPDATE print_specifications
            SET canonical_size = @CanonicalSize,
                canonical_name = @CanonicalName,
                unit_price = @UnitPrice,
                included_sheets = @IncludedSheets,
                base_price = @BasePrice,
                extra_sheet_price = @ExtraSheetPrice,
                is_active = @IsActive,
                updated_at = @UpdatedAt
            WHERE id = @Id;
        ", new
        {
            Id = variant.Id,
            CanonicalSize = canonicalSize,
            CanonicalName = variant.CanonicalName.Trim(),
            UnitPrice = variant.UnitPrice,
            IncludedSheets = variant.IncludedSheets,
            BasePrice = variant.BasePrice,
            ExtraSheetPrice = variant.ExtraSheetPrice,
            IsActive = variant.IsActive ? 1 : 0,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        }, transaction: transaction);

        transaction.Commit();
    }

    public async Task DeleteVariantAsync(long variantId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        // Check if referenced in historical orders or bills
        int orderScanCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM order_item_scans WHERE print_specification_id = @Id", new { Id = variantId }, transaction: transaction);
        int billLineCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM bill_lines WHERE print_specification_id = @Id", new { Id = variantId }, transaction: transaction);
        int custBillLineCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM customer_bill_lines WHERE product_specification_id = @Id", new { Id = variantId }, transaction: transaction);

        if (orderScanCount > 0 || billLineCount > 0 || custBillLineCount > 0)
        {
            // Soft delete: mark inactive
            await connection.ExecuteAsync("UPDATE product_variants SET is_active = 0 WHERE id = @Id", new { Id = variantId }, transaction: transaction);
            await connection.ExecuteAsync("UPDATE print_specifications SET is_active = 0 WHERE id = @Id", new { Id = variantId }, transaction: transaction);
        }
        else
        {
            // Hard delete
            await connection.ExecuteAsync("DELETE FROM product_specific_aliases WHERE variant_id = @Id", new { Id = variantId }, transaction: transaction);
            await connection.ExecuteAsync("DELETE FROM print_specification_aliases WHERE print_specification_id = @Id", new { Id = variantId }, transaction: transaction);
            await connection.ExecuteAsync("DELETE FROM product_variants WHERE id = @Id", new { Id = variantId }, transaction: transaction);
            await connection.ExecuteAsync("DELETE FROM print_specifications WHERE id = @Id", new { Id = variantId }, transaction: transaction);
        }

        transaction.Commit();
    }

    // ==========================================
    // SPECIFIC ALIASES
    // ==========================================

    public async Task AddSpecificAliasAsync(long variantId, string aliasText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(aliasText)) return;

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        string norm = CustomerNormalizer.Normalize(aliasText);

        await connection.ExecuteAsync(@"
            INSERT INTO product_specific_aliases (variant_id, alias_text, normalized_alias, created_at)
            VALUES (@VariantId, @AliasText, @NormalizedAlias, @CreatedAt);
        ", new
        {
            VariantId = variantId,
            AliasText = aliasText.Trim(),
            NormalizedAlias = norm,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o")
        }, transaction: transaction);

        // Sync with print_specification_aliases
        await connection.ExecuteAsync(@"
            INSERT OR IGNORE INTO print_specification_aliases (print_specification_id, alias_text, normalized_alias)
            VALUES (@SpecId, @AliasText, @NormalizedAlias);
        ", new
        {
            SpecId = variantId,
            AliasText = aliasText.Trim(),
            NormalizedAlias = norm
        }, transaction: transaction);

        transaction.Commit();
    }

    public async Task UpdateSpecificAliasAsync(long aliasId, string newAliasText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newAliasText))
            throw new ArgumentException("Tên alias không được để trống.", nameof(newAliasText));

        string trimmed = newAliasText.Trim();
        string norm = CustomerNormalizer.Normalize(trimmed);

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(@"
            UPDATE product_specific_aliases
            SET alias_text = @AliasText,
                normalized_alias = @NormalizedAlias
            WHERE id = @Id;
        ", new
        {
            Id = aliasId,
            AliasText = trimmed,
            NormalizedAlias = norm
        }, transaction: transaction);

        transaction.Commit();
    }

    public async Task RemoveSpecificAliasAsync(long aliasId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM product_specific_aliases WHERE id = @Id", new { Id = aliasId });
    }

    public async Task<IReadOnlyList<ProductSpecificAlias>> GetAllSpecificAliasesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var dtos = await connection.QueryAsync<SpecificAliasDto>("SELECT * FROM product_specific_aliases");
        return dtos.Select(MapSpecificAlias).ToList();
    }

    public async Task<IReadOnlyList<ProductVariant>> FindVariantsBySpecificAliasAsync(string normalizedAlias, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var variantIds = (await connection.QueryAsync<long>(@"
            SELECT DISTINCT variant_id FROM product_specific_aliases
            WHERE normalized_alias = @Norm
        ", new { Norm = normalizedAlias })).ToList();

        if (variantIds.Count == 0)
        {
            // Also check print_specification_aliases in case of legacy aliases
            variantIds = (await connection.QueryAsync<long>(@"
                SELECT DISTINCT print_specification_id FROM print_specification_aliases
                WHERE normalized_alias = @Norm
            ", new { Norm = normalizedAlias })).ToList();
        }

        var results = new List<ProductVariant>();
        foreach (var id in variantIds)
        {
            var v = await GetVariantByIdAsync(id, cancellationToken);
            if (v != null && v.IsActive)
            {
                results.Add(v);
            }
        }

        return results;
    }

    public async Task<bool> HasVariantCollisionAsync(long familyId, string canonicalSize, long? excludeVariantId = null, CancellationToken cancellationToken = default)
    {
        string norm = SizeNormalizer.NormalizeSize(canonicalSize);
        if (string.IsNullOrEmpty(norm)) return false;

        using var connection = _connectionFactory.CreateConnection();

        var existingSizes = (await connection.QueryAsync<string>(@"
            SELECT canonical_size FROM product_variants
            WHERE family_id = @FamilyId AND (@ExcludeId IS NULL OR id != @ExcludeId)
        ", new { FamilyId = familyId, ExcludeId = excludeVariantId })).ToList();

        return existingSizes.Any(s => SizeNormalizer.AreSizesEqual(s, norm));
    }

    // ==========================================
    // BACKWARD COMPATIBILITY: IPrintSpecificationRepository
    // ==========================================

    public async Task<PrintSpecification?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var v = await GetVariantByIdAsync(id, cancellationToken);
        if (v != null)
        {
            var spec = v.ToPrintSpecification();
            spec.Aliases = v.SpecificAliases.Select(a => new PrintSpecificationAlias
            {
                Id = a.Id,
                PrintSpecificationId = v.Id,
                AliasText = a.AliasText,
                NormalizedAlias = a.NormalizedAlias
            }).ToList();
            return spec;
        }

        // Fallback directly to print_specifications table
        using var connection = _connectionFactory.CreateConnection();
        var dto = await connection.QuerySingleOrDefaultAsync<PrintSpecDtoLegacy>(
            "SELECT * FROM print_specifications WHERE id = @Id", new { Id = id });
        if (dto == null) return null;
        return MapLegacySpec(dto);
    }

    public async Task<IReadOnlyList<PrintSpecification>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var variants = await GetAllVariantsAsync(includeInactive, cancellationToken);
        if (variants.Count > 0)
        {
            return variants.Select(v =>
            {
                var spec = v.ToPrintSpecification();
                spec.Aliases = v.SpecificAliases.Select(a => new PrintSpecificationAlias
                {
                    Id = a.Id,
                    PrintSpecificationId = v.Id,
                    AliasText = a.AliasText,
                    NormalizedAlias = a.NormalizedAlias
                }).ToList();
                return spec;
            }).ToList();
        }

        // Fallback
        using var connection = _connectionFactory.CreateConnection();
        string q = includeInactive
            ? "SELECT * FROM print_specifications ORDER BY canonical_name"
            : "SELECT * FROM print_specifications WHERE is_active = 1 ORDER BY canonical_name";
        var dtos = await connection.QueryAsync<PrintSpecDtoLegacy>(q);
        return dtos.Select(MapLegacySpec).ToList();
    }

    public async Task<PrintSpecification?> FindExactMatchAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        var variants = await FindVariantsBySpecificAliasAsync(normalizedName, cancellationToken);
        if (variants.Count == 1)
        {
            return (await GetByIdAsync(variants[0].Id, cancellationToken));
        }

        var all = await GetAllAsync(includeInactive: false, cancellationToken);
        return all.FirstOrDefault(s => string.Equals(CustomerNormalizer.Normalize(s.CanonicalName), normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<PrintSpecification> CreateSpecificationAsync(PrintSpecification spec, string? initialAlias = null, CancellationToken cancellationToken = default)
    {
        // Find or create appropriate family
        long familyId;
        if (spec.FamilyId.HasValue)
        {
            familyId = spec.FamilyId.Value;
        }
        else
        {
            string famName = DetermineFamilyName(spec.CanonicalName, spec.Category);
            var fam = await GetFamilyByNameAsync(famName, cancellationToken);
            if (fam == null)
            {
                fam = await CreateFamilyAsync(new ProductFamily
                {
                    Name = famName,
                    Category = spec.Category,
                    BillingMethod = spec.BillingMethod
                }, cancellationToken: cancellationToken);

                await SeedDefaultFamilyAliasesAsync(fam.Id, famName, cancellationToken);
            }
            familyId = fam.Id;
        }

        string size = !string.IsNullOrWhiteSpace(spec.CanonicalSize)
            ? spec.CanonicalSize
            : SizeNormalizer.NormalizeSize(spec.CanonicalName);

        var variant = new ProductVariant
        {
            FamilyId = familyId,
            CanonicalSize = size,
            CanonicalName = spec.CanonicalName,
            UnitPrice = spec.UnitPrice,
            IncludedSheets = spec.IncludedSheets,
            BasePrice = spec.BasePrice,
            ExtraSheetPrice = spec.ExtraSheetPrice,
            IsActive = spec.IsActive
        };

        var created = await CreateVariantAsync(variant, initialAlias ?? spec.CanonicalName, cancellationToken);
        return (await GetByIdAsync(created.Id, cancellationToken))!;
    }

    public async Task UpdateSpecificationAsync(PrintSpecification spec, CancellationToken cancellationToken = default)
    {
        var variant = await GetVariantByIdAsync(spec.Id, cancellationToken);
        if (variant != null)
        {
            variant.CanonicalName = spec.CanonicalName;
            variant.UnitPrice = spec.UnitPrice;
            variant.IncludedSheets = spec.IncludedSheets;
            variant.BasePrice = spec.BasePrice;
            variant.ExtraSheetPrice = spec.ExtraSheetPrice;
            variant.IsActive = spec.IsActive;
            if (!string.IsNullOrWhiteSpace(spec.CanonicalSize))
            {
                variant.CanonicalSize = spec.CanonicalSize;
            }
            await UpdateVariantAsync(variant, cancellationToken);
        }
    }

    public async Task DeleteSpecificationAsync(long specId, CancellationToken cancellationToken = default)
    {
        await DeleteVariantAsync(specId, cancellationToken);
    }

    public async Task AddAliasAsync(long specId, string aliasText, CancellationToken cancellationToken = default)
    {
        await AddSpecificAliasAsync(specId, aliasText, cancellationToken);
    }

    public async Task UpdateAliasAsync(long aliasId, string newAliasText, CancellationToken cancellationToken = default)
    {
        await UpdateSpecificAliasAsync(aliasId, newAliasText, cancellationToken);
    }

    public async Task RemoveAliasAsync(long aliasId, CancellationToken cancellationToken = default)
    {
        await RemoveSpecificAliasAsync(aliasId, cancellationToken);
    }

    public async Task<IReadOnlyList<PrintSpecificationAlias>> GetAllAliasesAsync(CancellationToken cancellationToken = default)
    {
        var aliases = await GetAllSpecificAliasesAsync(cancellationToken);
        return aliases.Select(a => new PrintSpecificationAlias
        {
            Id = a.Id,
            PrintSpecificationId = a.VariantId,
            AliasText = a.AliasText,
            NormalizedAlias = a.NormalizedAlias
        }).ToList();
    }

    // ==========================================
    // MAPPERS & DTOS
    // ==========================================

    private static ProductFamily MapFamily(FamilyDto dto) => new()
    {
        Id = dto.id,
        Name = dto.name,
        Category = Enum.TryParse<ProductCategory>(dto.category, out var cat) ? cat : ProductCategory.PhotoPrint,
        BillingMethod = Enum.TryParse<BillingMethod>(dto.billing_method, out var bm) ? bm : BillingMethod.FileCount,
        CreatedAt = DateTimeOffset.TryParse(dto.created_at, out var ca) ? ca : DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.TryParse(dto.updated_at, out var ua) ? ua : DateTimeOffset.UtcNow
    };

    private static ProductFamilyAlias MapFamilyAlias(FamilyAliasDto dto) => new()
    {
        Id = dto.id,
        FamilyId = dto.family_id,
        AliasText = dto.alias_text,
        NormalizedAlias = dto.normalized_alias,
        CreatedAt = DateTimeOffset.TryParse(dto.created_at, out var ca) ? ca : DateTimeOffset.UtcNow
    };

    private static ProductVariant MapVariant(VariantDto dto) => new()
    {
        Id = dto.id,
        FamilyId = dto.family_id,
        CanonicalSize = dto.canonical_size,
        CanonicalName = dto.canonical_name,
        UnitPrice = dto.unit_price,
        IncludedSheets = dto.included_sheets,
        BasePrice = dto.base_price,
        ExtraSheetPrice = dto.extra_sheet_price,
        IsActive = dto.is_active == 1,
        CreatedAt = DateTimeOffset.TryParse(dto.created_at, out var ca) ? ca : DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.TryParse(dto.updated_at, out var ua) ? ua : DateTimeOffset.UtcNow,
        Family = new ProductFamily
        {
            Id = dto.family_id,
            Name = dto.family_name ?? string.Empty,
            Category = Enum.TryParse<ProductCategory>(dto.family_category, out var cat) ? cat : ProductCategory.PhotoPrint,
            BillingMethod = Enum.TryParse<BillingMethod>(dto.family_billing_method, out var bm) ? bm : BillingMethod.FileCount
        }
    };

    private static ProductSpecificAlias MapSpecificAlias(SpecificAliasDto dto) => new()
    {
        Id = dto.id,
        VariantId = dto.variant_id,
        AliasText = dto.alias_text,
        NormalizedAlias = dto.normalized_alias,
        CreatedAt = DateTimeOffset.TryParse(dto.created_at, out var ca) ? ca : DateTimeOffset.UtcNow
    };

    public static string DetermineFamilyName(string canonicalName, ProductCategory category)
    {
        if (category == ProductCategory.Album || category == ProductCategory.Photobook)
            return "Album";

        if (category == ProductCategory.Canvas)
            return "Canvas";

        if (category == ProductCategory.Frame)
            return "Khung";

        if (category == ProductCategory.WoodMount)
            return "Tranh Gỗ";

        string name = canonicalName.Trim();
        if (name.Contains("Mica", StringComparison.OrdinalIgnoreCase))
            return "Tranh Mica";

        if (name.StartsWith("Tranh Gỗ", StringComparison.OrdinalIgnoreCase) || name.Contains("Tranh Go", StringComparison.OrdinalIgnoreCase))
            return "Tranh Gỗ";

        if (name.Contains("Canvas", StringComparison.OrdinalIgnoreCase))
            return "Canvas";

        if (name.Contains("Pha Lê", StringComparison.OrdinalIgnoreCase) || name.Contains("Pha Le", StringComparison.OrdinalIgnoreCase))
            return "Pha Lê";

        if (name.StartsWith("Khung", StringComparison.OrdinalIgnoreCase))
            return "Khung";

        return "Ảnh in";
    }

    private async Task SeedDefaultFamilyAliasesAsync(long familyId, string familyName, CancellationToken cancellationToken)
    {
        if (string.Equals(familyName, "Tranh Mica", StringComparison.OrdinalIgnoreCase))
        {
            await AddFamilyAliasAsync(familyId, "tranh mica", cancellationToken);
            await AddFamilyAliasAsync(familyId, "mica", cancellationToken);
        }
        else if (string.Equals(familyName, "Tranh Gỗ", StringComparison.OrdinalIgnoreCase))
        {
            await AddFamilyAliasAsync(familyId, "tranh go", cancellationToken);
            await AddFamilyAliasAsync(familyId, "go", cancellationToken);
        }
        else if (string.Equals(familyName, "Canvas", StringComparison.OrdinalIgnoreCase))
        {
            await AddFamilyAliasAsync(familyId, "canvas", cancellationToken);
        }
        else if (string.Equals(familyName, "Pha Lê", StringComparison.OrdinalIgnoreCase))
        {
            await AddFamilyAliasAsync(familyId, "pha le", cancellationToken);
            await AddFamilyAliasAsync(familyId, "tranh pha le", cancellationToken);
        }
        else if (string.Equals(familyName, "Khung", StringComparison.OrdinalIgnoreCase))
        {
            await AddFamilyAliasAsync(familyId, "khung", cancellationToken);
        }
        else if (!string.Equals(familyName, "Ảnh in", StringComparison.OrdinalIgnoreCase) && !string.Equals(familyName, "Album", StringComparison.OrdinalIgnoreCase))
        {
            await AddFamilyAliasAsync(familyId, familyName.ToLowerInvariant(), cancellationToken);
        }
    }

    private static PrintSpecification MapLegacySpec(PrintSpecDtoLegacy dto) => new()
    {
        Id = dto.id,
        FamilyId = dto.family_id,
        CanonicalSize = dto.canonical_size,
        CanonicalName = dto.canonical_name,
        UnitPrice = dto.unit_price,
        Category = Enum.TryParse<ProductCategory>(dto.category, out var cat) ? cat : ProductCategory.PhotoPrint,
        BillingMethod = Enum.TryParse<BillingMethod>(dto.billing_method, out var bm) ? bm : BillingMethod.FileCount,
        IncludedSheets = dto.included_sheets,
        BasePrice = dto.base_price,
        ExtraSheetPrice = dto.extra_sheet_price,
        IsActive = dto.is_active == 1
    };

    private class FamilyDto
    {
        public long id { get; set; }
        public string name { get; set; } = string.Empty;
        public string category { get; set; } = "PhotoPrint";
        public string billing_method { get; set; } = "FileCount";
        public string? created_at { get; set; }
        public string? updated_at { get; set; }
    }

    private class FamilyAliasDto
    {
        public long id { get; set; }
        public long family_id { get; set; }
        public string alias_text { get; set; } = string.Empty;
        public string normalized_alias { get; set; } = string.Empty;
        public string? created_at { get; set; }
    }

    private class VariantDto
    {
        public long id { get; set; }
        public long family_id { get; set; }
        public string canonical_size { get; set; } = string.Empty;
        public string canonical_name { get; set; } = string.Empty;
        public long unit_price { get; set; }
        public int? included_sheets { get; set; }
        public long? base_price { get; set; }
        public long? extra_sheet_price { get; set; }
        public int is_active { get; set; }
        public string? created_at { get; set; }
        public string? updated_at { get; set; }
        public string? family_name { get; set; }
        public string? family_category { get; set; }
        public string? family_billing_method { get; set; }
    }

    private class SpecificAliasDto
    {
        public long id { get; set; }
        public long variant_id { get; set; }
        public string alias_text { get; set; } = string.Empty;
        public string normalized_alias { get; set; } = string.Empty;
        public string? created_at { get; set; }
    }

    private class PrintSpecDtoLegacy
    {
        public long id { get; set; }
        public long? family_id { get; set; }
        public string? canonical_size { get; set; }
        public string canonical_name { get; set; } = string.Empty;
        public long unit_price { get; set; }
        public string category { get; set; } = "PhotoPrint";
        public string billing_method { get; set; } = "FileCount";
        public int? included_sheets { get; set; }
        public long? base_price { get; set; }
        public long? extra_sheet_price { get; set; }
        public int is_active { get; set; }
    }
}
