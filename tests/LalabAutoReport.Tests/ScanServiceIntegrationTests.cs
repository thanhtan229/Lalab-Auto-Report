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

public class ScanServiceIntegrationTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();

    private class TestSettingsRepo : LalabAutoReport.Core.Interfaces.ISettingsRepository
    {
        private readonly string _rootFolder;

        public TestSettingsRepo(string rootFolder)
        {
            _rootFolder = rootFolder;
        }

        public Task<AppSettings> GetSettingsAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AppSettings
            {
                RootFolder = _rootFolder,
                SupportedExtensions = new() { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp", ".heic" }
            });
        }

        public Task SaveSettingsAsync(AppSettings settings, System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ScanDate_ScenarioA_NormalJob_ShouldAutoMatch()
    {
        using var fixture = new TestFileSystemFixture();
        // Setup Date/Customer/Spec/
        // 13x18 in: 3 source images, retouch: 3 print images
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s2.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s3.jpg");

        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\p1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\p2.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\p3.jpg");

        var parser = new FolderStructureParser(_fileSystem);
        var resolver = new PrintFolderResolver(_fileSystem);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);
        var scanner = new ScanService(_fileSystem, parser, resolver, settingsRepo);

        var orders = await scanner.ScanDateAsync("2026-09-28");

        orders.Should().HaveCount(1);
        var order = orders[0];
        order.OriginalFolderName.Should().Be("Van An");
        order.Status.Should().Be(OrderStatus.Ready);
        order.Items.Should().HaveCount(1);

        var item = order.Items[0];
        item.SpecificationFolderName.Should().Be("13x18 in");
        item.SourceCount.Should().Be(3);
        item.PrintCount.Should().Be(3);
        item.MismatchCount.Should().Be(0);
        item.BillQuantity.Should().Be(3);
        item.QuantityResolutionMode.Should().Be(QuantityResolutionMode.UsePrint);
    }

    [Fact]
    public async Task ScanDate_ScenarioB_V2_OnlyPrintCountUsedForBilling()
    {
        using var fixture = new TestFileSystemFixture();
        // In V2: 13x18 in: 3 source images, retouch: 2 print images
        // V2 rule: only final print folder is counted, source is ignored for billing
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s2.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s3.jpg");

        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\p1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\p2.jpg");

        var parser = new FolderStructureParser(_fileSystem);
        var resolver = new PrintFolderResolver(_fileSystem);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);
        var scanner = new ScanService(_fileSystem, parser, resolver, settingsRepo);

        var orders = await scanner.ScanDateAsync("2026-09-28");

        orders.Should().HaveCount(1);
        var order = orders[0];
        order.Status.Should().Be(OrderStatus.Ready);

        var item = order.Items[0];
        item.SourceCount.Should().Be(3);
        item.PrintCount.Should().Be(2);
        item.BillQuantity.Should().Be(2);
        item.QuantityResolutionMode.Should().Be(QuantityResolutionMode.UsePrint);
    }

    [Fact]
    public async Task ScanDate_SourceCount_MustNotRecurseIntoChildFolders()
    {
        using var fixture = new TestFileSystemFixture();
        // 13x18 in: 2 source images directly in folder
        // retouch: 5 images
        // subchild: 10 images
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s2.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\r1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\r2.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\subchild\x1.jpg");

        var parser = new FolderStructureParser(_fileSystem);
        var resolver = new PrintFolderResolver(_fileSystem);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);
        var scanner = new ScanService(_fileSystem, parser, resolver, settingsRepo);

        var orders = await scanner.ScanDateAsync("2026-09-28");

        orders.Should().HaveCount(1);
        var item = orders[0].Items[0];
        item.SourceCount.Should().Be(2, "SourceCount must count files directly inside specification folder without recursing");
        item.PrintCount.Should().Be(1, "PrintCount must count files directly inside deepest leaf folder 'subchild'");
    }

    [Fact]
    public async Task ScanDate_MultiplePhysicalFolders_ShouldRemainSeparateOrders()
    {
        using var fixture = new TestFileSystemFixture();
        // Three folders on the same date: "Van An", "Anh An", "A.An"
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\s1.jpg");

        fixture.CreateFile(@"2026-09-28\Anh An\20x30\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Anh An\20x30\retouch\s1.jpg");

        fixture.CreateFile(@"2026-09-28\A.An\40x60 TG\s1.jpg");
        fixture.CreateFile(@"2026-09-28\A.An\40x60 TG\retouch\s1.jpg");

        var parser = new FolderStructureParser(_fileSystem);
        var resolver = new PrintFolderResolver(_fileSystem);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);
        var scanner = new ScanService(_fileSystem, parser, resolver, settingsRepo);

        var orders = await scanner.ScanDateAsync("2026-09-28");

        orders.Should().HaveCount(3, "Each physical folder must be an independent physical order");
        orders.Select(o => o.OriginalFolderName).Should().BeEquivalentTo(new[] { "Van An", "Anh An", "A.An" });
    }

    [Fact]
    public async Task DatabaseMigrator_ShouldInitializeSchema_AndPersistOrders()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "test.db");
        var connectionFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connectionFactory);

        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connectionFactory);
        var settingsRepo = new SqliteSettingsRepository(connectionFactory);

        // Save settings
        await settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = fixture.RootPath,
            SupportedExtensions = new() { ".jpg", ".png" }
        });

        var loadedSettings = await settingsRepo.GetSettingsAsync();
        loadedSettings.RootFolder.Should().Be(fixture.RootPath);
        loadedSettings.SupportedExtensions.Should().Contain(".jpg");

        // Scan and persist an order
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\s1.jpg");

        var parser = new FolderStructureParser(_fileSystem);
        var resolver = new PrintFolderResolver(_fileSystem);
        var scanner = new ScanService(_fileSystem, parser, resolver, settingsRepo, orderRepo);

        var orders = await scanner.ScanDateAsync("2026-09-28");
        orders.Should().HaveCount(1);
        orders[0].Id.Should().BeGreaterThan(0, "Order should be assigned a database ID");

        // Reload from repository
        var reloaded = await orderRepo.GetOrderByRelativePathAsync(orders[0].RelativePath);
        reloaded.Should().NotBeNull();
        reloaded!.OriginalFolderName.Should().Be("Van An");
        reloaded.Items.Should().HaveCount(1);
        reloaded.Items[0].SourceCount.Should().Be(1);
        reloaded.Items[0].PrintCount.Should().Be(1);
    }
}
