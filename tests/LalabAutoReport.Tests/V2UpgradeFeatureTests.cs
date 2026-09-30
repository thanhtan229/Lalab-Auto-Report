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

public class V2UpgradeFeatureTests
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

    [Theory]
    [InlineData(8, 400000, 0)]   // 8 sheets (< 10 included): base price 400k, 0 extra
    [InlineData(10, 400000, 0)]  // 10 sheets (equal included): base price 400k, 0 extra
    [InlineData(11, 420000, 1)]  // 11 sheets (no cover subtracted): base 400k + 1 * 20k = 420k
    [InlineData(13, 460000, 3)]  // 13 sheets: base 400k + 3 * 20k = 460k
    public async Task AlbumPricingMatrix_CalculatesCorrectly(int sheetCount, long expectedTotal, int expectedExtraSheets)
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, $"album_matrix_{sheetCount}.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Anh An Studio" }, "Anh An");

        // Seed Album 20x20 files directly in final print folder
        for (int i = 1; i <= sheetCount; i++)
        {
            fixture.CreateFile($@"2026-09-29\Anh An\Album 20x20\retouch\sheet_{i:D2}.jpg");
        }

        var parser = new FolderStructureParser(_fileSystem, specRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        orders.Should().HaveCount(1);
        var order = orders[0];
        order.Items.Should().HaveCount(1);

        var item = order.Items[0];
        item.PrintCount.Should().Be(sheetCount);

        var billing = new BillingService(orderRepo, billRepo, specRepo, custRepo);
        var bill = await billing.CalculateBillForOrderAsync(order.Id);

        bill.Should().NotBeNull();
        bill.Subtotal.Should().Be(expectedTotal);
        bill.Lines.Should().HaveCount(1);

        var line = bill.Lines[0];
        line.ProductNameSnapshot.Should().Be("Album 20x20");
        line.BillingMethodSnapshot.Should().Be(BillingMethod.AlbumBasePlusExtra);
        line.BillQuantity.Should().Be(1); // 1 physical album
        line.SheetCount.Should().Be(sheetCount);
        line.IncludedSheetsSnapshot.Should().Be(10);
        line.ExtraSheetCount.Should().Be(expectedExtraSheets);
        line.BasePriceSnapshot.Should().Be(400000);
        line.ExtraSheetPriceSnapshot.Should().Be(20000);
        line.LineTotal.Should().Be(expectedTotal);
    }

    [Fact]
    public async Task MultipleAlbums_InSameOrder_AggregateCorrectly()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "multi_album.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Kim Studio" });

        // Album 1 (10 sheets = 400,000 VND)
        for (int i = 1; i <= 10; i++)
        {
            fixture.CreateFile($@"2026-09-29\Kim Studio\Album 20x20\sheet_{i:D2}.jpg");
        }

        // Album 2 (Album 25x25 - 13 sheets = 500k base + 3 * 25k extra = 575,000 VND)
        var album25 = await specRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "Album 25x25",
            Category = ProductCategory.Album,
            BillingMethod = BillingMethod.AlbumBasePlusExtra,
            IncludedSheets = 10,
            BasePrice = 500000,
            ExtraSheetPrice = 25000
        }, "Album 25x25");

        for (int i = 1; i <= 13; i++)
        {
            fixture.CreateFile($@"2026-09-29\Kim Studio\Album 25x25\sheet_{i:D2}.jpg");
        }

        var parser = new FolderStructureParser(_fileSystem, specRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        orders.Should().HaveCount(1);
        var order = orders[0];
        order.Items.Should().HaveCount(2);

        var billing = new BillingService(orderRepo, billRepo, specRepo, custRepo);
        var bill = await billing.CalculateBillForOrderAsync(order.Id);

        bill.Lines.Should().HaveCount(2);
        // Album 1 = 400,000; Album 2 = 500,000 + 3*25,000 = 575,000 -> Total = 975,000 VND
        bill.Subtotal.Should().Be(400000 + 575000);
    }

    [Fact]
    public async Task KimStudio_MultiAlbum_Bo2_Bo3_AllResolveToAlbum20x20_AndCalculateCorrectly()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "kim_studio_multi.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Kim Studio" }, "Kim Studio");

        var allSpecs = await specRepo.GetAllAsync();
        var album20 = allSpecs.First(s => s.CanonicalName == "Album 20x20");
        album20.BasePrice = 400000;
        album20.ExtraSheetPrice = 20000;
        album20.IncludedSheets = 10;
        await specRepo.UpdateSpecificationAsync(album20);

        // Album 1: Album 20x20/retouch (10 files)
        for (int i = 1; i <= 10; i++)
        {
            fixture.CreateFile($@"2026-09-29\Kim Studio\Album 20x20\retouch\sheet_chuan_{i:D3}.jpg");
        }

        // Album 2: Album 20x20 - Bo 2/in (14 files)
        for (int i = 1; i <= 14; i++)
        {
            fixture.CreateFile($@"2026-09-29\Kim Studio\Album 20x20 - Bo 2\in\sheet_vuot_{i:D3}.jpg");
        }

        // Album 3: Album 20x20 - Bo 3/final (8 files)
        for (int i = 1; i <= 8; i++)
        {
            fixture.CreateFile($@"2026-09-29\Kim Studio\Album 20x20 - Bo 3\final\sheet_thieu_{i:D3}.jpg");
        }

        var parser = new FolderStructureParser(_fileSystem, specRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        orders.Should().HaveCount(1);
        var order = orders[0];
        order.Items.Should().HaveCount(3);

        // All 3 items must be resolved to Album 20x20
        order.Items.Should().AllSatisfy(item =>
        {
            item.PrintSpecificationId.Should().Be(album20.Id);
            item.PrintSpecification.Should().NotBeNull();
            item.PrintSpecification!.CanonicalName.Should().Be("Album 20x20");
        });

        var item1 = order.Items.First(i => i.SpecificationFolderName == "Album 20x20");
        item1.PrintableFileCount.Should().Be(10);
        item1.BillQuantity.Should().Be(1);

        var item2 = order.Items.First(i => i.SpecificationFolderName == "Album 20x20 - Bo 2");
        item2.PrintableFileCount.Should().Be(14);
        item2.BillQuantity.Should().Be(1);

        var item3 = order.Items.First(i => i.SpecificationFolderName == "Album 20x20 - Bo 3");
        item3.PrintableFileCount.Should().Be(8);
        item3.BillQuantity.Should().Be(1);

        var billing = new BillingService(orderRepo, billRepo, specRepo, custRepo);
        var bill = await billing.CalculateBillForOrderAsync(order.Id);

        bill.Lines.Should().HaveCount(3);
        // Album 1 = 400,000 (10 sheets); Album 2 = 400,000 + 4*20,000 = 480,000 (14 sheets); Album 3 = 400,000 (8 sheets)
        // Subtotal = 400k + 480k + 400k = 1,280,000 VND
        bill.Subtotal.Should().Be(1280000);
    }

    [Fact]
    public async Task Hierarchy_CaseB_ExplicitOrders_ScannedAsDistinctOrders()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "case_b_explicit.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Anh An" });

        // Structure:
        // 2026-09-29/
        //   Anh An/
        //     Don 01/
        //       13x18 in/ (5 prints)
        //       Album 20x20/ (10 sheets)
        //     Don 02/
        //       13x18 in/ (8 prints)

        for (int i = 1; i <= 5; i++)
            fixture.CreateFile($@"2026-09-29\Anh An\Don 01\13x18 in\img_{i}.jpg");

        for (int i = 1; i <= 10; i++)
            fixture.CreateFile($@"2026-09-29\Anh An\Don 01\Album 20x20\sheet_{i}.jpg");

        for (int i = 1; i <= 8; i++)
            fixture.CreateFile($@"2026-09-29\Anh An\Don 02\13x18 in\img_{i}.jpg");

        var parser = new FolderStructureParser(_fileSystem, specRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        // Must discover 2 distinct orders
        orders.Should().HaveCount(2);

        var don01 = orders.FirstOrDefault(o => o.OrderName == "Don 01");
        don01.Should().NotBeNull();
        don01!.OrderKind.Should().Be(OrderKind.Explicit);
        don01.Items.Should().HaveCount(2);

        var don02 = orders.FirstOrDefault(o => o.OrderName == "Don 02");
        don02.Should().NotBeNull();
        don02!.OrderKind.Should().Be(OrderKind.Explicit);
        don02.Items.Should().HaveCount(1);
        don02.Items[0].PrintCount.Should().Be(8);
    }

    [Fact]
    public async Task Hierarchy_CaseC_MixedStructure_HandledGracefully()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "case_c_mixed.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio B" });

        // Mixed:
        // Studio B/
        //   13x18 in/ (direct product -> implicit order)
        //   Don Bo Sung/ (explicit sub-order)
        //     15x21 in/
        fixture.CreateFile(@"2026-09-29\Studio B\13x18 in\p1.jpg");
        fixture.CreateFile(@"2026-09-29\Studio B\Don Bo Sung\13x18 in\p2.jpg");

        var parser = new FolderStructureParser(_fileSystem, specRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        // In Case C: direct items grouped into implicit order, explicit folder into explicit order
        orders.Should().HaveCount(2);
        orders.Should().Contain(o => o.OrderKind == OrderKind.Implicit);
        orders.Should().Contain(o => o.OrderKind == OrderKind.Explicit && o.OrderName == "Don Bo Sung");
    }

    [Fact]
    public async Task HistoricalV1_LockedBill_RemainsIntact_AfterV2Migration()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "v1_migration_historical.db");
        var connFactory = new SqliteConnectionFactory(dbPath);

        // Run migrations up to V2
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        // Simulate a historical locked bill created in V1
        var cust = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Historical Cust" });
        var spec = await specRepo.GetByIdAsync(1); // 13x18 in (5000 vnd)

        // Create V1 order
        var order = new Order
        {
            WorkDate = "2026-09-01",
            CustomerId = cust.Id,
            OriginalFolderName = "Historical Cust",
            RelativePath = @"2026-09-01\Historical Cust",
            Status = OrderStatus.Locked,
            OrderKind = OrderKind.Implicit,
            OrderName = "Đơn mặc định"
        };
        order.Items.Add(new OrderItemScan
        {
            SpecificationFolderName = "13x18 in",
            SpecificationRelativePath = @"2026-09-01\Historical Cust\13x18 in",
            PrintSpecificationId = spec!.Id,
            SourceCount = 10,
            PrintCount = 10,
            BillQuantity = 10,
            QuantityResolutionMode = QuantityResolutionMode.AutoMatch
        });
        var snapshot = new ScanSnapshot { OrderId = 0, StartedAt = DateTimeOffset.UtcNow, Scope = ScanScope.Order, Status = ScanStatus.Success };
        await orderRepo.SaveOrderAsync(order, snapshot);

        // Save historical bill
        var historicalBill = new Bill
        {
            OrderId = order.Id,
            CustomerId = cust.Id,
            Status = OrderStatus.Locked,
            LockedAt = DateTimeOffset.UtcNow.AddDays(-10),
            Subtotal = 50000,
            Lines =
            {
                new BillLine
                {
                    PrintSpecificationId = spec.Id,
                    ProductNameSnapshot = "13x18 in",
                    BillingMethodSnapshot = BillingMethod.FileCount,
                    BillQuantity = 10,
                    UnitPrice = 5000,
                    LineTotal = 50000,
                    QuantityResolutionMode = QuantityResolutionMode.AutoMatch,
                    SourceCount = 10,
                    PrintCount = 10,
                    SourceScanSnapshotId = snapshot.Id
                }
            }
        };
        await billRepo.SaveBillAsync(historicalBill);

        // Verify retrieval under V2
        var loadedBill = await billRepo.GetBillByOrderIdAsync(order.Id);
        loadedBill.Should().NotBeNull();
        loadedBill!.Status.Should().Be(OrderStatus.Locked);
        loadedBill.Subtotal.Should().Be(50000);
        loadedBill.Lines.Should().HaveCount(1);
        loadedBill.Lines[0].BillQuantity.Should().Be(10);
        loadedBill.Lines[0].UnitPrice.Should().Be(5000);
        loadedBill.Lines[0].LineTotal.Should().Be(50000);
        loadedBill.Lines[0].ProductNameSnapshot.Should().Be("13x18 in");
    }
}
