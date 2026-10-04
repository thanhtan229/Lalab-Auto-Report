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

public class SqliteRootFolderRepository : IRootFolderRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteRootFolderRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<RootFolder>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var dtos = await connection.QueryAsync<RootFolderDto>(@"
            SELECT * FROM root_folders ORDER BY is_default DESC, id ASC;
        ");
        return dtos.Select(MapDto).ToList();
    }

    public async Task<IReadOnlyList<RootFolder>> GetActiveRootsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var dtos = await connection.QueryAsync<RootFolderDto>(@"
            SELECT * FROM root_folders WHERE is_active = 1 ORDER BY is_default DESC, id ASC;
        ");
        return dtos.Select(MapDto).ToList();
    }

    public async Task<RootFolder?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var dto = await connection.QuerySingleOrDefaultAsync<RootFolderDto>(@"
            SELECT * FROM root_folders WHERE id = @Id;
        ", new { Id = id });

        return dto != null ? MapDto(dto) : null;
    }

    public async Task<RootFolder?> GetByPathAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return null;

        var allRoots = await GetAllAsync(cancellationToken);
        string normTarget = PathNormalizer.Normalize(fullPath);

        return allRoots.FirstOrDefault(r => PathNormalizer.Normalize(r.FullPath) == normTarget);
    }

    public async Task<RootFolder?> GetDefaultRootAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var dto = await connection.QuerySingleOrDefaultAsync<RootFolderDto>(@"
            SELECT * FROM root_folders WHERE is_default = 1 LIMIT 1;
        ");

        if (dto != null) return MapDto(dto);

        // Fallback: first root folder if none marked default
        var firstDto = await connection.QuerySingleOrDefaultAsync<RootFolderDto>(@"
            SELECT * FROM root_folders ORDER BY id ASC LIMIT 1;
        ");

        return firstDto != null ? MapDto(firstDto) : null;
    }

    public async Task<long> InsertAsync(RootFolder rootFolder, CancellationToken cancellationToken = default)
    {
        if (rootFolder == null) throw new ArgumentNullException(nameof(rootFolder));

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        // If this root is set as default, clear any other default
        if (rootFolder.IsDefault)
        {
            await connection.ExecuteAsync("UPDATE root_folders SET is_default = 0;", transaction: transaction);
        }

        string now = DateTimeOffset.UtcNow.ToString("O");
        long id = await connection.QuerySingleAsync<long>(@"
            INSERT INTO root_folders (name, full_path, is_active, is_default, created_at, updated_at)
            VALUES (@Name, @FullPath, @IsActive, @IsDefault, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
        ", new
        {
            Name = rootFolder.Name.Trim(),
            FullPath = rootFolder.FullPath.Trim(),
            IsActive = rootFolder.IsActive ? 1 : 0,
            IsDefault = rootFolder.IsDefault ? 1 : 0,
            CreatedAt = now,
            UpdatedAt = now
        }, transaction: transaction);

        transaction.Commit();
        rootFolder.Id = id;
        return id;
    }

    public async Task UpdateAsync(RootFolder rootFolder, CancellationToken cancellationToken = default)
    {
        if (rootFolder == null) throw new ArgumentNullException(nameof(rootFolder));

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        if (rootFolder.IsDefault)
        {
            await connection.ExecuteAsync("UPDATE root_folders SET is_default = 0 WHERE id != @Id;", new { Id = rootFolder.Id }, transaction: transaction);
        }

        await connection.ExecuteAsync(@"
            UPDATE root_folders
            SET name = @Name,
                full_path = @FullPath,
                is_active = @IsActive,
                is_default = @IsDefault,
                updated_at = @UpdatedAt
            WHERE id = @Id;
        ", new
        {
            Id = rootFolder.Id,
            Name = rootFolder.Name.Trim(),
            FullPath = rootFolder.FullPath.Trim(),
            IsActive = rootFolder.IsActive ? 1 : 0,
            IsDefault = rootFolder.IsDefault ? 1 : 0,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        }, transaction: transaction);

        transaction.Commit();
    }

    public async Task UpdatePathAsync(long id, string newPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPath)) throw new ArgumentException("Đường dẫn mới không được để trống.", nameof(newPath));

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            UPDATE root_folders
            SET full_path = @FullPath,
                updated_at = @UpdatedAt
            WHERE id = @Id;
        ", new
        {
            Id = id,
            FullPath = newPath.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        });
    }

    public async Task SetActiveAsync(long id, bool isActive, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            UPDATE root_folders
            SET is_active = @IsActive,
                updated_at = @UpdatedAt
            WHERE id = @Id;
        ", new
        {
            Id = id,
            IsActive = isActive ? 1 : 0,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        });
    }

    public async Task SetDefaultAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync("UPDATE root_folders SET is_default = 0;", transaction: transaction);
        await connection.ExecuteAsync(@"
            UPDATE root_folders
            SET is_default = 1,
                updated_at = @UpdatedAt
            WHERE id = @Id;
        ", new
        {
            Id = id,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        }, transaction: transaction);

        transaction.Commit();
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        // Check if orders exist for this root
        int orderCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM orders WHERE root_folder_id = @Id;",
            new { Id = id }
        );

        if (orderCount > 0)
        {
            throw new InvalidOperationException($"Không thể xóa Thư mục gốc này vì đang có {orderCount} đơn hàng liên kết. Hãy chuyển trạng thái sang Lưu trữ (Tắt quét) để giữ lịch sử.");
        }

        await connection.ExecuteAsync("DELETE FROM root_folders WHERE id = @Id;", new { Id = id });
    }

    public async Task<bool> HasOrdersAsync(long rootFolderId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        int count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM orders WHERE root_folder_id = @Id;",
            new { Id = rootFolderId }
        );
        return count > 0;
    }

    private static RootFolder MapDto(RootFolderDto dto)
    {
        return new RootFolder
        {
            Id = dto.id,
            Name = dto.name,
            FullPath = dto.full_path,
            IsActive = dto.is_active == 1,
            IsDefault = dto.is_default == 1,
            CreatedAt = DateTimeOffset.TryParse(dto.created_at, out var ca) ? ca : DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.TryParse(dto.updated_at, out var ua) ? ua : DateTimeOffset.UtcNow
        };
    }

    private class RootFolderDto
    {
        public long id { get; set; }
        public string name { get; set; } = string.Empty;
        public string full_path { get; set; } = string.Empty;
        public long is_active { get; set; }
        public long is_default { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string updated_at { get; set; } = string.Empty;
    }
}
