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

public class FinalPrintFolderAndBillingTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();

    private class TestSettingsRepo : ISettingsRepository
    {
        private readonly string _rootFolder;
        public TestSettingsRepo(string rootFolder) => _rootFolder = rootFolder;
        public Task<AppSettings> GetSettingsAsync(System.Threading.CancellationToken ct = default) =>
            Task.FromResult(new AppSettings
            {
                RootFolder = _rootFolder,
                SupportedExtensions = new() { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp", ".heic" }
            });
        public Task SaveSettingsAsync(AppSettings settings, System.Threading.CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private async Task<(SqliteConnectionFactory connFactory,
                        SqliteOrderRepository orderRepo,
                        SqliteBillRepository billRepo,
                        SqliteProductRepository productRepo,
                        SqliteCustomerRepository custRepo,
                        ScanService scanner,
                        BillingService billing)>
        CreateTestContextAsync(TestFileSystemFixture fixture)
    {
        string dbPath = Path.Combine(fixture.RootPath, $"test_{Guid.NewGuid():N}.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var productRepo = new SqliteProductRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        var parser = new FolderStructureParser(_fileSystem, productRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(productRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var billing = new BillingService(orderRepo, billRepo, productRepo, custRepo);

        return (connFactory, orderRepo, billRepo, productRepo, custRepo, scanner, billing);
    }

    // =========================================================================
    // SCENARIOS 7-12: FINAL PRINT FOLDER RESOLUTION & BILLING QUANTITY SOLE SOURCE
    // =========================================================================

    [Fact]
    public async Task Scenario07_DirectProductImageLeaf_FolderItselfResolvedAsFinalPrintFolder()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Customer A" });

        // Direct images in product folder, no subdirectories
        fixture.CreateFile(@"2026-09-29\Customer A\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Customer A\13x18 in\img2.jpg");
        fixture.CreateFile(@"2026-09-29\Customer A\13x18 in\img3.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        orders.Should().HaveCount(1);
        var item = orders[0].Items.Single();

        item.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        item.SelectedPrintFolderRelativePath.Should().Be(@"2026-09-29\Customer A\13x18 in");
        item.PrintCount.Should().Be(3);
        item.BillQuantity.Should().Be(3);
    }

    [Fact]
    public async Task Scenario08_ChainLeaf_ResolvesToDeepestLeafFolder()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Customer B" });

        // Product -> In -> Final
        fixture.CreateFile(@"2026-09-29\Customer B\13x18 in\raw.jpg");
        fixture.CreateFile(@"2026-09-29\Customer B\13x18 in\In\draft.jpg");
        fixture.CreateFile(@"2026-09-29\Customer B\13x18 in\In\Final\f1.jpg");
        fixture.CreateFile(@"2026-09-29\Customer B\13x18 in\In\Final\f2.jpg");
        fixture.CreateFile(@"2026-09-29\Customer B\13x18 in\In\Final\f3.jpg");
        fixture.CreateFile(@"2026-09-29\Customer B\13x18 in\In\Final\f4.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        orders.Should().HaveCount(1);
        var item = orders[0].Items.Single();

        item.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        item.SelectedPrintFolderRelativePath.Should().Be(@"2026-09-29\Customer B\13x18 in\In\Final");
        item.PrintCount.Should().Be(4);
        item.BillQuantity.Should().Be(4);
    }

    [Fact]
    public async Task Scenario09_MultipleCompetingLeaves_SetsStatusNeedsReview()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Customer C" });

        // 2 competing leaf folders: In and Retouch
        fixture.CreateFile(@"2026-09-29\Customer C\13x18 in\In\p1.jpg");
        fixture.CreateFile(@"2026-09-29\Customer C\13x18 in\Retouch\p2.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        orders.Should().HaveCount(1);
        var order = orders[0];
        order.Status.Should().Be(OrderStatus.NeedsReview);

        var item = order.Items.Single();
        item.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.AmbiguousPrintFolder);
        item.SelectedPrintFolderRelativePath.Should().BeNull();
        item.CandidatePrintFolderRelativePaths.Should().HaveCount(2);
    }

    [Fact]
    public async Task Scenario10_PersistedUserSelection_ReusedOnRescan()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, orderRepo, _, _, custRepo, scanner, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Customer D" });

        // 2 competing leaf folders initially
        fixture.CreateFile(@"2026-09-29\Customer D\13x18 in\BranchA\p1.jpg");
        fixture.CreateFile(@"2026-09-29\Customer D\13x18 in\BranchB\p2.jpg");

        var orders1 = await scanner.ScanDateAsync("2026-09-29");
        var item1 = orders1[0].Items.Single();
        item1.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.AmbiguousPrintFolder);

        // User explicitly selects BranchB
        string chosenFolder = @"2026-09-29\Customer D\13x18 in\BranchB";
        await orderRepo.UpdateOrderItemPrintFolderAsync(item1.Id, chosenFolder, 1);

        // Rescan date
        var orders2 = await scanner.ScanDateAsync("2026-09-29");
        var item2 = orders2[0].Items.Single();

        item2.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        item2.SelectedPrintFolderRelativePath.Should().Be(chosenFolder);
        item2.PrintCount.Should().Be(1);
    }

    [Fact]
    public async Task Scenario11_ExclusionOfSourceCountFromBilling_SourceCountMismatchDoesNotBlock()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Customer E" });

        // Source = 50 files, Retouch (Print) = 48 files
        for (int i = 1; i <= 50; i++)
            fixture.CreateFile($@"2026-09-29\Customer E\13x18 in\src_{i}.jpg");
        for (int i = 1; i <= 48; i++)
            fixture.CreateFile($@"2026-09-29\Customer E\13x18 in\retouch\prn_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var order = orders[0];
        order.Status.Should().Be(OrderStatus.Ready); // NOT NeedsReview!

        var item = order.Items.Single();
        item.SourceCount.Should().Be(50);
        item.PrintCount.Should().Be(48);
        item.BillQuantity.Should().Be(48); // Print folder is the sole source!
        item.MismatchCount.Should().Be(0);

        var bill = await billing.CalculateBillForOrderAsync(order.Id);
        bill.Subtotal.Should().Be(48 * 5000);
        bill.Lines.Single().BillQuantity.Should().Be(48);
    }

    [Fact]
    public async Task Scenario12_EmptyPrintFolderOrNoImages_ReturnsNoPrintFolderAndErrorState()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Customer F" });

        // Only text file, no valid images
        fixture.CreateFile(@"2026-09-29\Customer F\13x18 in\notes.txt");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var order = orders[0];
        order.Status.Should().Be(OrderStatus.NeedsReview);

        var item = order.Items.Single();
        item.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.NoPrintFolder);
        item.PrintCount.Should().BeNull();
        item.BillQuantity.Should().BeNull();
    }

    // =========================================================================
    // SCENARIOS 43-45: PHOTO PRINT BILLING (FILE_COUNT)
    // =========================================================================

    [Fact]
    public async Task Scenario43_PhotoPrint_FileCountPricing_15ImagesAt5000Equals75000()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Viet" });

        for (int i = 1; i <= 15; i++)
            fixture.CreateFile($@"2026-09-29\Studio Viet\13x18 in\retouch\img_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var bill = await billing.CalculateBillForOrderAsync(orders[0].Id);

        bill.Subtotal.Should().Be(75000);
        var line = bill.Lines.Single();
        line.BillQuantity.Should().Be(15);
        line.UnitPrice.Should().Be(5000);
        line.LineTotal.Should().Be(75000);
    }

    [Fact]
    public async Task Scenario44_PhotoPrint_NonImageFilesIgnoredDuringCounting()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Mai" });

        // 10 valid images + 5 unsupported files (.txt, .psd, .doc, .zip, .exe)
        for (int i = 1; i <= 10; i++)
            fixture.CreateFile($@"2026-09-29\Studio Mai\13x18 in\final\p_{i}.jpg");

        fixture.CreateFile(@"2026-09-29\Studio Mai\13x18 in\final\readme.txt");
        fixture.CreateFile(@"2026-09-29\Studio Mai\13x18 in\final\master.psd");
        fixture.CreateFile(@"2026-09-29\Studio Mai\13x18 in\final\contract.doc");
        fixture.CreateFile(@"2026-09-29\Studio Mai\13x18 in\final\backup.zip");
        fixture.CreateFile(@"2026-09-29\Studio Mai\13x18 in\final\run.exe");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var bill = await billing.CalculateBillForOrderAsync(orders[0].Id);

        bill.Lines.Single().BillQuantity.Should().Be(10);
        bill.Subtotal.Should().Be(50000);
    }

    [Fact]
    public async Task Scenario45_MultiplePhotoPrintItemsInOneOrder_SummedAccurately()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Hoa" });

        // Item 1: 13x18 in (5,000) x 20 = 100,000
        for (int i = 1; i <= 20; i++)
            fixture.CreateFile($@"2026-09-29\Studio Hoa\13x18 in\p_{i}.jpg");

        // Item 2: 20x30 (15,000) x 10 = 150,000
        for (int i = 1; i <= 10; i++)
            fixture.CreateFile($@"2026-09-29\Studio Hoa\20x30\p_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var bill = await billing.CalculateBillForOrderAsync(orders[0].Id);

        bill.Lines.Should().HaveCount(2);
        bill.Subtotal.Should().Be(250000);
    }

    // =========================================================================
    // SCENARIOS 46-52: ALBUM BILLING (ALBUM_BASE_PLUS_EXTRA)
    // =========================================================================

    [Fact]
    public async Task Scenario46_Album_BaseSheetsExactly_10FilesEquals400000()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Wedding Studio" });

        for (int i = 1; i <= 10; i++)
            fixture.CreateFile($@"2026-09-29\Wedding Studio\Album 20x20\sheet_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var bill = await billing.CalculateBillForOrderAsync(orders[0].Id);

        bill.Subtotal.Should().Be(400000);
        var line = bill.Lines.Single();
        line.BillQuantity.Should().Be(1);
        line.SheetCount.Should().Be(10);
        line.IncludedSheetsSnapshot.Should().Be(10);
        line.ExtraSheetCount.Should().Be(0);
        line.BasePriceSnapshot.Should().Be(400000);
        line.LineTotal.Should().Be(400000);
    }

    [Fact]
    public async Task Scenario47_Album_ExtraSheets_13FilesEquals460000()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Wedding Studio 2" });

        for (int i = 1; i <= 13; i++)
            fixture.CreateFile($@"2026-09-29\Wedding Studio 2\Album 20x20\sheet_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var bill = await billing.CalculateBillForOrderAsync(orders[0].Id);

        bill.Subtotal.Should().Be(460000);
        var line = bill.Lines.Single();
        line.BillQuantity.Should().Be(1);
        line.SheetCount.Should().Be(13);
        line.IncludedSheetsSnapshot.Should().Be(10);
        line.ExtraSheetCount.Should().Be(3);
        line.BasePriceSnapshot.Should().Be(400000);
        line.ExtraSheetPriceSnapshot.Should().Be(20000);
        line.LineTotal.Should().Be(460000);
    }

    [Fact]
    public async Task Scenario48_Album_FewerSheetsThanBase_8FilesEquals400000WithWarning()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Wedding Studio 3" });

        for (int i = 1; i <= 8; i++)
            fixture.CreateFile($@"2026-09-29\Wedding Studio 3\Album 20x20\sheet_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var item = orders[0].Items.Single();

        // Informational warning for album under base sheets
        item.ScanStatus.Should().Be(ScanStatus.Warning);
        item.ErrorMessage.Should().Contain("dưới số trang/tờ tiêu chuẩn");

        var bill = await billing.CalculateBillForOrderAsync(orders[0].Id);

        bill.Subtotal.Should().Be(400000);
        var line = bill.Lines.Single();
        line.BillQuantity.Should().Be(1);
        line.SheetCount.Should().Be(8);
        line.ExtraSheetCount.Should().Be(0);
        line.LineTotal.Should().Be(400000);
    }

    [Fact]
    public async Task Scenario49_Album_SheetCountIsDirectFileCount_NoCoverDeduction()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Wedding Studio 4" });

        // 11 files = 11 sheets (no cover deduction: NOT 9 sheets) -> 1 extra sheet!
        for (int i = 1; i <= 11; i++)
            fixture.CreateFile($@"2026-09-29\Wedding Studio 4\Album 20x20\sheet_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var bill = await billing.CalculateBillForOrderAsync(orders[0].Id);

        var line = bill.Lines.Single();
        line.SheetCount.Should().Be(11);
        line.ExtraSheetCount.Should().Be(1);
        line.LineTotal.Should().Be(420000); // 400k + 1 * 20k
    }

    [Fact]
    public async Task Scenario50_TwoAlbumsOfSameSizeInOneOrder_BilledAsTwoSeparateJobs()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Hai Album" });

        // Album 1: 10 sheets (400,000đ)
        for (int i = 1; i <= 10; i++)
            fixture.CreateFile($@"2026-09-29\Studio Hai Album\Album 20x20 - 1\sheet_{i}.jpg");

        // Album 2: 12 sheets (400,000đ + 2 * 20,000đ = 440,000đ)
        for (int i = 1; i <= 12; i++)
            fixture.CreateFile($@"2026-09-29\Studio Hai Album\Album 20x20 - 2\sheet_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        orders.Should().HaveCount(1);
        var order = orders[0];
        order.Items.Should().HaveCount(2);

        var bill = await billing.CalculateBillForOrderAsync(order.Id);
        bill.Lines.Should().HaveCount(2);

        // Subtotal = 400,000 + 440,000 = 840,000 VND
        bill.Subtotal.Should().Be(840000);

        var line1 = bill.Lines.Single(l => l.FinalPrintFolderPath!.Contains("Album 20x20 - 1"));
        line1.SheetCount.Should().Be(10);
        line1.ExtraSheetCount.Should().Be(0);
        line1.LineTotal.Should().Be(400000);

        var line2 = bill.Lines.Single(l => l.FinalPrintFolderPath!.Contains("Album 20x20 - 2"));
        line2.SheetCount.Should().Be(12);
        line2.ExtraSheetCount.Should().Be(2);
        line2.LineTotal.Should().Be(440000);
    }

    [Fact]
    public async Task Scenario51_OrderWithBothPhotoPrintsAndAlbums_CalculatesAndSnapshotsAccurately()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Tong Hop" });

        // Item 1: Photo print 13x18 in (5,000) x 20 = 100,000 VND
        for (int i = 1; i <= 20; i++)
            fixture.CreateFile($@"2026-09-29\Studio Tong Hop\13x18 in\p_{i}.jpg");

        // Item 2: Album 20x20 with 11 sheets = 400,000 + 20,000 = 420,000 VND
        for (int i = 1; i <= 11; i++)
            fixture.CreateFile($@"2026-09-29\Studio Tong Hop\Album 20x20\sheet_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var bill = await billing.CalculateBillForOrderAsync(orders[0].Id);

        bill.Lines.Should().HaveCount(2);
        bill.Subtotal.Should().Be(520000); // 100k + 420k

        var photoLine = bill.Lines.Single(l => l.BillingMethodSnapshot == BillingMethod.FileCount);
        photoLine.BillQuantity.Should().Be(20);
        photoLine.LineTotal.Should().Be(100000);

        var albumLine = bill.Lines.Single(l => l.BillingMethodSnapshot == BillingMethod.AlbumBasePlusExtra);
        albumLine.BillQuantity.Should().Be(1);
        albumLine.SheetCount.Should().Be(11);
        albumLine.ExtraSheetCount.Should().Be(1);
        albumLine.LineTotal.Should().Be(420000);
    }

    [Fact]
    public async Task Scenario52_HistoricalLockedBillSnapshotImmutability_PriceChangesDoNotAffectLockedBill()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, orderRepo, billRepo, productRepo, custRepo, scanner, billing) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Khoa" });

        // Create 10 prints of 13x18 in (originally 5,000đ = 50,000đ)
        for (int i = 1; i <= 10; i++)
            fixture.CreateFile($@"2026-09-29\Studio Khoa\13x18 in\p_{i}.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var order = orders[0];

        var bill = await billing.CalculateBillForOrderAsync(order.Id);
        bill.Subtotal.Should().Be(50000);

        // Lock the bill
        bill.Status = OrderStatus.Locked;
        bill.LockedAt = DateTimeOffset.UtcNow;
        await billRepo.SaveBillAsync(bill);
        await orderRepo.UpdateOrderStatusAsync(order.Id, OrderStatus.Locked);

        // Change price of 13x18 in from 5,000đ to 99,000đ
        var spec = (await productRepo.GetAllAsync()).First(s => s.CanonicalName == "13x18 in");
        spec.UnitPrice = 99000;
        await productRepo.UpdateSpecificationAsync(spec);

        // Rescan date
        await scanner.ScanDateAsync("2026-09-29");

        // Reload locked bill from DB
        var reloadedBill = await billRepo.GetBillByOrderIdAsync(order.Id);
        reloadedBill.Should().NotBeNull();
        reloadedBill!.Status.Should().Be(OrderStatus.Locked);
        reloadedBill.Subtotal.Should().Be(50000, "Historical locked bill total must remain completely immutable!");
        reloadedBill.Lines.Single().UnitPrice.Should().Be(5000);
        reloadedBill.Lines.Single().LineTotal.Should().Be(50000);
    }
}
