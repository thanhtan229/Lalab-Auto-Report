using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.Infrastructure.Data;

public class SqliteOrderRepository : IOrderRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteOrderRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Order?> GetOrderByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var orderDto = await connection.QuerySingleOrDefaultAsync<OrderDto>(
            "SELECT * FROM orders WHERE id = @Id",
            new { Id = id }
        );

        if (orderDto == null) return null;

        var order = MapOrder(orderDto);

        var items = await connection.QueryAsync<OrderItemDto>(@"
            SELECT i.*, ps.canonical_name AS spec_canonical_name, ps.unit_price AS unit_price 
            FROM order_item_scans i
            LEFT JOIN print_specifications ps ON i.print_specification_id = ps.id
            WHERE i.order_id = @OrderId
            AND i.scan_snapshot_id = (SELECT MAX(id) FROM scan_snapshots WHERE order_id = @OrderId)
        ", new { OrderId = order.Id });

        order.Items = items.Select(MapOrderItem).ToList();
        return order;
    }

    public async Task<Order?> GetOrderByRelativePathAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var orderDto = await connection.QuerySingleOrDefaultAsync<OrderDto>(
            "SELECT * FROM orders WHERE relative_path = @RelativePath",
            new { RelativePath = relativePath }
        );

        if (orderDto == null) return null;

        var order = MapOrder(orderDto);

        // Load items from latest scan
        var items = await connection.QueryAsync<OrderItemDto>(@"
            SELECT i.*, ps.canonical_name AS spec_canonical_name, ps.unit_price AS unit_price 
            FROM order_item_scans i
            LEFT JOIN print_specifications ps ON i.print_specification_id = ps.id
            WHERE i.order_id = @OrderId
            AND i.scan_snapshot_id = (SELECT MAX(id) FROM scan_snapshots WHERE order_id = @OrderId)
        ", new { OrderId = order.Id });

        order.Items = items.Select(MapOrderItem).ToList();
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersByDateAsync(string date, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var orderDtos = await connection.QueryAsync<OrderDto>(
            "SELECT * FROM orders WHERE work_date = @WorkDate ORDER BY original_folder_name",
            new { WorkDate = date }
        );

        var orders = new List<Order>();
        foreach (var dto in orderDtos)
        {
            var order = MapOrder(dto);

            var items = await connection.QueryAsync<OrderItemDto>(@"
                SELECT i.*, ps.canonical_name AS spec_canonical_name, ps.unit_price AS unit_price 
                FROM order_item_scans i
                LEFT JOIN print_specifications ps ON i.print_specification_id = ps.id
                WHERE i.order_id = @OrderId
                AND i.scan_snapshot_id = (SELECT MAX(id) FROM scan_snapshots WHERE order_id = @OrderId)
            ", new { OrderId = order.Id });

            order.Items = items.Select(MapOrderItem).ToList();
            orders.Add(order);
        }

        return orders;
    }

    public async Task SaveOrderAsync(Order order, ScanSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        // 1. Insert or update order
        if (order.Id == 0)
        {
            long orderId = await connection.QuerySingleAsync<long>(@"
                INSERT INTO orders (work_date, customer_id, original_folder_name, relative_path, status, filesystem_changed_after_lock, last_scan_at, created_at, updated_at)
                VALUES (@WorkDate, @CustomerId, @OriginalFolderName, @RelativePath, @Status, @FilesystemChangedAfterLock, @LastScanAt, @CreatedAt, @UpdatedAt);
                SELECT last_insert_rowid();
            ", new
            {
                WorkDate = order.WorkDate,
                CustomerId = order.CustomerId,
                OriginalFolderName = order.OriginalFolderName,
                RelativePath = order.RelativePath,
                Status = order.Status.ToString(),
                FilesystemChangedAfterLock = order.FilesystemChangedAfterLock ? 1 : 0,
                LastScanAt = order.LastScanAt?.ToString("o"),
                CreatedAt = order.CreatedAt.ToString("o"),
                UpdatedAt = order.UpdatedAt.ToString("o")
            }, transaction: transaction);

            order.Id = orderId;
        }
        else
        {
            await connection.ExecuteAsync(@"
                UPDATE orders
                SET customer_id = @CustomerId,
                    status = @Status,
                    filesystem_changed_after_lock = @FilesystemChangedAfterLock,
                    last_scan_at = @LastScanAt,
                    updated_at = @UpdatedAt
                WHERE id = @Id
            ", new
            {
                Id = order.Id,
                CustomerId = order.CustomerId,
                Status = order.Status.ToString(),
                FilesystemChangedAfterLock = order.FilesystemChangedAfterLock ? 1 : 0,
                LastScanAt = order.LastScanAt?.ToString("o"),
                UpdatedAt = order.UpdatedAt.ToString("o")
            }, transaction: transaction);
        }

        // 2. Insert scan snapshot
        snapshot.OrderId = order.Id;
        long snapshotId = await connection.QuerySingleAsync<long>(@"
            INSERT INTO scan_snapshots (order_id, started_at, completed_at, scan_scope, status, error_message, app_version)
            VALUES (@OrderId, @StartedAt, @CompletedAt, @ScanScope, @Status, @ErrorMessage, @AppVersion);
            SELECT last_insert_rowid();
        ", new
        {
            OrderId = snapshot.OrderId,
            StartedAt = snapshot.StartedAt.ToString("o"),
            CompletedAt = snapshot.CompletedAt?.ToString("o"),
            ScanScope = snapshot.Scope.ToString(),
            Status = snapshot.Status.ToString(),
            ErrorMessage = snapshot.ErrorMessage,
            AppVersion = snapshot.AppVersion
        }, transaction: transaction);

        snapshot.Id = snapshotId;

        // 3. Insert order item scans
        foreach (var item in order.Items)
        {
            item.ScanSnapshotId = snapshotId;
            item.OrderId = order.Id;

            string candidatesJson = JsonSerializer.Serialize(item.CandidatePrintFolderRelativePaths);

            long itemId = await connection.QuerySingleAsync<long>(@"
                INSERT INTO order_item_scans (
                    scan_snapshot_id, order_id, print_specification_id, specification_folder_name,
                    specification_relative_path, source_count, print_count, selected_print_folder_relative_path,
                    print_folder_status, mismatch_count, scan_status, error_message, candidate_print_folders,
                    bill_quantity, quantity_resolution_mode, quantity_resolution_note
                ) VALUES (
                    @ScanSnapshotId, @OrderId, @PrintSpecificationId, @SpecificationFolderName,
                    @SpecificationRelativePath, @SourceCount, @PrintCount, @SelectedPrintFolderRelativePath,
                    @PrintFolderStatus, @MismatchCount, @ScanStatus, @ErrorMessage, @CandidatePrintFolders,
                    @BillQuantity, @QuantityResolutionMode, @QuantityResolutionNote
                );
                SELECT last_insert_rowid();
            ", new
            {
                ScanSnapshotId = item.ScanSnapshotId,
                OrderId = item.OrderId,
                PrintSpecificationId = item.PrintSpecificationId,
                SpecificationFolderName = item.SpecificationFolderName,
                SpecificationRelativePath = item.SpecificationRelativePath,
                SourceCount = item.SourceCount,
                PrintCount = item.PrintCount,
                SelectedPrintFolderRelativePath = item.SelectedPrintFolderRelativePath,
                PrintFolderStatus = item.PrintFolderStatus.ToString(),
                MismatchCount = item.MismatchCount,
                ScanStatus = item.ScanStatus.ToString(),
                ErrorMessage = item.ErrorMessage,
                CandidatePrintFolders = candidatesJson,
                BillQuantity = item.BillQuantity,
                QuantityResolutionMode = item.QuantityResolutionMode?.ToString(),
                QuantityResolutionNote = item.QuantityResolutionNote
            }, transaction: transaction);

            item.Id = itemId;
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<string>> GetScannedDatesInMonthAsync(string yearMonth, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var dates = await connection.QueryAsync<string>(@"
            SELECT DISTINCT work_date FROM orders
            WHERE work_date LIKE @Pattern AND last_scan_at IS NOT NULL
        ", new { Pattern = yearMonth + "%" });

        return dates.ToList();
    }

    public async Task UpdateOrderItemResolutionAsync(long orderItemId, int billQuantity, QuantityResolutionMode mode, string? note, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(@"
            UPDATE order_item_scans
            SET bill_quantity = @BillQuantity,
                quantity_resolution_mode = @Mode,
                quantity_resolution_note = @Note
            WHERE id = @Id
        ", new
        {
            Id = orderItemId,
            BillQuantity = billQuantity,
            Mode = mode.ToString(),
            Note = note
        });
    }

    public async Task UpdateOrderItemPrintFolderAsync(long orderItemId, string printFolderRelativePath, int printCount, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(@"
            UPDATE order_item_scans
            SET selected_print_folder_relative_path = @Path,
                print_count = @Count,
                print_folder_status = @Status,
                mismatch_count = @Count - source_count
            WHERE id = @Id
        ", new
        {
            Id = orderItemId,
            Path = printFolderRelativePath,
            Count = printCount,
            Status = PrintFolderResolutionStatus.Resolved.ToString()
        });
    }

    public async Task UpdateOrderStatusAsync(long orderId, OrderStatus status, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            UPDATE orders
            SET status = @Status,
                updated_at = @UpdatedAt
            WHERE id = @Id
        ", new
        {
            Id = orderId,
            Status = status.ToString(),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        });
    }

    public async Task SetFilesystemChangedAfterLockAsync(long orderId, bool changed, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            UPDATE orders
            SET filesystem_changed_after_lock = @Changed,
                updated_at = @UpdatedAt
            WHERE id = @Id
        ", new
        {
            Id = orderId,
            Changed = changed ? 1 : 0,
            UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
        });
    }

    public async Task<IReadOnlyList<ScanSnapshot>> GetScanSnapshotsForOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var snapshotDtos = await connection.QueryAsync<ScanSnapshotDto>(
            "SELECT * FROM scan_snapshots WHERE order_id = @OrderId ORDER BY started_at DESC",
            new { OrderId = orderId });

        var snapshots = new List<ScanSnapshot>();
        foreach (var dto in snapshotDtos)
        {
            var items = await connection.QueryAsync<OrderItemDto>(@"
                SELECT i.*, ps.canonical_name AS spec_canonical_name, ps.unit_price AS unit_price 
                FROM order_item_scans i
                LEFT JOIN print_specifications ps ON i.print_specification_id = ps.id
                WHERE i.scan_snapshot_id = @SnapshotId
            ", new { SnapshotId = dto.id });

            snapshots.Add(new ScanSnapshot
            {
                Id = dto.id,
                OrderId = dto.order_id,
                StartedAt = DateTimeOffset.Parse(dto.started_at),
                CompletedAt = !string.IsNullOrEmpty(dto.completed_at) ? DateTimeOffset.Parse(dto.completed_at) : null,
                Scope = Enum.TryParse<ScanScope>(dto.scan_scope, out var s) ? s : ScanScope.Date,
                Status = Enum.TryParse<ScanStatus>(dto.status, out var st) ? st : ScanStatus.Pending,
                ErrorMessage = dto.error_message,
                AppVersion = dto.app_version,
                Items = items.Select(MapOrderItem).ToList()
            });
        }

        return snapshots;
    }

    private class ScanSnapshotDto
    {
        public long id { get; set; }
        public long order_id { get; set; }
        public string started_at { get; set; } = string.Empty;
        public string? completed_at { get; set; }
        public string scan_scope { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public string? error_message { get; set; }
        public string app_version { get; set; } = string.Empty;
    }

    private static Order MapOrder(OrderDto dto)
    {
        return new Order
        {
            Id = dto.id,
            WorkDate = dto.work_date,
            CustomerId = dto.customer_id,
            OriginalFolderName = dto.original_folder_name,
            RelativePath = dto.relative_path,
            Status = Enum.TryParse<OrderStatus>(dto.status, out var st) ? st : OrderStatus.Unscanned,
            FilesystemChangedAfterLock = dto.filesystem_changed_after_lock == 1,
            LastScanAt = !string.IsNullOrEmpty(dto.last_scan_at) ? DateTimeOffset.Parse(dto.last_scan_at) : null,
            CreatedAt = DateTimeOffset.Parse(dto.created_at),
            UpdatedAt = DateTimeOffset.Parse(dto.updated_at)
        };
    }

    private static OrderItemScan MapOrderItem(OrderItemDto dto)
    {
        var item = new OrderItemScan
        {
            Id = dto.id,
            ScanSnapshotId = dto.scan_snapshot_id,
            OrderId = dto.order_id,
            PrintSpecificationId = dto.print_specification_id,
            SpecificationFolderName = dto.specification_folder_name,
            SpecificationRelativePath = dto.specification_relative_path,
            SourceCount = (int)dto.source_count,
            PrintCount = (int?)dto.print_count,
            SelectedPrintFolderRelativePath = dto.selected_print_folder_relative_path,
            PrintFolderStatus = Enum.TryParse<PrintFolderResolutionStatus>(dto.print_folder_status, out var pfs) ? pfs : PrintFolderResolutionStatus.NoPrintFolder,
            MismatchCount = (int?)dto.mismatch_count,
            ScanStatus = Enum.TryParse<ScanStatus>(dto.scan_status, out var ss) ? ss : ScanStatus.Pending,
            ErrorMessage = dto.error_message,
            BillQuantity = (int?)dto.bill_quantity,
            QuantityResolutionMode = !string.IsNullOrEmpty(dto.quantity_resolution_mode) && Enum.TryParse<QuantityResolutionMode>(dto.quantity_resolution_mode, out var qrm) ? qrm : null,
            QuantityResolutionNote = dto.quantity_resolution_note
        };

        if (dto.print_specification_id.HasValue)
        {
            item.PrintSpecification = new PrintSpecification
            {
                Id = dto.print_specification_id.Value,
                CanonicalName = dto.spec_canonical_name ?? dto.specification_folder_name,
                UnitPrice = dto.unit_price ?? 0
            };
        }

        if (!string.IsNullOrWhiteSpace(dto.candidate_print_folders))
        {
            try
            {
                item.CandidatePrintFolderRelativePaths = JsonSerializer.Deserialize<List<string>>(dto.candidate_print_folders) ?? new();
            }
            catch
            {
                // ignored
            }
        }

        return item;
    }

    private class OrderDto
    {
        public long id { get; set; }
        public string work_date { get; set; } = string.Empty;
        public long? customer_id { get; set; }
        public string original_folder_name { get; set; } = string.Empty;
        public string relative_path { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public long filesystem_changed_after_lock { get; set; }
        public string? last_scan_at { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string updated_at { get; set; } = string.Empty;
    }

    private class OrderItemDto
    {
        public long id { get; set; }
        public long scan_snapshot_id { get; set; }
        public long order_id { get; set; }
        public long? print_specification_id { get; set; }
        public string specification_folder_name { get; set; } = string.Empty;
        public string specification_relative_path { get; set; } = string.Empty;
        public long source_count { get; set; }
        public long? print_count { get; set; }
        public string? selected_print_folder_relative_path { get; set; }
        public string print_folder_status { get; set; } = string.Empty;
        public long? mismatch_count { get; set; }
        public string scan_status { get; set; } = string.Empty;
        public string? error_message { get; set; }
        public string? candidate_print_folders { get; set; }
        public long? bill_quantity { get; set; }
        public string? quantity_resolution_mode { get; set; }
        public string? quantity_resolution_note { get; set; }
        public string? spec_canonical_name { get; set; }
        public long? unit_price { get; set; }
    }
}
