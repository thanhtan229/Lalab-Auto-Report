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
using Xunit;

namespace LalabAutoReport.Tests;

public class AutoScanAndFingerprintTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();

    private class MockIdleDetector : IIdleDetectionService
    {
        public TimeSpan CurrentIdleTime { get; set; } = TimeSpan.Zero;
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

    private async Task<(SqliteOrderRepository orderRepo,
                        SqliteCustomerRepository custRepo,
                        SqliteProductRepository productRepo,
                        ScanService scanner,
                        FolderFingerprintService fingerprintService,
                        TestSettingsRepo settingsRepo)>
        CreateTestContextAsync(TestFileSystemFixture fixture, AppSettings? settings = null)
    {
        string dbPath = Path.Combine(fixture.RootPath, $"test_{Guid.NewGuid():N}.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var productRepo = new SqliteProductRepository(connFactory);

        var parser = new FolderStructureParser(_fileSystem, productRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(productRepo);
        var fingerprintService = new FolderFingerprintService(_fileSystem);

        var currentSettings = settings ?? new AppSettings
        {
            RootFolder = fixture.RootPath,
            SupportedExtensions = new() { ".jpg", ".jpeg", ".png" },
            EnableIdleScan = true,
            IdleThresholdMinutes = 30,
            IdleScanWindowDays = 7,
            AutoScanStartup = true,
            StartupIncludeYesterday = true
        };
        var settingsRepo = new TestSettingsRepo(currentSettings);

        var billing = new BillingService(orderRepo, billRepo, productRepo, custRepo);
        var scanner = new ScanService(
            _fileSystem, parser, printResolver, settingsRepo,
            orderRepo, custResolver, specResolver, billRepo, billing,
            null, fingerprintService);

        return (orderRepo, custRepo, productRepo, scanner, fingerprintService, settingsRepo);
    }

    [Fact]
    public void FolderFingerprint_IdenticalFolder_ProducesMatchingFingerprint()
    {
        using var fixture = new TestFileSystemFixture();
        var fpService = new FolderFingerprintService(_fileSystem);

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img2.jpg");

        var fp1 = fpService.ComputeOrderFingerprint(fixture.RootPath, @"2026-09-29\Van An");
        var fp2 = fpService.ComputeOrderFingerprint(fixture.RootPath, @"2026-09-29\Van An");

        fp1.FileCount.Should().Be(2);
        fpService.HasChanged(fp1.Value, fp2).Should().BeFalse();
    }

    [Fact]
    public void FolderFingerprint_AddingFileInDeepNestedSubfolder_DetectsChange()
    {
        using var fixture = new TestFileSystemFixture();
        var fpService = new FolderFingerprintService(_fileSystem);

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img1.jpg");
        var fp1 = fpService.ComputeOrderFingerprint(fixture.RootPath, @"2026-09-29\Van An");

        // Add a deeper retouch file inside sua\sua lai\
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\sua\sua lai\retouched.jpg");
        var fp2 = fpService.ComputeOrderFingerprint(fixture.RootPath, @"2026-09-29\Van An");

        fp2.FileCount.Should().Be(2);
        fp2.DirectoryCount.Should().BeGreaterThan(fp1.DirectoryCount);
        fpService.HasChanged(fp1.Value, fp2).Should().BeTrue();
    }

    [Fact]
    public void FolderFingerprint_DeletingFile_DetectsChange()
    {
        using var fixture = new TestFileSystemFixture();
        var fpService = new FolderFingerprintService(_fileSystem);

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img2.jpg");
        var fp1 = fpService.ComputeOrderFingerprint(fixture.RootPath, @"2026-09-29\Van An");

        // Delete a file
        File.Delete(Path.Combine(fixture.RootPath, @"2026-09-29\Van An\13x18 in\img2.jpg"));
        var fp2 = fpService.ComputeOrderFingerprint(fixture.RootPath, @"2026-09-29\Van An");

        fp2.FileCount.Should().Be(1);
        fpService.HasChanged(fp1.Value, fp2).Should().BeTrue();
    }

    [Fact]
    public async Task AutoScanCoordinator_PerformsIdleScan_AndUpdatesFingerprintInDatabase()
    {
        using var fixture = new TestFileSystemFixture();
        var (orderRepo, custRepo, productRepo, scanner, fpService, settingsRepo) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        string today = DateTime.Today.ToString("yyyy-MM-dd");
        fixture.CreateFile($@"{today}\Van An\13x18 in\img1.jpg");

        var parser = new FolderStructureParser(_fileSystem, productRepo);
        var idleDetector = new MockIdleDetector { CurrentIdleTime = TimeSpan.FromMinutes(35) };

        var coordinator = new AutoScanCoordinator(
            scanner, settingsRepo, orderRepo, parser, _fileSystem, fpService, idleDetector);

        // Run first idle scan cycle
        int updated = await coordinator.PerformIdleScanAsync();
        updated.Should().Be(1);

        var savedOrder = await orderRepo.GetOrderByRelativePathAsync($@"{today}\Van An");
        savedOrder.Should().NotBeNull();
        savedOrder!.Fingerprint.Should().NotBeNullOrWhiteSpace();

        // Run second idle scan cycle with no disk changes -> should skip!
        int secondRun = await coordinator.PerformIdleScanAsync();
        secondRun.Should().Be(0);

        // Add a new file -> should detect change and update
        fixture.CreateFile($@"{today}\Van An\13x18 in\sua\new.jpg");
        int thirdRun = await coordinator.PerformIdleScanAsync();
        thirdRun.Should().Be(1);

        var reloadedOrder = await orderRepo.GetOrderByRelativePathAsync($@"{today}\Van An");
        reloadedOrder!.Fingerprint.Should().NotBe(savedOrder.Fingerprint);
    }

    [Fact]
    public async Task AutoScanCoordinator_SkipsBilledAndLockedOrders_NeverAltersThem()
    {
        using var fixture = new TestFileSystemFixture();
        var (orderRepo, custRepo, productRepo, scanner, fpService, settingsRepo) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        string today = DateTime.Today.ToString("yyyy-MM-dd");
        fixture.CreateFile($@"{today}\Van An\13x18 in\img1.jpg");

        // Initial scan
        var orders = await scanner.ScanDateAsync(today);
        var order = orders.Single();
        order.Status = OrderStatus.Billed;
        await orderRepo.UpdateOrderStatusAsync(order.Id, OrderStatus.Billed);

        // Add a new file outside on disk
        fixture.CreateFile($@"{today}\Van An\13x18 in\sua\new.jpg");

        var parser = new FolderStructureParser(_fileSystem, productRepo);
        var idleDetector = new MockIdleDetector { CurrentIdleTime = TimeSpan.FromMinutes(35) };
        var coordinator = new AutoScanCoordinator(
            scanner, settingsRepo, orderRepo, parser, _fileSystem, fpService, idleDetector);

        // Perform idle scan
        int updated = await coordinator.PerformIdleScanAsync();
        updated.Should().Be(0); // Billed order must be skipped completely

        var unchangedOrder = await orderRepo.GetOrderByIdAsync(order.Id);
        unchangedOrder!.Status.Should().Be(OrderStatus.Billed);
    }

    [Fact]
    public async Task AutoScanCoordinator_StartupScan_ChecksTodayAndYesterday()
    {
        using var fixture = new TestFileSystemFixture();
        var (orderRepo, custRepo, productRepo, scanner, fpService, settingsRepo) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        string today = DateTime.Today.ToString("yyyy-MM-dd");
        string yesterday = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");

        fixture.CreateFile($@"{today}\Van An\13x18 in\img1.jpg");
        fixture.CreateFile($@"{yesterday}\Van An\13x18 in\img1.jpg");

        var parser = new FolderStructureParser(_fileSystem, productRepo);
        var idleDetector = new MockIdleDetector();
        var coordinator = new AutoScanCoordinator(
            scanner, settingsRepo, orderRepo, parser, _fileSystem, fpService, idleDetector);

        int updated = await coordinator.PerformStartupScanAsync();
        updated.Should().Be(2); // Scans both today and yesterday
    }
}
