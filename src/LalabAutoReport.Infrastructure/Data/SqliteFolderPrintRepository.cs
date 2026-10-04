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

public class SqliteFolderPrintRepository : IFolderPrintRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteFolderPrintRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<FolderPrintRecord?> GetByNormalizedPathAsync(string normalizedPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath)) return null;

        using var connection = _connectionFactory.CreateConnection();
        var dto = await connection.QuerySingleOrDefaultAsync<FolderPrintRecordDto>(@"
            SELECT * FROM folder_print_statuses
            WHERE normalized_path = @NormalizedPath
            LIMIT 1;
        ", new { NormalizedPath = normalizedPath });

        return dto != null ? MapDto(dto) : null;
    }

    public async Task<FolderPrintRecord?> GetByFolderPathAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;
        string norm = PathNormalizer.Normalize(folderPath);
        return await GetByNormalizedPathAsync(norm, cancellationToken);
    }

    public async Task SaveAsync(FolderPrintRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));

        string norm = !string.IsNullOrWhiteSpace(record.NormalizedPath)
            ? record.NormalizedPath
            : PathNormalizer.Normalize(record.FolderPath);

        record.NormalizedPath = norm;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            INSERT INTO folder_print_statuses (folder_path, normalized_path, status, marked_at, marked_by, order_id, printed_sub_count, total_sub_count, created_at, updated_at)
            VALUES (@FolderPath, @NormalizedPath, @Status, @MarkedAt, @MarkedBy, @OrderId, @PrintedSubCount, @TotalSubCount, @CreatedAt, @UpdatedAt)
            ON CONFLICT(normalized_path) DO UPDATE SET
                folder_path = excluded.folder_path,
                status = excluded.status,
                marked_at = excluded.marked_at,
                marked_by = excluded.marked_by,
                order_id = COALESCE(excluded.order_id, folder_print_statuses.order_id),
                printed_sub_count = excluded.printed_sub_count,
                total_sub_count = excluded.total_sub_count,
                updated_at = excluded.updated_at;
        ", new
        {
            FolderPath = record.FolderPath,
            NormalizedPath = record.NormalizedPath,
            Status = (int)record.Status,
            MarkedAt = record.MarkedAt.ToString("o"),
            MarkedBy = record.MarkedBy,
            OrderId = record.AssociatedOrderId,
            PrintedSubCount = record.PrintedSubCount,
            TotalSubCount = record.TotalSubCount,
            CreatedAt = record.CreatedAt.ToString("o"),
            UpdatedAt = record.UpdatedAt.ToString("o")
        });
    }

    public async Task DeleteAsync(string normalizedPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath)) return;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            DELETE FROM folder_print_statuses
            WHERE normalized_path = @NormalizedPath;
        ", new { NormalizedPath = normalizedPath });
    }

    public async Task<IReadOnlySet<string>> GetAllPrintedNormalizedPathsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var paths = await connection.QueryAsync<string>(@"
            SELECT normalized_path
            FROM folder_print_statuses
            WHERE status = 1;
        ");

        return paths.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<int> GetPrintedCountAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM folder_print_statuses
            WHERE status = 1;
        ");
    }

    private static FolderPrintRecord MapDto(FolderPrintRecordDto dto)
    {
        return new FolderPrintRecord
        {
            Id = dto.id,
            FolderPath = dto.folder_path,
            NormalizedPath = dto.normalized_path,
            Status = (PrintStatus)dto.status,
            MarkedAt = DateTimeOffset.Parse(dto.marked_at),
            MarkedBy = dto.marked_by,
            AssociatedOrderId = dto.order_id,
            PrintedSubCount = dto.printed_sub_count,
            TotalSubCount = dto.total_sub_count,
            CreatedAt = DateTimeOffset.Parse(dto.created_at),
            UpdatedAt = DateTimeOffset.Parse(dto.updated_at)
        };
    }

    private class FolderPrintRecordDto
    {
        public long id { get; set; }
        public string folder_path { get; set; } = string.Empty;
        public string normalized_path { get; set; } = string.Empty;
        public int status { get; set; }
        public string marked_at { get; set; } = string.Empty;
        public string marked_by { get; set; } = string.Empty;
        public long? order_id { get; set; }
        public int? printed_sub_count { get; set; }
        public int? total_sub_count { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string updated_at { get; set; } = string.Empty;
    }
}
