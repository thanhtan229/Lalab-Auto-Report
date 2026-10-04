using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.Data;

public class DataPurgeService : IDataPurgeService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IDatabaseBackupService _backupService;
    private readonly ILogger<DataPurgeService>? _logger;

    public DataPurgeService(
        ISqliteConnectionFactory connectionFactory,
        IDatabaseBackupService backupService,
        ILogger<DataPurgeService>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<PurgePreviewResult> GetPurgePreviewAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string cutoffStr = cutoffDate.ToString("yyyy-MM-dd");

        using var connection = _connectionFactory.CreateConnection();

        int orderCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM orders WHERE work_date < @Cutoff;",
            new { Cutoff = cutoffStr }
        );

        int billCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM customer_bills WHERE period_end < @Cutoff;",
            new { Cutoff = cutoffStr }
        );

        int itemCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM order_item_scans WHERE order_id IN (SELECT id FROM orders WHERE work_date < @Cutoff);",
            new { Cutoff = cutoffStr }
        );

        int snapshotCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM scan_snapshots WHERE order_id IN (SELECT id FROM orders WHERE work_date < @Cutoff);",
            new { Cutoff = cutoffStr }
        );

        return new PurgePreviewResult(
            CutoffDate: cutoffDate,
            OrderCount: orderCount,
            BillCount: billCount,
            OrderItemCount: itemCount,
            SnapshotCount: snapshotCount
        );
    }

    public async Task<PurgeExecutionResult> PurgeOperationalDataBeforeDateAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string cutoffStr = cutoffDate.ToString("yyyy-MM-dd");

        string dbPath = _connectionFactory.DatabasePath;
        using var lifecycle = await DatabaseLifecycleGuard.EnterAsync(dbPath, cancellationToken);
        using (var guardConnection = _connectionFactory.CreateConnection())
            await DatabaseLifecycleGuard.EnsureLocalOnlyAsync(guardConnection);
        string backupDir = Path.Combine(
            Path.GetDirectoryName(dbPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "backups"
        );

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        // Step 1: Pre-purge safety backup
        string backupFile = Path.Combine(backupDir, $"lalab_pre_purge_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");
        _logger?.LogInformation("Creating pre-purge safety backup at '{BackupPath}'", backupFile);
        var backupInfo = await _backupService.CreateBackupAsync(backupFile, cancellationToken);

        // Step 2: Delete operational rows before cutoffDate
        int ordersDeleted = 0;
        int billsDeleted = 0;

        using (var connection = _connectionFactory.CreateConnection())
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;", transaction: transaction);

                // 2.1 Customer bill adjustments, lines, and orders
                await connection.ExecuteAsync(@"
                    DELETE FROM customer_bill_adjustments 
                    WHERE bill_id IN (SELECT id FROM customer_bills WHERE period_end < @Cutoff);
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                await connection.ExecuteAsync(@"
                    DELETE FROM customer_bill_lines 
                    WHERE bill_id IN (SELECT id FROM customer_bills WHERE period_end < @Cutoff);
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                await connection.ExecuteAsync(@"
                    DELETE FROM customer_bill_orders 
                    WHERE bill_id IN (SELECT id FROM customer_bills WHERE period_end < @Cutoff);
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                await connection.ExecuteAsync(@"
                    DELETE FROM guest_bill_source_folders 
                    WHERE bill_id IN (SELECT id FROM customer_bills WHERE period_end < @Cutoff);
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                // 2.2 Customer bills
                billsDeleted = await connection.ExecuteAsync(@"
                    DELETE FROM customer_bills WHERE period_end < @Cutoff;
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                // 2.3 Legacy bills and lines
                await connection.ExecuteAsync(@"
                    DELETE FROM bill_lines 
                    WHERE bill_id IN (SELECT id FROM bills WHERE order_id IN (SELECT id FROM orders WHERE work_date < @Cutoff));
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                await connection.ExecuteAsync(@"
                    DELETE FROM bills 
                    WHERE order_id IN (SELECT id FROM orders WHERE work_date < @Cutoff);
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                // 2.4 Order items and snapshots
                await connection.ExecuteAsync(@"
                    DELETE FROM order_item_scans 
                    WHERE order_id IN (SELECT id FROM orders WHERE work_date < @Cutoff);
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                await connection.ExecuteAsync(@"
                    DELETE FROM scan_snapshots 
                    WHERE order_id IN (SELECT id FROM orders WHERE work_date < @Cutoff);
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                // 2.5 Folder print statuses for purged orders
                await connection.ExecuteAsync(@"
                    DELETE FROM folder_print_statuses 
                    WHERE order_id IN (SELECT id FROM orders WHERE work_date < @Cutoff);
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                // 2.6 Orders
                ordersDeleted = await connection.ExecuteAsync(@"
                    DELETE FROM orders WHERE work_date < @Cutoff;
                ", new { Cutoff = cutoffStr }, transaction: transaction);

                await connection.ExecuteAsync("PRAGMA foreign_keys = ON;", transaction: transaction);
                transaction.Commit();
                _logger?.LogInformation("Successfully purged {Orders} orders and {Bills} bills before {Cutoff}", ordersDeleted, billsDeleted, cutoffStr);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger?.LogError(ex, "Failed to purge operational data before {Cutoff}", cutoffStr);
                throw;
            }
        }

        // Step 3: Run VACUUM to reclaim storage and defragment database file
        try
        {
            using var vacuumConnection = _connectionFactory.CreateConnection();
            await vacuumConnection.ExecuteAsync("VACUUM;");
            _logger?.LogInformation("Database VACUUM completed successfully after purge.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "VACUUM after purge encountered non-critical error; database is intact.");
        }

        return new PurgeExecutionResult(
            CutoffDate: cutoffDate,
            OrdersDeleted: ordersDeleted,
            BillsDeleted: billsDeleted,
            BackupFilePath: backupInfo.BackupPath
        );
    }
}
