using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using Microsoft.Data.Sqlite;
using Dapper;
using Xunit;

namespace LalabAutoReport.Tests;

public class MultiRootFolderTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteRootFolderRepository _rootRepo;
    private readonly SqliteOrderRepository _orderRepo;

    public MultiRootFolderTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"lalab_multiroot_test_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _rootRepo = new SqliteRootFolderRepository(_connectionFactory);
        _orderRepo = new SqliteOrderRepository(_connectionFactory);
    }

    [Fact]
    public async Task ProductionIdentity_SameRelativePathInTwoRootsStaysSeparateAcrossRescans()
    {
        await _migrator.MigrateAsync();
        string folder = Path.Combine(Path.GetTempPath(), "lalab_roots_" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(folder, "A"), second = Path.Combine(folder, "B");
        string relative = Path.Combine("2026-10-02", "Customer", "13x18 in");
        try
        {
            Directory.CreateDirectory(Path.Combine(first, relative));
            Directory.CreateDirectory(Path.Combine(second, relative));
            File.WriteAllText(Path.Combine(first, relative, "1.jpg"), "");
            File.WriteAllText(Path.Combine(second, relative, "1.jpg"), "");
            File.WriteAllText(Path.Combine(second, relative, "2.jpg"), "");
            var rootA = await _rootRepo.InsertAsync(new RootFolder { Name = "A", FullPath = first, IsActive = true });
            var rootB = await _rootRepo.InsertAsync(new RootFolder { Name = "B", FullPath = second, IsActive = true });
            var settings = new SqliteSettingsRepository(_connectionFactory);
            await settings.SaveSettingsAsync(new AppSettings { RootFolder = first });
            var fs = new PhysicalFileSystemAdapter();
            var scanner = new ScanService(fs, new FolderStructureParser(fs), new PrintFolderResolver(fs),
                settings, _orderRepo, rootFolderRepository: _rootRepo);
            var orders = await scanner.ScanDateAsync("2026-10-02");
            Assert.Equal(2, orders.Count);
            Assert.Equal(2, orders.Select(o => o.Id).Distinct().Count());
            string orderPath = Path.Combine("2026-10-02", "Customer");
            var a = (await scanner.ScanOrderInRootAsync(orderPath, rootA))!;
            var b = (await scanner.ScanOrderInRootAsync(orderPath, rootB))!;
            Assert.NotEqual(a.Id, b.Id);
            Assert.Equal(1, a.Items.Single().PrintCount);
            Assert.Equal(2, b.Items.Single().PrintCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanSpecificationAsync(relative));
            await Assert.ThrowsAsync<InvalidOperationException>(() => _orderRepo.UpdateOrderPrintedStatusByRelativePathAsync(orderPath, true, DateTimeOffset.UtcNow));
            var printing = new PrintStatusService(new SqliteFolderPrintRepository(_connectionFactory), new QuietMarker(),
                _orderRepo, settings, rootFolderRepository: _rootRepo);
            await printing.ToggleStatusAsync(Path.Combine(second, orderPath));
            Assert.True((await _orderRepo.GetOrderByIdAsync(b.Id))!.IsPrinted);
            Assert.False((await _orderRepo.GetOrderByIdAsync(a.Id))!.IsPrinted);
            var faulty = new FailingEnumerationAdapter { FailedPath = Path.Combine(second, "2026-10-02") };
            var failingScanner = new ScanService(faulty, new FolderStructureParser(faulty), new PrintFolderResolver(faulty),
                settings, _orderRepo, rootFolderRepository: _rootRepo);
            await failingScanner.ScanDateAsync("2026-10-02");
            Assert.NotNull(await _orderRepo.GetOrderByIdAsync(b.Id));
            Assert.Equal(2, (await _orderRepo.GetOrdersByDateAsync("2026-10-02")).Count);
            var again = await scanner.ScanDateAsync("2026-10-02");
            Assert.Equal(orders.Select(o => o.Id).OrderBy(id => id), again.Select(o => o.Id).OrderBy(id => id));
            using var connection = _connectionFactory.CreateConnection();
            Assert.Empty(await connection.QueryAsync("PRAGMA foreign_key_check"));
            Assert.Equal("ok", await connection.QuerySingleAsync<string>("PRAGMA integrity_check"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private sealed class QuietMarker : IFolderVisualMarkerService
    {
        public bool HasVisualMarker(string path) => false;
        public VisualFolderColor GetVisualMarkerColor(string path) => VisualFolderColor.DefaultYellow;
        public bool ApplyVisualMarker(string path, VisualFolderColor color = VisualFolderColor.RedPrinted) => true;
        public bool RemoveVisualMarker(string path) => true;
        public void RefreshExplorer(string path) { }
        public string GetOrExtractPrintedFolderIconPath() => "";
        public string GetOrExtractPartialFolderIconPath() => "";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [Fact]
    public async Task Migration13_CreatesRootFoldersTable_AndSetsUpOrdersCompositeUnique()
    {
        // Act: Run migrations up to V13
        await _migrator.MigrateAsync();

        // Assert: Root folders table exists and can be queried
        var roots = await _rootRepo.GetAllAsync();
        Assert.NotNull(roots);

        // Can insert multiple roots
        long root1 = await _rootRepo.InsertAsync(new RootFolder
        {
            Name = "Kho 2025 (Lưu trữ)",
            FullPath = @"D:\ANH_2025",
            IsActive = false,
            IsDefault = false
        });

        long root2 = await _rootRepo.InsertAsync(new RootFolder
        {
            Name = "Kho 2026 (Hiện tại)",
            FullPath = @"E:\ANH_2026",
            IsActive = true,
            IsDefault = true
        });

        Assert.True(root1 > 0);
        Assert.True(root2 > 0);

        var activeRoots = await _rootRepo.GetActiveRootsAsync();
        Assert.Single(activeRoots);
        Assert.Equal("Kho 2026 (Hiện tại)", activeRoots[0].Name);

        var defaultRoot = await _rootRepo.GetDefaultRootAsync();
        Assert.NotNull(defaultRoot);
        Assert.Equal(root2, defaultRoot.Id);
    }

    [Fact]
    public async Task Migration13_WhenDatabaseHasExistingOrdersAndSnapshots_SucceedsWithoutForeignKeyViolation()
    {
        // 1. Run migrations up to V12 (before V13 table rebuild)
        await _migrator.MigrateToVersionAsync(12);

        // 2. Insert sample customer, order, scan_snapshot, order_item_scan referencing order
        using (var connection = _connectionFactory.CreateConnection())
        {
            long custId = await connection.QuerySingleAsync<long>(@"
                INSERT INTO customers (canonical_name, phone, note, created_at, updated_at)
                VALUES ('Khách Test', '0901234567', '', datetime('now'), datetime('now'));
                SELECT last_insert_rowid();
            ");

            long orderId = await connection.QuerySingleAsync<long>(@"
                INSERT INTO orders (work_date, customer_id, original_folder_name, relative_path, status, created_at, updated_at)
                VALUES ('2026-09-30', @CustId, 'Khách Test', '2026-09-30\Khach Test', 'Ready', datetime('now'), datetime('now'));
                SELECT last_insert_rowid();
            ", new { CustId = custId });

            long snapshotId = await connection.QuerySingleAsync<long>(@"
                INSERT INTO scan_snapshots (order_id, started_at, scan_scope, status, app_version)
                VALUES (@OrderId, datetime('now'), 'Date', 'Success', '1.0.0');
                SELECT last_insert_rowid();
            ", new { OrderId = orderId });

            await connection.ExecuteAsync(@"
                INSERT INTO order_item_scans (scan_snapshot_id, order_id, specification_folder_name, specification_relative_path, source_count, print_folder_status, scan_status)
                VALUES (@SnapshotId, @OrderId, '13x18 in', '13x18 in', 10, 'SingleLeaf', 'Valid');
            ", new { SnapshotId = snapshotId, OrderId = orderId });

            // Also insert customer_bill and customer_bill_orders referencing order
            long billId = await connection.QuerySingleAsync<long>(@"
                INSERT INTO customer_bills (bill_number, customer_id, customer_name_snapshot, period_start, period_end, status, created_at, updated_at)
                VALUES ('HD-TEST-001', @CustId, 'Khách Test', '2026-09-01', '2026-09-30', 'Draft', datetime('now'), datetime('now'));
                SELECT last_insert_rowid();
            ", new { CustId = custId });

            await connection.ExecuteAsync(@"
                INSERT INTO customer_bill_orders (bill_id, order_id, order_name_snapshot, order_date_snapshot, original_folder_name_snapshot)
                VALUES (@BillId, @OrderId, 'Đơn test', '2026-09-30', 'Khách Test');
            ", new { BillId = billId, OrderId = orderId });
        }

        // 3. Act: Run pending migrations (V13 to V15)
        // This will rebuild orders table and should succeed without SQLite Error 19
        await _migrator.MigrateAsync();

        // 4. Assert: Verify order still exists, root_folder_id is populated, and foreign keys are valid
        var order = await _orderRepo.GetOrderByIdAsync(1);
        Assert.NotNull(order);
        Assert.NotNull(order.RootFolderId);

        using (var connection = _connectionFactory.CreateConnection())
        {
            var violations = (await connection.QueryAsync<dynamic>("PRAGMA foreign_key_check;")).ToList();
            Assert.Empty(violations);
        }
    }

    [Fact]
    public async Task UpdatePathAsync_CanChangeToAnyNewPath_Immediately()
    {
        await _migrator.MigrateAsync();

        long rootId = await _rootRepo.InsertAsync(new RootFolder
        {
            Name = "Kho Chính",
            FullPath = @"D:\DATA_OLD",
            IsActive = true
        });

        // Act: update path to a completely new path (e.g. NAS or new drive)
        string newPath = @"\\NAS\PhotoPrint\DATA_NEW";
        await _rootRepo.UpdatePathAsync(rootId, newPath);

        // Assert
        var updated = await _rootRepo.GetByIdAsync(rootId);
        Assert.NotNull(updated);
        Assert.Equal(newPath, updated.FullPath);
    }

    [Fact]
    public async Task OrdersInDifferentRoots_CanShareSameRelativePath()
    {
        await _migrator.MigrateAsync();

        long root1 = await _rootRepo.InsertAsync(new RootFolder
        {
            Name = "Kho 1",
            FullPath = @"D:\KHO_1",
            IsActive = true
        });

        long root2 = await _rootRepo.InsertAsync(new RootFolder
        {
            Name = "Kho 2",
            FullPath = @"E:\KHO_2",
            IsActive = true
        });

        string relativePath = @"2026-09-30\Van An";

        // Order 1 in Root 1
        var order1 = new Order
        {
            WorkDate = "2026-09-30",
            OriginalFolderName = "Van An",
            RelativePath = relativePath,
            RootFolderId = root1,
            Status = OrderStatus.Ready
        };
        var snapshot1 = new ScanSnapshot { Scope = ScanScope.Date, Status = ScanStatus.Success };
        await _orderRepo.SaveOrderAsync(order1, snapshot1);

        // Order 2 in Root 2 with identical relative path
        var order2 = new Order
        {
            WorkDate = "2026-09-30",
            OriginalFolderName = "Van An",
            RelativePath = relativePath,
            RootFolderId = root2,
            Status = OrderStatus.Ready
        };
        var snapshot2 = new ScanSnapshot { Scope = ScanScope.Date, Status = ScanStatus.Success };
        await _orderRepo.SaveOrderAsync(order2, snapshot2);

        // Assert: Both orders exist independently and are retrieved by root
        var fetched1 = await _orderRepo.GetOrderByRootAndRelativePathAsync(root1, relativePath);
        var fetched2 = await _orderRepo.GetOrderByRootAndRelativePathAsync(root2, relativePath);

        Assert.NotNull(fetched1);
        Assert.NotNull(fetched2);
        Assert.NotEqual(fetched1.Id, fetched2.Id);
        Assert.Equal("Kho 1", fetched1.RootFolderName);
        Assert.Equal("Kho 2", fetched2.RootFolderName);
    }

    [Fact]
    public async Task DeleteAsync_PreventsDeletingRootFolder_WhenOrdersExist()
    {
        await _migrator.MigrateAsync();

        long rootId = await _rootRepo.InsertAsync(new RootFolder
        {
            Name = "Kho Co Don",
            FullPath = @"D:\KHO_CO_DON",
            IsActive = true
        });

        var order = new Order
        {
            WorkDate = "2026-09-30",
            OriginalFolderName = "Khach A",
            RelativePath = @"2026-09-30\Khach A",
            RootFolderId = rootId,
            Status = OrderStatus.Ready
        };
        await _orderRepo.SaveOrderAsync(order, new ScanSnapshot());

        // Act & Assert: Delete should throw InvalidOperationException to protect historical data
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _rootRepo.DeleteAsync(rootId));
        Assert.Contains("Không thể xóa", ex.Message);

        // Should allow deactivating instead
        await _rootRepo.SetActiveAsync(rootId, false);
        var updated = await _rootRepo.GetByIdAsync(rootId);
        Assert.False(updated!.IsActive);
    }

    [Fact]
    public async Task DataPurgeService_GetPurgePreviewAndExecute_DeletesOldOperationalData_PreservesCustomerMasterData()
    {
        await _migrator.MigrateAsync();

        var backupService = new DatabaseBackupService(_connectionFactory);
        var purgeService = new DataPurgeService(_connectionFactory, backupService);

        // 1. Insert Customer Master Data
        var customerRepo = new SqliteCustomerRepository(_connectionFactory);
        var customer = await customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Studio Ánh Sáng",
            Note = "VIP Customer"
        });

        // 2. Insert Old Order (2025-01-15, older than cutoff 2026-01-01)
        var oldOrder = new Order
        {
            WorkDate = "2025-01-15",
            OriginalFolderName = "Studio Ánh Sáng",
            RelativePath = @"2025-01-15\Studio Ánh Sáng",
            CustomerId = customer.Id,
            Status = OrderStatus.Billed
        };
        await _orderRepo.SaveOrderAsync(oldOrder, new ScanSnapshot());
        long oldOrderId = oldOrder.Id;

        // 3. Insert Recent Order (2026-09-30, newer than cutoff 2026-01-01)
        var recentOrder = new Order
        {
            WorkDate = "2026-09-30",
            OriginalFolderName = "Studio Ánh Sáng",
            RelativePath = @"2026-09-30\Studio Ánh Sáng",
            CustomerId = customer.Id,
            Status = OrderStatus.Billed
        };
        await _orderRepo.SaveOrderAsync(recentOrder, new ScanSnapshot());
        long recentOrderId = recentOrder.Id;

        // 4. Test Purge Preview
        DateTime cutoff = new DateTime(2026, 1, 1);
        var preview = await purgeService.GetPurgePreviewAsync(cutoff);
        Assert.Equal(1, preview.OrderCount);

        // 5. Execute Purge
        var result = await purgeService.PurgeOperationalDataBeforeDateAsync(cutoff);
        Assert.Equal(1, result.OrdersDeleted);
        Assert.True(File.Exists(result.BackupFilePath));

        // Cleanup the created backup file
        try { File.Delete(result.BackupFilePath); } catch { }

        // 6. Verify Old Order is deleted, Recent Order is preserved
        var remainingOld = await _orderRepo.GetOrderByIdAsync(oldOrderId);
        var remainingRecent = await _orderRepo.GetOrderByIdAsync(recentOrderId);
        Assert.Null(remainingOld);
        Assert.NotNull(remainingRecent);

        // 7. Verify Customer Master Data is completely intact!
        var preservedCustomer = await customerRepo.GetByIdAsync(customer.Id);
        Assert.NotNull(preservedCustomer);
        Assert.Equal("Studio Ánh Sáng", preservedCustomer.CanonicalName);
    }

    [Fact]
    public async Task ScanService_MultiRoot_ScansAllActiveRoots_AndIsolatesOrders()
    {
        await _migrator.MigrateAsync();

        string root1Dir = Path.Combine(Path.GetTempPath(), $"lalab_root1_{Guid.NewGuid():N}");
        string root2Dir = Path.Combine(Path.GetTempPath(), $"lalab_root2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root1Dir, @"2026-09-30\Khach A\13x18 in\retouch"));
        Directory.CreateDirectory(Path.Combine(root2Dir, @"2026-09-30\Khach B\15x21 in\retouch"));

        File.WriteAllText(Path.Combine(root1Dir, @"2026-09-30\Khach A\13x18 in\s1.jpg"), "fake");
        File.WriteAllText(Path.Combine(root1Dir, @"2026-09-30\Khach A\13x18 in\retouch\p1.jpg"), "fake");

        File.WriteAllText(Path.Combine(root2Dir, @"2026-09-30\Khach B\15x21 in\s2.jpg"), "fake");
        File.WriteAllText(Path.Combine(root2Dir, @"2026-09-30\Khach B\15x21 in\retouch\p2.jpg"), "fake");

        try
        {
            long root1Id = await _rootRepo.InsertAsync(new RootFolder
            {
                Name = "Kho 1",
                FullPath = root1Dir,
                IsActive = true
            });

            long root2Id = await _rootRepo.InsertAsync(new RootFolder
            {
                Name = "Kho 2",
                FullPath = root2Dir,
                IsActive = true
            });

            var fileSystem = new PhysicalFileSystemAdapter();
            var parser = new FolderStructureParser(fileSystem);
            var resolver = new PrintFolderResolver(fileSystem);
            var settingsRepo = new LalabAutoReport.Infrastructure.Data.SqliteSettingsRepository(_connectionFactory);

            var scanner = new ScanService(
                fileSystem: fileSystem,
                structureParser: parser,
                printFolderResolver: resolver,
                settingsRepository: settingsRepo,
                rootFolderRepository: _rootRepo,
                orderRepository: _orderRepo
            );

            // Act: Scan Date across active roots
            var orders = await scanner.ScanDateAsync("2026-09-30");

            // Assert
            Assert.Equal(2, orders.Count);

            var orderA = orders.FirstOrDefault(o => o.OriginalFolderName == "Khach A");
            var orderB = orders.FirstOrDefault(o => o.OriginalFolderName == "Khach B");

            Assert.NotNull(orderA);
            Assert.NotNull(orderB);

            Assert.Equal(root1Id, orderA.RootFolderId);
            Assert.Equal("Kho 1", orderA.RootFolderName);

            Assert.Equal(root2Id, orderB.RootFolderId);
            Assert.Equal("Kho 2", orderB.RootFolderName);
        }
        finally
        {
            try { Directory.Delete(root1Dir, true); } catch { }
            try { Directory.Delete(root2Dir, true); } catch { }
        }
    }
}
