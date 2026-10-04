using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LalabAutoReport.Tests;

public class DatabaseResetServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _tempDbPath;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IDatabaseBackupService _backupService;
    private readonly IDatabaseMigrator _migrator;
    private readonly DatabaseResetService _resetService;
    private readonly SqliteCustomerRepository _customerRepo;
    private readonly SqliteOrderRepository _orderRepo;
    private readonly SqliteFolderPrintRepository _printRepo;

    public DatabaseResetServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "LalabResetTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _tempDbPath = Path.Combine(_tempRoot, "test_reset.db");
        _connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        _backupService = new DatabaseBackupService(_connectionFactory);
        _migrator = new DatabaseMigrator(_connectionFactory, _backupService);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _resetService = new DatabaseResetService(_connectionFactory, _backupService, _migrator);
        _customerRepo = new SqliteCustomerRepository(_connectionFactory);
        _orderRepo = new SqliteOrderRepository(_connectionFactory);
        _printRepo = new SqliteFolderPrintRepository(_connectionFactory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors on Windows
        }
    }

    [Fact]
    public async Task ResetDataAsync_OperationalOnly_ClearsOrdersAndBills_PreservesCustomersAndSpecs()
    {
        // 1. Seed Customer
        var customer = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Studio Áo Cưới Paris",
            Phone = "0987654321",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await _customerRepo.AddAliasAsync(customer.Id, "Paris Studio");

        // Seed Order & Snapshot
        var order = new Order
        {
            CustomerId = customer.Id,
            WorkDate = "2026-09-30",
            RelativePath = "2026-09-30\\Studio Áo Cưới Paris",
            OriginalFolderName = "Studio Áo Cưới Paris",
            Status = OrderStatus.Scanned,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var snapshot = new ScanSnapshot
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Scope = ScanScope.Date,
            Status = ScanStatus.Success
        };
        await _orderRepo.SaveOrderAsync(order, snapshot);

        // Seed folder print status
        string printFolder = Path.Combine(_tempRoot, "2026-09-30", "Studio Áo Cưới Paris");
        await _printRepo.SaveAsync(new FolderPrintRecord
        {
            FolderPath = printFolder,
            Status = PrintStatus.Printed,
            MarkedAt = DateTimeOffset.UtcNow
        });

        // Verify data before reset
        var ordersBefore = await _orderRepo.GetOrdersByDateAsync("2026-09-30");
        Assert.NotEmpty(ordersBefore);
        var printStatusBefore = await _printRepo.GetByFolderPathAsync(printFolder);
        Assert.NotNull(printStatusBefore);
        Assert.Equal(PrintStatus.Printed, printStatusBefore!.Status);

        // 2. Perform OperationalOnly Reset
        var backupInfo = await _resetService.ResetDataAsync(ResetDataScope.OperationalOnly);

        // 3. Verify safety backup exists
        Assert.NotNull(backupInfo);
        Assert.True(File.Exists(backupInfo.BackupPath));
        Assert.True(backupInfo.FileSizeBytes > 0);

        // 4. Verify operational data is wiped
        var ordersAfter = await _orderRepo.GetOrdersByDateAsync("2026-09-30");
        Assert.Empty(ordersAfter);
        var printStatusAfter = await _printRepo.GetByFolderPathAsync(printFolder);
        Assert.Null(printStatusAfter);

        // 5. Verify Customers and Aliases are preserved
        var fetchedCustomer = await _customerRepo.GetByIdAsync(customer.Id);
        Assert.NotNull(fetchedCustomer);
        Assert.Equal("Studio Áo Cưới Paris", fetchedCustomer!.CanonicalName);
        Assert.Contains(fetchedCustomer.Aliases, a => a.AliasText == "Paris Studio");

        // Verify products / specifications are still intact
        using var connection = _connectionFactory.CreateConnection();
        var specCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM print_specifications;");
        Assert.True(specCount > 0);
    }

    [Fact]
    public async Task ResetDataAsync_FactoryReset_WipesEverything_AndReseedsDefaults()
    {
        // 1. Seed custom customer
        var customer = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Khách Hàng Tạm",
            Phone = "0123456789",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        // 2. Perform Factory Reset
        var backupInfo = await _resetService.ResetDataAsync(ResetDataScope.FactoryReset);

        // 3. Verify safety backup was created
        Assert.NotNull(backupInfo);
        Assert.True(File.Exists(backupInfo.BackupPath));

        // 4. Verify custom customer is completely wiped
        var fetchedCustomer = await _customerRepo.GetByIdAsync(customer.Id);
        Assert.Null(fetchedCustomer);

        // 5. Verify default schema & seed migrations re-ran successfully
        using var connection = _connectionFactory.CreateConnection();
        var migrationCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations;");
        Assert.True(migrationCount > 0);

        var specCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM print_specifications;");
        Assert.True(specCount > 0);
    }

    [Fact]
    public async Task ResetDataAsync_PreResetBackup_CanBeRestoredFully()
    {
        // 1. Seed Customer and Order
        var customer = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Khách Test Phục Hồi",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var order = new Order
        {
            CustomerId = customer.Id,
            WorkDate = "2026-09-30",
            RelativePath = "2026-09-30\\Khách Test Phục Hồi",
            OriginalFolderName = "Khách Test Phục Hồi",
            Status = OrderStatus.Scanned,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var snapshot = new ScanSnapshot
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Scope = ScanScope.Date,
            Status = ScanStatus.Success
        };
        await _orderRepo.SaveOrderAsync(order, snapshot);

        // 2. Reset operational data
        var backupInfo = await _resetService.ResetDataAsync(ResetDataScope.OperationalOnly);
        Assert.Empty(await _orderRepo.GetOrdersByDateAsync("2026-09-30"));

        // 3. Restore from the pre-reset safety backup
        await _backupService.RestoreBackupAsync(backupInfo.BackupPath);

        // 4. Verify orders and data are fully restored
        var restoredOrders = await _orderRepo.GetOrdersByDateAsync("2026-09-30");
        Assert.Single(restoredOrders);
        Assert.Equal("2026-09-30\\Khách Test Phục Hồi", restoredOrders[0].RelativePath);
    }
    [Theory]
    [InlineData(ResetDataScope.OperationalOnly, "stream")]
    [InlineData(ResetDataScope.FactoryReset, "stream")]
    [InlineData(ResetDataScope.OperationalOnly, "pending")]
    [InlineData(ResetDataScope.FactoryReset, "pending")]
    [InlineData(ResetDataScope.OperationalOnly, "disabled-secret")]
    [InlineData(ResetDataScope.FactoryReset, "enabled")]
    public async Task Reset_CloudLifecycleIsBound_RejectsWithoutDeletingData(ResetDataScope scope, string state)
    {
        using (var c = _connectionFactory.CreateConnection())
        {
            await c.ExecuteAsync("INSERT INTO orders(id,work_date,original_folder_name,relative_path,status,created_at,updated_at) VALUES(123,'2026-10-02','Old','Old','Scanned',datetime('now'),datetime('now'))");
            if (state == "stream") await c.ExecuteAsync("UPDATE cloud_sync_state SET stream_id='old-stream',endpoint='https://cloud' WHERE id=1");
            if (state == "pending") await c.ExecuteAsync("INSERT INTO cloud_operational_outbox(operation_id,entity_type,entity_id,value_json,created_at) VALUES('old-op','order_note',123,'true',datetime('now'))");
            if (state == "disabled-secret") await c.ExecuteAsync("INSERT OR REPLACE INTO app_settings(key,value) VALUES('EnableCloudSync','False'),('CloudSyncSecret','previous-secret')");
            if (state == "enabled") await c.ExecuteAsync("INSERT OR REPLACE INTO app_settings(key,value) VALUES('EnableCloudSync','True'),('CloudSyncSecret','configured-secret')");
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _resetService.ResetDataAsync(scope));
        Assert.Contains("Cloud", ex.Message);
        using var check = _connectionFactory.CreateConnection();
        Assert.Equal(1, await check.ExecuteScalarAsync<int>("SELECT count(*) FROM orders WHERE id=123"));
        if (state == "pending") Assert.Equal(1, await check.ExecuteScalarAsync<int>("SELECT count(*) FROM cloud_operational_outbox"));
    }

    [Fact]
    public async Task OperationalReset_PreservesIdentityHighWaterMark()
    {
        using (var c = _connectionFactory.CreateConnection())
            await c.ExecuteAsync("INSERT INTO orders(id,work_date,original_folder_name,relative_path,status,created_at,updated_at) VALUES(123,'2026-10-02','Old','Old','Scanned',datetime('now'),datetime('now'))");
        await _resetService.ResetDataAsync(ResetDataScope.OperationalOnly);
        using var next = _connectionFactory.CreateConnection();
        long id = await next.ExecuteScalarAsync<long>("INSERT INTO orders(work_date,original_folder_name,relative_path,status,created_at,updated_at) VALUES('2026-10-02','New','New','Scanned',datetime('now'),datetime('now')); SELECT last_insert_rowid()");
        Assert.True(id > 123);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restore_CloudBoundSourceOrTarget_RejectsBeforeReplacement(bool bindSource)
    {
        var backup = await _backupService.CreateBackupAsync(Path.Combine(_tempRoot, "restore-source.db"));
        if (bindSource)
        {
            using var c = new SqliteConnection($"Data Source={backup.BackupPath}"); c.Open();
            await c.ExecuteAsync("UPDATE cloud_sync_state SET stream_id='old-stream' WHERE id=1");
        }
        else
        {
            using var c = _connectionFactory.CreateConnection();
            await c.ExecuteAsync("UPDATE cloud_sync_state SET stream_id='old-stream' WHERE id=1");
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => _backupService.RestoreBackupAsync(backup.BackupPath));
        using var check = _connectionFactory.CreateConnection();
        Assert.Equal(bindSource ? 0 : 1, await check.ExecuteScalarAsync<int>("SELECT count(*) FROM cloud_sync_state WHERE stream_id='old-stream'"));
    }

}
