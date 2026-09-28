using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.Infrastructure.Data;

public class SqliteBillRepository : IBillRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteBillRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Bill?> GetBillByOrderIdAsync(long orderId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var billDto = await connection.QuerySingleOrDefaultAsync<BillDto>(
            "SELECT * FROM bills WHERE order_id = @OrderId ORDER BY id DESC LIMIT 1",
            new { OrderId = orderId });

        if (billDto == null) return null;

        var bill = MapBill(billDto);

        var lineDtos = await connection.QueryAsync<BillLineDto>(
            "SELECT * FROM bill_lines WHERE bill_id = @BillId",
            new { BillId = bill.Id });

        bill.Lines = lineDtos.Select(MapBillLine).ToList();
        return bill;
    }

    public async Task<IReadOnlyList<Bill>> GetBillsByDateAsync(string date, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var billDtos = await connection.QueryAsync<BillDto>(@"
            SELECT b.* FROM bills b
            INNER JOIN orders o ON b.order_id = o.id
            WHERE o.work_date = @WorkDate
        ", new { WorkDate = date });

        var bills = new List<Bill>();
        foreach (var dto in billDtos)
        {
            var bill = MapBill(dto);
            var lines = await connection.QueryAsync<BillLineDto>(
                "SELECT * FROM bill_lines WHERE bill_id = @BillId",
                new { BillId = bill.Id });
            bill.Lines = lines.Select(MapBillLine).ToList();
            bills.Add(bill);
        }

        return bills;
    }

    public async Task<IReadOnlyList<Bill>> GetBillsByMonthAsync(string yearMonth, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var billDtos = await connection.QueryAsync<BillDto>(@"
            SELECT b.* FROM bills b
            INNER JOIN orders o ON b.order_id = o.id
            WHERE o.work_date LIKE @Pattern
        ", new { Pattern = yearMonth + "%" });

        var bills = new List<Bill>();
        foreach (var dto in billDtos)
        {
            var bill = MapBill(dto);
            var lines = await connection.QueryAsync<BillLineDto>(
                "SELECT * FROM bill_lines WHERE bill_id = @BillId",
                new { BillId = bill.Id });
            bill.Lines = lines.Select(MapBillLine).ToList();
            bills.Add(bill);
        }

        return bills;
    }

    public async Task<IReadOnlyList<Bill>> GetBillsByDateRangeAsync(string startDate, string endDate, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var billDtos = await connection.QueryAsync<BillDto>(@"
            SELECT b.* FROM bills b
            INNER JOIN orders o ON b.order_id = o.id
            WHERE o.work_date >= @StartDate AND o.work_date <= @EndDate
        ", new { StartDate = startDate, EndDate = endDate });

        var bills = new List<Bill>();
        foreach (var dto in billDtos)
        {
            var bill = MapBill(dto);
            var lines = await connection.QueryAsync<BillLineDto>(
                "SELECT * FROM bill_lines WHERE bill_id = @BillId",
                new { BillId = bill.Id });
            bill.Lines = lines.Select(MapBillLine).ToList();
            bills.Add(bill);
        }

        return bills;
    }

    public async Task SaveBillAsync(Bill bill, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        if (bill.Id == 0)
        {
            long billId = await connection.QuerySingleAsync<long>(@"
                INSERT INTO bills (order_id, customer_id, status, subtotal, locked_at, created_at, updated_at)
                VALUES (@OrderId, @CustomerId, @Status, @Subtotal, @LockedAt, @CreatedAt, @UpdatedAt);
                SELECT last_insert_rowid();
            ", new
            {
                OrderId = bill.OrderId,
                CustomerId = bill.CustomerId,
                Status = bill.Status.ToString(),
                Subtotal = bill.Subtotal,
                LockedAt = bill.LockedAt?.ToString("o"),
                CreatedAt = bill.CreatedAt.ToString("o"),
                UpdatedAt = bill.UpdatedAt.ToString("o")
            }, transaction: transaction);

            bill.Id = billId;
        }
        else
        {
            await connection.ExecuteAsync(@"
                UPDATE bills
                SET customer_id = @CustomerId,
                    status = @Status,
                    subtotal = @Subtotal,
                    locked_at = @LockedAt,
                    updated_at = @UpdatedAt
                WHERE id = @Id
            ", new
            {
                Id = bill.Id,
                CustomerId = bill.CustomerId,
                Status = bill.Status.ToString(),
                Subtotal = bill.Subtotal,
                LockedAt = bill.LockedAt?.ToString("o"),
                UpdatedAt = bill.UpdatedAt.ToString("o")
            }, transaction: transaction);

            // Delete old lines for update
            await connection.ExecuteAsync(
                "DELETE FROM bill_lines WHERE bill_id = @BillId",
                new { BillId = bill.Id },
                transaction: transaction);
        }

        // Insert bill lines
        foreach (var line in bill.Lines)
        {
            line.BillId = bill.Id;
            long lineId = await connection.QuerySingleAsync<long>(@"
                INSERT INTO bill_lines (
                    bill_id, print_specification_id, source_count, print_count,
                    bill_quantity, quantity_resolution_mode, quantity_resolution_note,
                    unit_price, line_total, source_scan_snapshot_id
                ) VALUES (
                    @BillId, @PrintSpecificationId, @SourceCount, @PrintCount,
                    @BillQuantity, @QuantityResolutionMode, @QuantityResolutionNote,
                    @UnitPrice, @LineTotal, @SourceScanSnapshotId
                );
                SELECT last_insert_rowid();
            ", new
            {
                BillId = line.BillId,
                PrintSpecificationId = line.PrintSpecificationId,
                SourceCount = line.SourceCount,
                PrintCount = line.PrintCount,
                BillQuantity = line.BillQuantity,
                QuantityResolutionMode = line.QuantityResolutionMode.ToString(),
                QuantityResolutionNote = line.QuantityResolutionNote,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal,
                SourceScanSnapshotId = line.SourceScanSnapshotId
            }, transaction: transaction);

            line.Id = lineId;
        }

        transaction.Commit();
    }

    private static Bill MapBill(BillDto dto) => new()
    {
        Id = dto.id,
        OrderId = dto.order_id,
        CustomerId = dto.customer_id,
        Status = Enum.TryParse<OrderStatus>(dto.status, out var st) ? st : OrderStatus.Billed,
        Subtotal = dto.subtotal,
        LockedAt = !string.IsNullOrEmpty(dto.locked_at) ? DateTimeOffset.Parse(dto.locked_at) : null,
        CreatedAt = DateTimeOffset.Parse(dto.created_at),
        UpdatedAt = DateTimeOffset.Parse(dto.updated_at)
    };

    private static BillLine MapBillLine(BillLineDto dto) => new()
    {
        Id = dto.id,
        BillId = dto.bill_id,
        PrintSpecificationId = dto.print_specification_id,
        SourceCount = (int)dto.source_count,
        PrintCount = (int?)dto.print_count,
        BillQuantity = (int)dto.bill_quantity,
        QuantityResolutionMode = Enum.TryParse<QuantityResolutionMode>(dto.quantity_resolution_mode, out var qrm) ? qrm : QuantityResolutionMode.AutoMatch,
        QuantityResolutionNote = dto.quantity_resolution_note,
        UnitPrice = dto.unit_price,
        LineTotal = dto.line_total,
        SourceScanSnapshotId = dto.source_scan_snapshot_id
    };

    private class BillDto
    {
        public long id { get; set; }
        public long order_id { get; set; }
        public long customer_id { get; set; }
        public string status { get; set; } = string.Empty;
        public long subtotal { get; set; }
        public string? locked_at { get; set; }
        public string created_at { get; set; } = string.Empty;
        public string updated_at { get; set; } = string.Empty;
    }

    private class BillLineDto
    {
        public long id { get; set; }
        public long bill_id { get; set; }
        public long print_specification_id { get; set; }
        public long source_count { get; set; }
        public long? print_count { get; set; }
        public long bill_quantity { get; set; }
        public string quantity_resolution_mode { get; set; } = string.Empty;
        public string? quantity_resolution_note { get; set; }
        public long unit_price { get; set; }
        public long line_total { get; set; }
        public long source_scan_snapshot_id { get; set; }
    }
}
