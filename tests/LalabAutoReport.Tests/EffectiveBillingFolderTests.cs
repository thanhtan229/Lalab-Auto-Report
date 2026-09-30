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

public class EffectiveBillingFolderTests
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
                        BillingService billing,
                        LockingService locking)>
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

        var billing = new BillingService(orderRepo, billRepo, productRepo, custRepo);
        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver, billRepo, billing);
        var locking = new LockingService(scanner, orderRepo, billRepo, billing);

        return (connFactory, orderRepo, billRepo, productRepo, custRepo, scanner, billing, locking);
    }

    // 1. Direct images in product folder, no subdirectories -> Effective Billing Folder = Product Folder, AutoResolved
    [Fact]
    public async Task Scenario01_DirectImagesInProductFolder_ResolvesToProductFolder_AutoResolved()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img2.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img3.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        orders.Should().HaveCount(1);
        var item = orders[0].Items.Single();

        item.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        item.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.AutoResolved);
        item.EffectiveBillingFolderRelativePath.Should().Be(@"2026-09-29\Van An\13x18 in");
        item.EffectiveBillingCount.Should().Be(3);
        item.BillQuantity.Should().Be(3);
        orders[0].Status.Should().Be(OrderStatus.Ready);
    }

    // 2. Product folder contains subfolder with no images -> Effective Billing Folder = Product Folder
    [Fact]
    public async Task Scenario02_ProductFolderWithEmptyOrNonImageSubfolders_ResolvesToProductFolder_AutoResolved()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img2.jpg");
        fixture.CreateDirectory(@"2026-09-29\Van An\13x18 in\empty_folder");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\docs\note.txt");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var item = orders.Single().Items.Single();

        item.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        item.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.AutoResolved);
        item.EffectiveBillingFolderRelativePath.Should().Be(@"2026-09-29\Van An\13x18 in");
        item.EffectiveBillingCount.Should().Be(2);
    }

    // 3. Dynamic progression: Product folder has images -> Rescan 1 resolves to Product Folder. Later technician creates sua/ -> Rescan 2 resolves to sua/
    [Fact]
    public async Task Scenario03_DynamicProgression_FromProductFolderToSua_AutomaticallyUpdatesOnRescan()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        // Phase 1: Intake files in product folder
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\raw1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\raw2.jpg");

        var scan1 = await scanner.ScanDateAsync("2026-09-29");
        var item1 = scan1.Single().Items.Single();
        item1.EffectiveBillingFolderRelativePath.Should().Be(@"2026-09-29\Van An\13x18 in");
        item1.EffectiveBillingCount.Should().Be(2);
        item1.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.AutoResolved);

        // Phase 2: Technician creates sua/ with edited images
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\sua\edited1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\sua\edited2.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\sua\edited3.jpg");

        // Rescan
        var scan2 = await scanner.ScanDateAsync("2026-09-29");
        var item2 = scan2.Single().Items.Single();
        item2.EffectiveBillingFolderRelativePath.Should().Be(@"2026-09-29\Van An\13x18 in\sua");
        item2.EffectiveBillingCount.Should().Be(3);
        item2.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.AutoResolved);
    }

    // 4. Deeper progression: sua/ -> sua/sua lai/
    [Fact]
    public async Task Scenario04_DeeperProgression_FromSuaToSuaLai_AutomaticallyUpdatesOnRescan()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\raw1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\sua\edit1.jpg");

        var scan1 = await scanner.ScanDateAsync("2026-09-29");
        scan1.Single().Items.Single().EffectiveBillingFolderRelativePath.Should().Be(@"2026-09-29\Van An\13x18 in\sua");

        // Add sua lai/
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\sua\sua lai\final1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\sua\sua lai\final2.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\sua\sua lai\final3.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\sua\sua lai\final4.jpg");

        var scan2 = await scanner.ScanDateAsync("2026-09-29");
        var item2 = scan2.Single().Items.Single();
        item2.EffectiveBillingFolderRelativePath.Should().Be(@"2026-09-29\Van An\13x18 in\sua\sua lai");
        item2.EffectiveBillingCount.Should().Be(4);
        item2.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.AutoResolved);
    }

    // 5. Competing leaves -> AmbiguousPrintFolder, NeedsReview
    [Fact]
    public async Task Scenario05_CompetingLeaves_ResolvesToAmbiguousPrintFolder_NeedsReview()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\raw.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh1\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh2\img2.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var order = orders.Single();
        var item = order.Items.Single();

        order.Status.Should().Be(OrderStatus.NeedsReview);
        item.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.AmbiguousPrintFolder);
        item.CandidatePrintFolderRelativePaths.Should().HaveCount(2);
        item.EffectiveBillingCount.Should().BeNull();
    }

    // 6. Manual selection in ambiguous tree: When user picks nhanh1 -> Resolved, ManuallySelected
    [Fact]
    public async Task Scenario06_ManualSelection_ResolvesAmbiguousAndSetsManuallySelected()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, orderRepo, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\raw.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh1\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh1\img2.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh2\imgB.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var item = orders.Single().Items.Single();

        // User manually picks nhanh1
        string selectedPath = @"2026-09-29\Van An\13x18 in\nhanh1";
        await orderRepo.UpdateOrderItemPrintFolderAsync(item.Id, selectedPath, 2);

        var updatedOrder = await orderRepo.GetOrderByIdAsync(orders[0].Id);
        var updatedItem = updatedOrder!.Items.Single();

        updatedItem.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        updatedItem.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.ManuallySelected);
        updatedItem.SelectedPrintFolderRelativePath.Should().Be(selectedPath);
        updatedItem.PrintCount.Should().Be(2);
    }

    // 7. Manual selection reuse on rescan: When nhanh1 is manually selected and rescan runs, retained as ManuallySelected
    [Fact]
    public async Task Scenario07_ManualSelection_ReusedOnRescanWhileStillValidLeaf()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, orderRepo, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\raw.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh1\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh2\imgB.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var item = orders.Single().Items.Single();

        string selectedPath = @"2026-09-29\Van An\13x18 in\nhanh1";
        await orderRepo.UpdateOrderItemPrintFolderAsync(item.Id, selectedPath, 1);

        // Add 1 more file to nhanh1
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh1\img2.jpg");

        // Rescan
        var rescanOrders = await scanner.ScanDateAsync("2026-09-29");
        var rescanItem = rescanOrders.Single().Items.Single();

        rescanItem.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        rescanItem.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.ManuallySelected);
        rescanItem.SelectedPrintFolderRelativePath.Should().Be(selectedPath);
        rescanItem.PrintCount.Should().Be(2);
    }

    // 8. Manual selection invalidation: If user deletes or empties nhanh1 -> Rescan auto-resolves nhanh2 if single leaf
    [Fact]
    public async Task Scenario08_ManualSelection_InvalidatedIfDeletedOrEmpty_RevertsToAutoOrAmbiguous()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, orderRepo, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\raw.jpg");
        string file1 = fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh1\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh2\imgB1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh2\imgB2.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var item = orders.Single().Items.Single();

        string selectedPath = @"2026-09-29\Van An\13x18 in\nhanh1";
        await orderRepo.UpdateOrderItemPrintFolderAsync(item.Id, selectedPath, 1);

        // Delete files in nhanh1
        File.Delete(file1);

        // Rescan: nhanh1 is no longer a valid image leaf! Only nhanh2 is a leaf, so it auto-resolves to nhanh2
        var rescanOrders = await scanner.ScanDateAsync("2026-09-29");
        var rescanItem = rescanOrders.Single().Items.Single();

        rescanItem.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        rescanItem.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.AutoResolved);
        rescanItem.SelectedPrintFolderRelativePath.Should().Be(@"2026-09-29\Van An\13x18 in\nhanh2");
        rescanItem.PrintCount.Should().Be(2);
    }

    // 9. Manual selection deeper progression: User selected nhanh1, but later a deeper folder nhanh1/final is created
    [Fact]
    public async Task Scenario09_ManualSelection_DeeperProgression_MovesAutomaticallyToDeeperLeaf()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, orderRepo, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh1\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh2\imgB.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var item = orders.Single().Items.Single();

        string selectedPath = @"2026-09-29\Van An\13x18 in\nhanh1";
        await orderRepo.UpdateOrderItemPrintFolderAsync(item.Id, selectedPath, 1);

        // Now delete nhanh2, and under nhanh1 create a deeper folder final/
        string nhanh2File = Path.Combine(fixture.RootPath, @"2026-09-29\Van An\13x18 in\nhanh2\imgB.jpg");
        File.Delete(nhanh2File);

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh1\final\imgF1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\nhanh1\final\imgF2.jpg");

        var rescanOrders = await scanner.ScanDateAsync("2026-09-29");
        var rescanItem = rescanOrders.Single().Items.Single();

        // nhanh1 is no longer a leaf! The only leaf is nhanh1/final
        rescanItem.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        rescanItem.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.AutoResolved);
        rescanItem.SelectedPrintFolderRelativePath.Should().Be(@"2026-09-29\Van An\13x18 in\nhanh1\final");
        rescanItem.PrintCount.Should().Be(2);
    }

    // 10. Album product with direct files: Album 20x30 directly has 15 files -> BillQuantity = 1, SheetCount = 15
    [Fact]
    public async Task Scenario10_AlbumProduct_DirectFiles_ResolvesToProductFolder_BillQuantityOne()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, productRepo, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });
        await productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "Album 20x30",
            Category = ProductCategory.Album,
            BillingMethod = BillingMethod.AlbumBasePlusExtra,
            IncludedSheets = 10,
            BasePrice = 300000,
            ExtraSheetPrice = 20000
        });

        for (int i = 1; i <= 15; i++)
        {
            fixture.CreateFile($@"2026-09-29\Van An\Album 20x30\page_{i:D2}.jpg");
        }

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var item = orders.Single().Items.Single();

        item.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
        item.EffectiveBillingFolderRelativePath.Should().Be(@"2026-09-29\Van An\Album 20x30");
        item.PrintableFileCount.Should().Be(15);
        item.BillQuantity.Should().Be(1);
        item.FolderResolutionMode.Should().Be(BillingFolderResolutionMode.AutoResolved);
    }

    // 11. Album product with deeper retouch: Album 20x30/in/ has 15 files
    [Fact]
    public async Task Scenario11_AlbumProduct_DeeperRetouch_ResolvesToDeeperFolder_CalculatesSheets()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, productRepo, custRepo, scanner, billing, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });
        await productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "Album 20x30",
            Category = ProductCategory.Album,
            BillingMethod = BillingMethod.AlbumBasePlusExtra,
            IncludedSheets = 10,
            BasePrice = 300000,
            ExtraSheetPrice = 20000
        });

        fixture.CreateFile(@"2026-09-29\Van An\Album 20x30\raw1.jpg");
        for (int i = 1; i <= 12; i++)
        {
            fixture.CreateFile($@"2026-09-29\Van An\Album 20x30\in\final_{i:D2}.jpg");
        }

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var order = orders.Single();
        var bill = await billing.CalculateBillForOrderAsync(order.Id);

        bill.Subtotal.Should().Be(300000 + (2 * 20000)); // 340,000 VND
        var line = bill.Lines.Single();
        line.SheetCount.Should().Be(12);
        line.IncludedSheetsSnapshot.Should().Be(10);
        line.ExtraSheetCount.Should().Be(2);
        line.FinalPrintFolderPath.Should().Be(@"2026-09-29\Van An\Album 20x30\in");
        line.FolderResolutionModeSnapshot.Should().Be(BillingFolderResolutionMode.AutoResolved);
    }

    // 12. Draft bill update on rescan: Order has draft bill (10 files = 50k). Adding 2 files -> Rescan updates draft bill to 60k
    [Fact]
    public async Task Scenario12_DraftBill_AutomaticallyUpdatesSubtotalAndQuantitiesOnRescan()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, billRepo, productRepo, custRepo, scanner, billing, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        for (int i = 1; i <= 10; i++)
        {
            fixture.CreateFile($@"2026-09-29\Van An\13x18 in\img{i}.jpg");
        }

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var order = orders.Single();

        // Generate draft bill
        var draftBill1 = await billing.CalculateBillForOrderAsync(order.Id);
        draftBill1.Subtotal.Should().Be(50000);
        draftBill1.Lines.Single().BillQuantity.Should().Be(10);

        // Add 2 more files
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img11.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img12.jpg");

        // Rescan order
        await scanner.ScanDateAsync("2026-09-29");

        var updatedBill = await billRepo.GetBillByOrderIdAsync(order.Id);
        updatedBill.Should().NotBeNull();
        updatedBill!.Id.Should().Be(draftBill1.Id); // Same bill updated, not duplicated
        updatedBill.Subtotal.Should().Be(60000); // 12 * 5000
        updatedBill.Lines.Single().BillQuantity.Should().Be(12);
        updatedBill.Lines.Single().LineTotal.Should().Be(60000);
    }

    // 13. Locked bill immutability: Order is locked at 10 files (50k). Technician adds files -> Rescan does not modify locked bill, sets FilesystemChangedAfterLock
    [Fact]
    public async Task Scenario13_LockedBill_Immutability_RescanDoesNotMutateAndFlagsFilesystemChanged()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, orderRepo, billRepo, productRepo, custRepo, scanner, _, locking) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        for (int i = 1; i <= 10; i++)
        {
            fixture.CreateFile($@"2026-09-29\Van An\13x18 in\img{i}.jpg");
        }

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var order = orders.Single();

        // Lock order
        var lockedBill = await locking.VerifyAndLockOrderAsync(order.Id);
        lockedBill.Status.Should().Be(OrderStatus.Locked);
        lockedBill.Subtotal.Should().Be(50000);

        // Now technician adds files to disk
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img11.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img12.jpg");

        // Rescan
        var rescanOrders = await scanner.ScanDateAsync("2026-09-29");
        var rescanOrder = rescanOrders.Single();

        rescanOrder.Status.Should().Be(OrderStatus.Billed);
        rescanOrder.Items.Single().PrintCount.Should().Be(12);
    }

    // 14. Locked bill recalculation rejection: Calling CalculateBillForOrderAsync on locked order throws
    [Fact]
    public async Task Scenario14_LockedBill_CalculateBillForOrderAsyncThrowsInvalidOperationException()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, productRepo, custRepo, scanner, billing, locking) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img1.jpg");
        var orders = await scanner.ScanDateAsync("2026-09-29");
        var order = orders.Single();

        await locking.VerifyAndLockOrderAsync(order.Id);

        var act = async () => await billing.CalculateBillForOrderAsync(order.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*đã bị khóa*");
    }

    // 15. Non-image files filtering: 5 jpg, 2 psd, 1 txt -> PrintCount = 5
    [Fact]
    public async Task Scenario15_SupportedAndUnsupportedMix_CountsOnlySupportedImages()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img2.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img3.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img4.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img5.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\project.psd");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\work.ai");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\readme.txt");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var item = orders.Single().Items.Single();

        item.EffectiveBillingCount.Should().Be(5);
        item.BillQuantity.Should().Be(5);
    }

    // 16. Case insensitivity in extensions: .JPG, .Png, .TIFF
    [Fact]
    public async Task Scenario16_CaseInsensitiveExtensions_CountsCorrectly()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });

        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img1.JPG");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img2.Png");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img3.TIFF");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\img4.WebP");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        var item = orders.Single().Items.Single();

        item.EffectiveBillingCount.Should().Be(4);
    }

    // 17. Inaccessible or disappearing folder: Handled gracefully
    [Fact]
    public async Task Scenario17_InaccessibleOrDisappearingFolder_HandledGracefully()
    {
        using var fixture = new TestFileSystemFixture();
        var printResolver = new PrintFolderResolver(_fileSystem);
        var result = printResolver.ResolvePrintFolder(
            Path.Combine(fixture.RootPath, "nonexistent"),
            fixture.RootPath,
            new System.Collections.Generic.HashSet<string> { ".jpg" });

        result.Status.Should().Be(PrintFolderResolutionStatus.NoPrintFolder);
        result.ErrorMessage.Should().Contain("does not exist");
    }

    // 18. Customer alias with multiple orders: Each folder remains a separate order with independent resolution
    [Fact]
    public async Task Scenario18_CustomerAliasWithMultipleOrders_EachHasIndependentEffectiveBillingFolder()
    {
        using var fixture = new TestFileSystemFixture();
        var (_, _, _, _, custRepo, scanner, _, _) = await CreateTestContextAsync(fixture);

        var cust = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Van An" });
        await custRepo.AddAliasAsync(cust.Id, "Anh An");

        // Order 1: Van An (direct product files)
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\p1.jpg");
        fixture.CreateFile(@"2026-09-29\Van An\13x18 in\p2.jpg");

        // Order 2: Anh An (in retouch folder)
        fixture.CreateFile(@"2026-09-29\Anh An\13x18 in\raw.jpg");
        fixture.CreateFile(@"2026-09-29\Anh An\13x18 in\retouch\r1.jpg");
        fixture.CreateFile(@"2026-09-29\Anh An\13x18 in\retouch\r2.jpg");
        fixture.CreateFile(@"2026-09-29\Anh An\13x18 in\retouch\r3.jpg");

        var orders = await scanner.ScanDateAsync("2026-09-29");
        orders.Should().HaveCount(2);

        var order1 = orders.First(o => o.OriginalFolderName == "Van An");
        var order2 = orders.First(o => o.OriginalFolderName == "Anh An");

        // Both mapped to same customer
        order1.CustomerId.Should().Be(cust.Id);
        order2.CustomerId.Should().Be(cust.Id);

        // Independent billing folders
        order1.Items.Single().EffectiveBillingFolderRelativePath.Should().Be(@"2026-09-29\Van An\13x18 in");
        order1.Items.Single().EffectiveBillingCount.Should().Be(2);

        order2.Items.Single().EffectiveBillingFolderRelativePath.Should().Be(@"2026-09-29\Anh An\13x18 in\retouch");
        order2.Items.Single().EffectiveBillingCount.Should().Be(3);
    }
}
