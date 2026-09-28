using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using Xunit;

namespace LalabAutoReport.Tests;

public class SmartScanAndSnapshotTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();

    private class TestSettingsRepo : LalabAutoReport.Core.Interfaces.ISettingsRepository
    {
        private readonly string _rootFolder;
        public TestSettingsRepo(string rootFolder) => _rootFolder = rootFolder;
        public Task<AppSettings> GetSettingsAsync(System.Threading.CancellationToken ct = default) =>
            Task.FromResult(new AppSettings { RootFolder = _rootFolder });
        public Task SaveSettingsAsync(AppSettings settings, System.Threading.CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task ScanMissingDays_ShouldOnlyScanUnscannedDates()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "missing_days.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);
        var parser = new FolderStructureParser(_fileSystem);
        var printResolver = new PrintFolderResolver(_fileSystem);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo);

        // Create 3 date folders on disk: 01, 02, 03
        fixture.CreateFile(@"2026-09-01\CustomerA\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-01\CustomerA\13x18 in\retouch\p1.jpg");

        fixture.CreateFile(@"2026-09-02\CustomerB\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-02\CustomerB\13x18 in\retouch\p1.jpg");

        fixture.CreateFile(@"2026-09-03\CustomerC\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-03\CustomerC\13x18 in\retouch\p1.jpg");

        // Scan only day 01 first
        var orders01 = await scanner.ScanDateAsync("2026-09-01");
        orders01.Should().HaveCount(1);

        // Missing days should only be 02 and 03
        var missing = await scanner.GetMissingScanDaysAsync(2026, 9);
        missing.Should().BeEquivalentTo(new[] { "2026-09-02", "2026-09-03" });

        // Run ScanMissingDays
        var scannedMissing = await scanner.ScanMissingDaysAsync(2026, 9);
        scannedMissing.Should().HaveCount(2);
        scannedMissing.Select(o => o.WorkDate).Should().BeEquivalentTo(new[] { "2026-09-02", "2026-09-03" });

        // Now there should be 0 missing days!
        var missingAfter = await scanner.GetMissingScanDaysAsync(2026, 9);
        missingAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanSnapshot_AuditHistory_PreservesPreviousObservations()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "snapshot_history.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);
        var parser = new FolderStructureParser(_fileSystem);
        var printResolver = new PrintFolderResolver(_fileSystem);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo);

        // Initial folder state: 3 source, 3 print
        string s1 = fixture.CreateFile(@"2026-09-28\CustomerA\13x18 in\s1.jpg");
        string s2 = fixture.CreateFile(@"2026-09-28\CustomerA\13x18 in\s2.jpg");
        string s3 = fixture.CreateFile(@"2026-09-28\CustomerA\13x18 in\s3.jpg");
        string p1 = fixture.CreateFile(@"2026-09-28\CustomerA\13x18 in\retouch\p1.jpg");
        string p2 = fixture.CreateFile(@"2026-09-28\CustomerA\13x18 in\retouch\p2.jpg");
        string p3 = fixture.CreateFile(@"2026-09-28\CustomerA\13x18 in\retouch\p3.jpg");

        // Scan 1
        var ordersFirst = await scanner.ScanDateAsync("2026-09-28");
        long orderId = ordersFirst[0].Id;

        // Later: 2 print files are deleted on disk!
        File.Delete(p2);
        File.Delete(p3);

        // Scan 2 (rescanning the order)
        var orderRescanned = await scanner.ScanOrderAsync(@"2026-09-28\CustomerA");
        orderRescanned.Should().NotBeNull();
        orderRescanned!.Items[0].PrintCount.Should().Be(1);

        // Check audit snapshots
        var history = await orderRepo.GetScanSnapshotsForOrderAsync(orderId);
        history.Should().HaveCount(2, "Previous scan snapshot must not be deleted or overwritten destructively");

        var latestSnapshot = history[0]; // ordered DESC
        var previousSnapshot = history[1];

        previousSnapshot.Items[0].PrintCount.Should().Be(3);
        latestSnapshot.Items[0].PrintCount.Should().Be(1);
    }

    [Fact]
    public async Task RescanOrder_ShouldNotMutateUnrelatedOrders()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "scoped_order.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);
        var parser = new FolderStructureParser(_fileSystem);
        var printResolver = new PrintFolderResolver(_fileSystem);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo);

        // Setup 2 orders
        fixture.CreateFile(@"2026-09-28\Order1\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Order1\13x18 in\retouch\p1.jpg");

        fixture.CreateFile(@"2026-09-28\Order2\20x30\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Order2\20x30\retouch\p1.jpg");

        var allOrders = await scanner.ScanDateAsync("2026-09-28");
        var order2OriginalScanAt = allOrders.Single(o => o.OriginalFolderName == "Order2").LastScanAt;

        // Small wait
        await Task.Delay(100);

        // Add file to Order1 only
        fixture.CreateFile(@"2026-09-28\Order1\13x18 in\retouch\p2.jpg");

        // Rescan ONLY Order1
        var order1Updated = await scanner.ScanOrderAsync(@"2026-09-28\Order1");
        order1Updated!.Items[0].PrintCount.Should().Be(2);

        // Check Order2 from database
        var order2Reloaded = await orderRepo.GetOrderByRelativePathAsync(@"2026-09-28\Order2");
        order2Reloaded!.LastScanAt.Should().Be(order2OriginalScanAt, "Order2 was not rescanned and must retain its original LastScanAt");
    }
}
