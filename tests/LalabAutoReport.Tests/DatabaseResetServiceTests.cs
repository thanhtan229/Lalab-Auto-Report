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
}
