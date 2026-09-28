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

public class SqlitePrintSpecificationRepository : IPrintSpecificationRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqlitePrintSpecificationRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PrintSpecification?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var dto = await connection.QuerySingleOrDefaultAsync<PrintSpecDto>(
            "SELECT * FROM print_specifications WHERE id = @Id", new { Id = id });

        if (dto == null) return null;

        var spec = MapSpec(dto);

        var aliases = await connection.QueryAsync<SpecAliasDto>(
            "SELECT * FROM print_specification_aliases WHERE print_specification_id = @SpecId",
            new { SpecId = id });

        spec.Aliases = aliases.Select(MapAlias).ToList();
        return spec;
    }

    public async Task<IReadOnlyList<PrintSpecification>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string query = includeInactive
            ? "SELECT * FROM print_specifications ORDER BY canonical_name"
            : "SELECT * FROM print_specifications WHERE is_active = 1 ORDER BY canonical_name";

        var specDtos = await connection.QueryAsync<PrintSpecDto>(query);

        var aliasDtos = await connection.QueryAsync<SpecAliasDto>(
            "SELECT * FROM print_specification_aliases");

        var aliasLookup = aliasDtos.ToLookup(a => a.print_specification_id);

        var list = new List<PrintSpecification>();
        foreach (var dto in specDtos)
        {
            var spec = MapSpec(dto);
            spec.Aliases = aliasLookup[dto.id].Select(MapAlias).ToList();
            list.Add(spec);
        }

        return list;
    }

    public async Task<PrintSpecification?> FindExactMatchAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        long? specId = await connection.QuerySingleOrDefaultAsync<long?>(
            "SELECT print_specification_id FROM print_specification_aliases WHERE normalized_alias = @Norm LIMIT 1",
            new { Norm = normalizedName });

        if (specId.HasValue)
        {
            return await GetByIdAsync(specId.Value, cancellationToken);
        }

        var all = await GetAllAsync(includeInactive: false, cancellationToken);
        return all.FirstOrDefault(s => string.Equals(CustomerNormalizer.Normalize(s.CanonicalName), normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<PrintSpecification> CreateSpecificationAsync(PrintSpecification spec, string? initialAlias = null, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        long id = await connection.QuerySingleAsync<long>(@"
            INSERT INTO print_specifications (canonical_name, unit_price, is_active, created_at, updated_at)
            VALUES (@CanonicalName, @UnitPrice, @IsActive, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
        ", new
        {
            CanonicalName = spec.CanonicalName,
            UnitPrice = spec.UnitPrice,
            IsActive = spec.IsActive ? 1 : 0,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        }, transaction: transaction);

        spec.Id = id;

        string aliasToAdd = !string.IsNullOrWhiteSpace(initialAlias) ? initialAlias : spec.CanonicalName;
        string normalized = CustomerNormalizer.Normalize(aliasToAdd);

        await connection.ExecuteAsync(@"
            INSERT OR IGNORE INTO print_specification_aliases (print_specification_id, alias_text, normalized_alias)
            VALUES (@SpecId, @AliasText, @NormalizedAlias);
        ", new
        {
            SpecId = id,
            AliasText = aliasToAdd,
            NormalizedAlias = normalized
        }, transaction: transaction);

        transaction.Commit();
        return spec;
    }

    public async Task UpdateSpecificationAsync(PrintSpecification spec, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(@"
            UPDATE print_specifications
            SET canonical_name = @CanonicalName,
                unit_price = @UnitPrice,
                is_active = @IsActive,
                updated_at = @UpdatedAt
            WHERE id = @Id
        ", new
        {
            Id = spec.Id,
            CanonicalName = spec.CanonicalName,
            UnitPrice = spec.UnitPrice,
            IsActive = spec.IsActive ? 1 : 0,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        });
    }

    public async Task AddAliasAsync(long specId, string aliasText, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string normalized = CustomerNormalizer.Normalize(aliasText);

        await connection.ExecuteAsync(@"
            INSERT INTO print_specification_aliases (print_specification_id, alias_text, normalized_alias)
            VALUES (@SpecId, @AliasText, @NormalizedAlias)
            ON CONFLICT(normalized_alias) DO UPDATE SET print_specification_id = excluded.print_specification_id, alias_text = excluded.alias_text;
        ", new
        {
            SpecId = specId,
            AliasText = aliasText,
            NormalizedAlias = normalized
        });
    }

    public async Task<IReadOnlyList<PrintSpecificationAlias>> GetAllAliasesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var dtos = await connection.QueryAsync<SpecAliasDto>("SELECT * FROM print_specification_aliases");
        return dtos.Select(MapAlias).ToList();
    }

    private static PrintSpecification MapSpec(PrintSpecDto dto) => new()
    {
        Id = dto.id,
        CanonicalName = dto.canonical_name,
        UnitPrice = dto.unit_price,
        IsActive = dto.is_active == 1,
        CreatedAt = DateTimeOffset.Parse(dto.created_at),
        UpdatedAt = DateTimeOffset.Parse(dto.updated_at)
    };

    private static PrintSpecificationAlias MapAlias(SpecAliasDto dto) => new()
    {
        Id = dto.id,
        PrintSpecificationId = dto.print_specification_id,
        AliasText = dto.alias_text,
        NormalizedAlias = dto.normalized_alias
    };

    private class PrintSpecDto
    {
        public long id { get; set; }
        public string canonical_name { get; set; } = string.Empty;
        public long unit_price { get; set; }
        public int is_active { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string updated_at { get; set; } = string.Empty;
    }

    private class SpecAliasDto
    {
        public long id { get; set; }
        public long print_specification_id { get; set; }
        public string alias_text { get; set; } = string.Empty;
        public string normalized_alias { get; set; } = string.Empty;
    }
}
