using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using LalabAutoReport.Infrastructure.Windows;
using Xunit;

namespace LalabAutoReport.Tests;

public class FolderPrintStatusTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();

    private class MockIdleDetector : IIdleDetectionService
    {
        public TimeSpan CurrentIdleTime { get; set; } = TimeSpan.FromMinutes(35);
        public TimeSpan GetIdleTime() => CurrentIdleTime;
    }

    private class TestSettingsRepo : ISettingsRepository
    {
        private AppSettings _settings;
        public TestSettingsRepo(AppSettings settings) => _settings = settings;

        public Task<AppSettings> GetSettingsAsync(System.Threading.CancellationToken ct = default) =>
            Task.FromResult(_settings);

        public Task SaveSettingsAsync(AppSettings settings, System.Threading.CancellationToken ct = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private async Task<(SqliteFolderPrintRepository printRepo,
                        SqliteOrderRepository orderRepo,
                        SqliteCustomerRepository custRepo,
                        SqliteProductRepository productRepo,
                        FolderVisualMarkerService visualService,
                        PrintStatusService printStatusService,
                        ScanService scanner,
                        FolderFingerprintService fingerprintService,
                        TestSettingsRepo settingsRepo)>
        CreateTestContextAsync(TestFileSystemFixture fixture)
    {
        string dbPath = Path.Combine(fixture.RootPath, $"test_{Guid.NewGuid():N}.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var printRepo = new SqliteFolderPrintRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var productRepo = new SqliteProductRepository(connFactory);

        var parser = new FolderStructureParser(_fileSystem, productRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(productRepo);
        var fingerprintService = new FolderFingerprintService(_fileSystem);

        var settings = new AppSettings
        {
            RootFolder = fixture.RootPath,
            SupportedExtensions = new() { ".jpg", ".jpeg", ".png" },
            EnableIdleScan = true,
            IdleThresholdMinutes = 30,
            IdleScanWindowDays = 7,
            AutoScanStartup = true,
            StartupIncludeYesterday = true
        };
        var settingsRepo = new TestSettingsRepo(settings);

        var visualService = new FolderVisualMarkerService();
        var printStatusService = new PrintStatusService(
            printRepo, visualService, orderRepo, settingsRepo);

        var billing = new BillingService(orderRepo, billRepo, productRepo, custRepo);
        var scanner = new ScanService(
            _fileSystem, parser, printResolver, settingsRepo,
            orderRepo, custResolver, specResolver, billRepo, billing,
            null, fingerprintService, printStatusService);

        return (printRepo, orderRepo, custRepo, productRepo, visualService, printStatusService, scanner, fingerprintService, settingsRepo);
    }

    [Fact]
    public async Task ToggleStatusAsync_ShouldToggleToPrinted_ThenBackToNotPrinted()
    {
        using var fixture = new TestFileSystemFixture();
        var (printRepo, orderRepo, _, _, visualService, printService, _, _, _) = await CreateTestContextAsync(fixture);

        string folderRelative = @"2026-09-30\Khach A";
        string folderFull = Path.Combine(fixture.RootPath, folderRelative);
        Directory.CreateDirectory(folderFull);
        fixture.CreateFile(Path.Combine(folderRelative, "sample.jpg"));

        // 1. Initial State: NotPrinted
        var initialStatus = await printService.GetStatusAsync(folderFull);
        initialStatus.Should().Be(PrintStatus.NotPrinted);

        // 2. First Toggle -> Should become Printed
        var result1 = await printService.ToggleStatusAsync(folderFull);
        result1.NewStatus.Should().Be(PrintStatus.Printed);
        result1.IsPrinted.Should().BeTrue();
        result1.IsSuccess.Should().BeTrue();

        // Verify marker file exists
        string markerPath = Path.Combine(folderFull, PrintStatusService.MarkerFileName);
        File.Exists(markerPath).Should().BeTrue();

        // Verify desktop.ini and ReadOnly attribute
        string iniPath = Path.Combine(folderFull, "desktop.ini");
        File.Exists(iniPath).Should().BeTrue();
        var dirInfo = new DirectoryInfo(folderFull);
        dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly).Should().BeTrue();

        // Verify DB record
        var record1 = await printRepo.GetByFolderPathAsync(folderFull);
        record1.Should().NotBeNull();
        record1!.Status.Should().Be(PrintStatus.Printed);

        // 3. Second Toggle -> Should become NotPrinted
        var result2 = await printService.ToggleStatusAsync(folderFull);
        result2.NewStatus.Should().Be(PrintStatus.NotPrinted);
        result2.IsPrinted.Should().BeFalse();
        result2.IsSuccess.Should().BeTrue();

        // Verify marker file is deleted
        File.Exists(markerPath).Should().BeFalse();

        // Verify desktop.ini is removed and ReadOnly attribute cleared
        File.Exists(iniPath).Should().BeFalse();
        dirInfo.Refresh();
        dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly).Should().BeFalse();

        // Verify DB record updated
        var record2 = await printRepo.GetByFolderPathAsync(folderFull);
        record2.Should().NotBeNull();
        record2!.Status.Should().Be(PrintStatus.NotPrinted);
    }

    [Fact]
    public async Task ToggleStatusAsync_WhenAssociatedOrderExists_ShouldSynchronizeOrderIsPrinted()
    {
        using var fixture = new TestFileSystemFixture();
        var (printRepo, orderRepo, custRepo, _, _, printService, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khach A" });
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        string orderRel = $@"{today}\Khach A";
        string folderFull = Path.Combine(fixture.RootPath, orderRel);

        fixture.CreateFile(Path.Combine(orderRel, @"13x18 in\img1.jpg"));

        // Scan order into DB
        var orders = await scanner.ScanDateAsync(today);
        orders.Should().HaveCount(1);
        orders[0].IsPrinted.Should().BeFalse();

        // Toggle to Printed
        var toggleRes = await printService.ToggleStatusAsync(folderFull);
        toggleRes.IsPrinted.Should().BeTrue();

        // Check SQLite order table
        var savedOrder = await orderRepo.GetOrderByRelativePathAsync(orderRel);
        savedOrder.Should().NotBeNull();
        savedOrder!.IsPrinted.Should().BeTrue();
        savedOrder.PrintedAt.Should().NotBeNull();

        // Toggle back to NotPrinted
        var untoggleRes = await printService.ToggleStatusAsync(folderFull);
        untoggleRes.IsPrinted.Should().BeFalse();

        // Check SQLite order table again
        var revertedOrder = await orderRepo.GetOrderByRelativePathAsync(orderRel);
        revertedOrder.Should().NotBeNull();
        revertedOrder!.IsPrinted.Should().BeFalse();
        revertedOrder.PrintedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_WhenDbHasNoRecord_ReconcilesFromMarkerFile()
    {
        using var fixture = new TestFileSystemFixture();
        var (printRepo, _, _, _, _, printService, _, _, _) = await CreateTestContextAsync(fixture);

        string folderRelative = @"2026-09-30\Khach Tu Do";
        string folderFull = Path.Combine(fixture.RootPath, folderRelative);
        Directory.CreateDirectory(folderFull);

        // Manually place .lalab-printed marker file
        string markerPath = Path.Combine(folderFull, PrintStatusService.MarkerFileName);
        File.WriteAllText(markerPath, $"{{\"printedAt\":\"{DateTime.UtcNow:O}\"}}");

        // Query status -> should reconcile and return Printed
        var status = await printService.GetStatusAsync(folderFull);
        status.Should().Be(PrintStatus.Printed);

        // Should also have saved record to SQLite DB
        var record = await printRepo.GetByFolderPathAsync(folderFull);
        record.Should().NotBeNull();
        record!.Status.Should().Be(PrintStatus.Printed);
    }

    [Fact]
    public async Task AutoScanCoordinator_SkipsPrintedFolders_DuringIdleScan()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, orderRepo, custRepo, productRepo, _, printService, scanner, fpService, settingsRepo) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khach A" });
        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khach B" });

        string today = DateTime.Today.ToString("yyyy-MM-dd");
        string folderA = $@"{today}\Khach A";
        string folderB = $@"{today}\Khach B";

        fixture.CreateFile(Path.Combine(folderA, @"13x18 in\img1.jpg"));
        fixture.CreateFile(Path.Combine(folderB, @"13x18 in\img2.jpg"));

        // Mark Khach A as Printed
        await printService.ToggleStatusAsync(Path.Combine(fixture.RootPath, folderA));

        var parser = new FolderStructureParser(_fileSystem, productRepo);
        var idleDetector = new MockIdleDetector { CurrentIdleTime = TimeSpan.FromMinutes(35) };
        var coordinator = new AutoScanCoordinator(
            scanner, settingsRepo, orderRepo, parser, _fileSystem, fpService, idleDetector, printService);

        // Run idle scan
        int updated = await coordinator.PerformIdleScanAsync();
        // Khach A is PRINTED -> skipped! Only Khach B should be scanned.
        updated.Should().Be(1);

        var orderA = await orderRepo.GetOrderByRelativePathAsync(folderA);
        orderA.Should().BeNull(); // Never scanned or created in DB by auto-scan

        var orderB = await orderRepo.GetOrderByRelativePathAsync(folderB);
        orderB.Should().NotBeNull();
        orderB!.Customer?.CanonicalName.Should().Be("Khach B");
    }

    [Fact]
    public async Task ManualScan_OnPrintedFolder_PreservesPrintedStatus()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, orderRepo, custRepo, _, _, printService, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khach A" });
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        string orderRel = $@"{today}\Khach A";
        string folderFull = Path.Combine(fixture.RootPath, orderRel);

        fixture.CreateFile(Path.Combine(orderRel, @"13x18 in\img1.jpg"));

        // Toggle to Printed before scanning
        await printService.ToggleStatusAsync(folderFull);

        // Perform explicit manual scan on order folder
        var order = await scanner.ScanOrderAsync(orderRel);
        order.Should().NotBeNull();
        order!.IsPrinted.Should().BeTrue();
        order.PrintedAt.Should().NotBeNull();

        // Perform date scan -> should also preserve IsPrinted
        var orders = await scanner.ScanDateAsync(today);
        orders.Should().HaveCount(1);
        orders[0].IsPrinted.Should().BeTrue();
    }

    [Fact]
    public void FolderVisualMarkerService_ClearOnFolderWithoutMarker_DoesNotThrow()
    {
        using var fixture = new TestFileSystemFixture();
        var service = new FolderVisualMarkerService();
        string testFolder = Path.Combine(fixture.RootPath, "normal_folder");
        Directory.CreateDirectory(testFolder);

        // Should not throw even when no desktop.ini or ReadOnly attributes exist
        var act = () => service.RemoveVisualMarker(testFolder);
        act.Should().NotThrow();
    }
}
