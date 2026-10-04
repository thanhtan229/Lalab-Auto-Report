using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.Data;

public class DatabaseResetService : IDatabaseResetService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IDatabaseBackupService _backupService;
    private readonly IDatabaseMigrator _databaseMigrator;
    private readonly ILogger<DatabaseResetService>? _logger;

    public DatabaseResetService(
        ISqliteConnectionFactory connectionFactory,
        IDatabaseBackupService backupService,
        IDatabaseMigrator databaseMigrator,
        ILogger<DatabaseResetService>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _backupService = backupService;
        _databaseMigrator = databaseMigrator;
        _logger = logger;
    }

    public async Task<DatabaseBackupInfo> ResetDataAsync(ResetDataScope scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string dbPath = _connectionFactory.DatabasePath;
        using var lifecycle = await DatabaseLifecycleGuard.EnterAsync(dbPath, cancellationToken);
        using (var guardConnection = _connectionFactory.CreateConnection())
            await DatabaseLifecycleGuard.EnsureLocalOnlyAsync(guardConnection);
        _logger?.LogInformation("Starting database reset with scope {Scope} on '{DbPath}'", scope, dbPath);

        // Step 1: Always take safety pre-reset backup first
        string backupDir = Path.Combine(
            Path.GetDirectoryName(dbPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "backups"
        );

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        string preResetFile = Path.Combine(backupDir, $"lalab_pre_reset_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");
        DatabaseBackupInfo backupInfo;
        try
        {
            backupInfo = await _backupService.CreateBackupAsync(preResetFile, cancellationToken);
            _logger?.LogInformation("Pre-reset safety backup created successfully at '{BackupPath}'", backupInfo.BackupPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create pre-reset safety backup! Aborting reset for safety.");
            throw new InvalidOperationException($"Không thể tạo bản sao lưu an toàn trước khi xóa: {ex.Message}. Thao tác đã bị hủy bỏ để bảo vệ dữ liệu.", ex);
        }

        // Step 2: Perform reset according to scope
        if (scope == ResetDataScope.OperationalOnly)
        {
            await ResetOperationalDataAsync(cancellationToken);
        }
        else if (scope == ResetDataScope.FactoryReset)
        {
            await FactoryResetAsync(cancellationToken);
        }

        _logger?.LogInformation("Database reset completed successfully for scope {Scope}", scope);
        return backupInfo;
    }

    private async Task ResetOperationalDataAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;", transaction: transaction);

            // Clean operational tables in reverse dependency order
            var operationalTables = new[]
            {
                "customer_bill_adjustments",
                "customer_bill_lines",
                "customer_bill_orders",
                "guest_bill_source_folders",
                "customer_bills",
                "bill_lines",
                "bills",
                "order_item_scans",
                "scan_snapshots",
                "orders",
                "folder_print_statuses"
            };

            foreach (var table in operationalTables)
            {
                // Check if table exists before deleting
                var exists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@TableName;",
                    new { TableName = table },
                    transaction: transaction
                );

                if (exists > 0)
                {
                    await connection.ExecuteAsync($"DELETE FROM {table};", transaction: transaction);
                }
            }

            // Preserve high-water IDs: an old Cloud identity must never be reused.

            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;", transaction: transaction);
            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger?.LogError(ex, "Failed during operational data reset");
            throw;
        }

        // Vacuum database outside transaction to reclaim space
        try
        {
            await connection.ExecuteAsync("VACUUM;");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "VACUUM after reset failed; continuing");
        }
    }

    private async Task FactoryResetAsync(CancellationToken cancellationToken)
    {
        string dbPath = _connectionFactory.DatabasePath;
        bool deletedFiles = false;

        SqliteConnection.ClearAllPools();

        try
        {
            // Attempt to delete physical files
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            string walPath = dbPath + "-wal";
            if (File.Exists(walPath))
            {
                File.Delete(walPath);
            }

            string shmPath = dbPath + "-shm";
            if (File.Exists(shmPath))
            {
                File.Delete(shmPath);
            }

            deletedFiles = true;
            _logger?.LogInformation("Successfully deleted active database file for Factory Reset");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not delete physical SQLite file directly (file lock). Falling back to DROP ALL TABLES.");
        }

        if (!deletedFiles)
        {
            // Fallback: Drop all tables within connection
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");

                var tableNames = (await connection.QueryAsync<string>(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"
                )).ToList();

                foreach (var table in tableNames)
                {
                    await connection.ExecuteAsync($"DROP TABLE IF EXISTS \"{table}\";");
                }

                await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
            }
        }

        // Re-run database migrations from scratch to recreate fresh schema and default seed data
        SqliteConnection.ClearAllPools();
        await _databaseMigrator.MigrateAsync(cancellationToken);
    }
}
