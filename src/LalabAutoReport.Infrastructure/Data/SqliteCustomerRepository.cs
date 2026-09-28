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

public class SqliteCustomerRepository : ICustomerRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteCustomerRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Customer?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var customerDto = await connection.QuerySingleOrDefaultAsync<CustomerDto>(
            "SELECT * FROM customers WHERE id = @Id", new { Id = id });

        if (customerDto == null) return null;

        var customer = MapCustomer(customerDto);

        var aliases = await connection.QueryAsync<CustomerAliasDto>(
            "SELECT * FROM customer_aliases WHERE customer_id = @CustomerId",
            new { CustomerId = id });

        customer.Aliases = aliases.Select(MapAlias).ToList();
        return customer;
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var customerDtos = await connection.QueryAsync<CustomerDto>(
            "SELECT * FROM customers ORDER BY canonical_name");

        var aliasDtos = await connection.QueryAsync<CustomerAliasDto>(
            "SELECT * FROM customer_aliases");

        var aliasLookup = aliasDtos.ToLookup(a => a.customer_id);

        var customers = new List<Customer>();
        foreach (var dto in customerDtos)
        {
            var customer = MapCustomer(dto);
            customer.Aliases = aliasLookup[dto.id].Select(MapAlias).ToList();
            customers.Add(customer);
        }

        return customers;
    }

    public async Task<Customer?> FindExactMatchAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // 1. Check aliases first
        long? customerId = await connection.QuerySingleOrDefaultAsync<long?>(
            "SELECT customer_id FROM customer_aliases WHERE normalized_alias = @Norm LIMIT 1",
            new { Norm = normalizedName });

        if (customerId.HasValue)
        {
            return await GetByIdAsync(customerId.Value, cancellationToken);
        }

        // 2. Check canonical names
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(c => string.Equals(CustomerNormalizer.Normalize(c.CanonicalName), normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Customer> CreateCustomerAsync(Customer customer, string? initialAlias = null, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        long id = await connection.QuerySingleAsync<long>(@"
            INSERT INTO customers (canonical_name, phone, note, created_at, updated_at)
            VALUES (@CanonicalName, @Phone, @Note, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
        ", new
        {
            CanonicalName = customer.CanonicalName,
            Phone = customer.Phone,
            Note = customer.Note,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        }, transaction: transaction);

        customer.Id = id;

        // Automatically add canonical name as an alias if not explicitly provided
        string aliasToAdd = !string.IsNullOrWhiteSpace(initialAlias) ? initialAlias : customer.CanonicalName;
        string normalized = CustomerNormalizer.Normalize(aliasToAdd);

        await connection.ExecuteAsync(@"
            INSERT OR IGNORE INTO customer_aliases (customer_id, alias_text, normalized_alias, created_at)
            VALUES (@CustomerId, @AliasText, @NormalizedAlias, @CreatedAt);
        ", new
        {
            CustomerId = id,
            AliasText = aliasToAdd,
            NormalizedAlias = normalized,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o")
        }, transaction: transaction);

        transaction.Commit();
        return customer;
    }

    public async Task UpdateCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(@"
            UPDATE customers
            SET canonical_name = @CanonicalName,
                phone = @Phone,
                note = @Note,
                updated_at = @UpdatedAt
            WHERE id = @Id
        ", new
        {
            Id = customer.Id,
            CanonicalName = customer.CanonicalName,
            Phone = customer.Phone,
            Note = customer.Note,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        });
    }

    public async Task AddAliasAsync(long customerId, string aliasText, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string normalized = CustomerNormalizer.Normalize(aliasText);

        await connection.ExecuteAsync(@"
            INSERT INTO customer_aliases (customer_id, alias_text, normalized_alias, created_at)
            VALUES (@CustomerId, @AliasText, @NormalizedAlias, @CreatedAt)
            ON CONFLICT(normalized_alias) DO UPDATE SET customer_id = excluded.customer_id, alias_text = excluded.alias_text;
        ", new
        {
            CustomerId = customerId,
            AliasText = aliasText,
            NormalizedAlias = normalized,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o")
        });
    }

    public async Task RemoveAliasAsync(long aliasId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(
            "DELETE FROM customer_aliases WHERE id = @Id",
            new { Id = aliasId });
    }

    public async Task<IReadOnlyList<CustomerAlias>> GetAllAliasesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var dtos = await connection.QueryAsync<CustomerAliasDto>("SELECT * FROM customer_aliases");
        return dtos.Select(MapAlias).ToList();
    }

    private static Customer MapCustomer(CustomerDto dto) => new()
    {
        Id = dto.id,
        CanonicalName = dto.canonical_name,
        Phone = dto.phone,
        Note = dto.note,
        CreatedAt = DateTimeOffset.Parse(dto.created_at),
        UpdatedAt = DateTimeOffset.Parse(dto.updated_at)
    };

    private static CustomerAlias MapAlias(CustomerAliasDto dto) => new()
    {
        Id = dto.id,
        CustomerId = dto.customer_id,
        AliasText = dto.alias_text,
        NormalizedAlias = dto.normalized_alias,
        CreatedAt = DateTimeOffset.Parse(dto.created_at)
    };

    private class CustomerDto
    {
        public long id { get; set; }
        public string canonical_name { get; set; } = string.Empty;
        public string? phone { get; set; }
        public string? note { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string updated_at { get; set; } = string.Empty;
    }

    private class CustomerAliasDto
    {
        public long id { get; set; }
        public long customer_id { get; set; }
        public string alias_text { get; set; } = string.Empty;
        public string normalized_alias { get; set; } = string.Empty;
        public string created_at { get; set; } = string.Empty;
    }
}
