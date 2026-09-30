using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using LalabAutoReport.Infrastructure.Reporting;
using Xunit;

namespace LalabAutoReport.Tests;

public class GuestBillingTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteOrderRepository _orderRepo;
    private readonly SqliteBillRepository _billRepo;
    private readonly SqliteCustomerBillRepository _customerBillRepo;
    private readonly SqliteCustomerRepository _customerRepo;
    private readonly SqliteProductRepository _productRepo;
    private readonly SqliteSettingsRepository _settingsRepo;
    private readonly PhysicalFileSystemAdapter _fileSystem;
    private readonly FolderStructureParser _structureParser;
    private readonly PrintFolderResolver _folderResolver;
    private readonly CustomerResolver _customerResolver;
    private readonly PrintSpecificationResolver _specResolver;
    private readonly ScanService _scanService;
    private readonly CustomerBillingService _customerBillingService;
    private readonly WpfJpegBillExporter _jpegExporter;

    public GuestBillingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_GuestBillTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _dbPath = Path.Combine(_tempRoot, "test_guest_billing.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _orderRepo = new SqliteOrderRepository(_connectionFactory);
        _billRepo = new SqliteBillRepository(_connectionFactory);
        _customerBillRepo = new SqliteCustomerBillRepository(_connectionFactory);
        _customerRepo = new SqliteCustomerRepository(_connectionFactory);
        _productRepo = new SqliteProductRepository(_connectionFactory);
        _settingsRepo = new SqliteSettingsRepository(_connectionFactory);

        _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "_BILLS"),
            SupportedExtensions = new() { ".jpg", ".jpeg", ".png", ".tif" }
        }).GetAwaiter().GetResult();

        _fileSystem = new PhysicalFileSystemAdapter();
        _structureParser = new FolderStructureParser(_fileSystem);
        _folderResolver = new PrintFolderResolver(_fileSystem);
        _customerResolver = new CustomerResolver(_customerRepo);
        _specResolver = new PrintSpecificationResolver(_productRepo);

        _scanService = new ScanService(
            _fileSystem,
            _structureParser,
            _folderResolver,
            _settingsRepo,
            _orderRepo,
            _customerResolver,
            _specResolver,
            _billRepo
        );

        _customerBillingService = new CustomerBillingService(
            _customerRepo,
            _orderRepo,
            _customerBillRepo,
            _productRepo,
            _scanService,
            _fileSystem,
            _structureParser,
            _folderResolver,
            _settingsRepo,
            _specResolver,
            _customerResolver
        );

        _jpegExporter = new WpfJpegBillExporter(_settingsRepo);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch { }
    }

    private void CreateDummyFiles(string folderPath, int count)
    {
        Directory.CreateDirectory(folderPath);
        for (int i = 1; i <= count; i++)
        {
            File.WriteAllText(Path.Combine(folderPath, $"img_{i:D3}.jpg"), "dummy image content");
        }
    }

    [Fact]
    public async Task BuildGuestBillDraft_CreatesGuestBillWithNoCustomerRecord_AndNullCustomerId()
    {
        // 1. Setup price spec
        await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "In Lụa 10x15",
            UnitPrice = 4000
        }, initialAlias: "In Lua 10x15");

        // 2. Create physical folder for guest
        string guestFolder = Path.Combine(_tempRoot, "Chi Lan Q7");
        string specFolder = Path.Combine(guestFolder, "In Lua 10x15");
        CreateDummyFiles(specFolder, 15);

        // 3. Action: Build Guest Bill Draft
        var result = await _customerBillingService.BuildGuestBillDraftAsync(new[] { guestFolder });

        // 4. Assert
        result.Draft.Should().NotBeNull();
        result.Draft.BillType.Should().Be(BillType.Guest);
        result.Draft.CustomerId.Should().BeNull();
        result.Draft.CustomerNameSnapshot.Should().Be("Chi Lan Q7");
        result.Draft.ProductSubtotal.Should().Be(15 * 4000);
        result.Draft.GrandTotal.Should().Be(15 * 4000);

        // Crucial requirement: Customers table must remain untouched (NO dummy/guest customer)
        var allCustomers = await _customerRepo.GetAllAsync();
        allCustomers.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildGuestBillDraft_CustomGuestName_PreservesCustomName()
    {
        string guestFolder = Path.Combine(_tempRoot, "Chi Lan 123");
        string specFolder = Path.Combine(guestFolder, "In 15x21");
        CreateDummyFiles(specFolder, 5);

        await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "In 15x21",
            UnitPrice = 5000
        });

        // Action: Pass custom name
        var result = await _customerBillingService.BuildGuestBillDraftAsync(
            new[] { guestFolder },
            customGuestName: "Chị Lan VIP Quận 7"
        );

        // Assert
        result.Draft.CustomerNameSnapshot.Should().Be("Chị Lan VIP Quận 7");
        result.Draft.BillType.Should().Be(BillType.Guest);
        result.Draft.CustomerId.Should().BeNull();
    }

    [Fact]
    public async Task BuildGuestBillDraft_MultiSourceFolders_LinksAllSourceFoldersAndTracksOrigins()
    {
        await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "In Lụa 10x15",
            UnitPrice = 4000
        }, initialAlias: "In Lua 10x15");

        // Create 2 folders: Chi Lan 1 and Chi Lan them
        string folder1 = Path.Combine(_tempRoot, "Chi Lan 1");
        string folder2 = Path.Combine(_tempRoot, "Chi Lan them");

        CreateDummyFiles(Path.Combine(folder1, "In Lua 10x15"), 10);
        CreateDummyFiles(Path.Combine(folder2, "In Lua 10x15"), 8);

        // Action: Build draft with both folders
        var result = await _customerBillingService.BuildGuestBillDraftAsync(new[] { folder1, folder2 });
        var draft = result.Draft;

        // Assert
        draft.SourceFolders.Should().HaveCount(2);
        draft.SourceFolders.Select(sf => sf.FolderPath).Should().Contain(new[] { folder1, folder2 });

        draft.Orders.Should().HaveCount(2);
        draft.Orders.First(o => o.OriginalFolderNameSnapshot == "Chi Lan 1").SourceFolderPath.Should().Be(folder1);
        draft.Orders.First(o => o.OriginalFolderNameSnapshot == "Chi Lan them").SourceFolderPath.Should().Be(folder2);

        draft.ProductSubtotal.Should().Be((10 + 8) * 4000);
        draft.GrandTotal.Should().Be((10 + 8) * 4000);

        // Persisted source folders verification
        var persistedFolders = await _customerBillRepo.GetSourceFoldersByBillIdAsync(draft.Id);
        persistedFolders.Should().HaveCount(2);
    }

    [Fact]
    public async Task BuildGuestBillDraft_CalculatesPhotoAndAlbumPricing()
    {
        // 1. Photo spec
        await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "In Ảnh 10x15",
            Category = ProductCategory.PhotoPrint,
            BillingMethod = BillingMethod.FileCount,
            UnitPrice = 3000
        }, initialAlias: "In Anh 10x15");

        // 2. Album spec: 10 included sheets, base 150,000, extra sheet 10,000
        await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "Album 25x25",
            Category = ProductCategory.Album,
            BillingMethod = BillingMethod.AlbumBasePlusExtra,
            IncludedSheets = 10,
            BasePrice = 150000,
            ExtraSheetPrice = 10000
        }, initialAlias: "Album 25x25");

        string guestFolder = Path.Combine(_tempRoot, "Khach Le VIP");
        CreateDummyFiles(Path.Combine(guestFolder, "In Anh 10x15"), 20); // 20 * 3000 = 60,000
        CreateDummyFiles(Path.Combine(guestFolder, "Album 25x25"), 14); // 10 inc + 4 extra @ 10k = 190,000

        var result = await _customerBillingService.BuildGuestBillDraftAsync(new[] { guestFolder });

        result.Draft.ProductSubtotal.Should().Be(60000 + 190000);
        result.Draft.GrandTotal.Should().Be(250000);
    }

    [Fact]
    public async Task CheckDuplicateSourceFolders_DetectsLockedOrExportedBill_ReturnsWarning()
    {
        await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "In 10x15",
            UnitPrice = 2000
        });

        string billedFolder = Path.Combine(_tempRoot, "Chi Lan Cu");
        CreateDummyFiles(Path.Combine(billedFolder, "In 10x15"), 10);

        // Create and lock a bill for this folder
        var draft = (await _customerBillingService.BuildGuestBillDraftAsync(new[] { billedFolder })).Draft;
        var lockedBill = await _customerBillingService.LockBillAsync(draft);
        lockedBill.Status.Should().Be(CustomerBillStatus.Locked);

        // Another folder not billed
        string newFolder = Path.Combine(_tempRoot, "Khach Moi");
        CreateDummyFiles(Path.Combine(newFolder, "In 10x15"), 5);

        // Action: Check duplicates for both
        var warnings = await _customerBillingService.CheckDuplicateSourceFoldersAsync(new[] { billedFolder, newFolder });

        // Assert: Warns only for the billed folder
        warnings.Should().HaveCount(1);
        var warning = warnings[0];
        warning.FolderPath.Should().Be(billedFolder);
        warning.PreviousBillId.Should().Be(lockedBill.Id);
        warning.PreviousBillNumber.Should().Be(lockedBill.BillNumber);
        warning.PreviousBillGrandTotal.Should().Be(lockedBill.GrandTotal);
    }

    [Fact]
    public async Task LockBillAsync_GuestBill_LocksSnapshotAndMarksItemsBilled()
    {
        await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "In 15x21",
            UnitPrice = 5000
        });

        string guestFolder = Path.Combine(_tempRoot, "Khach 001");
        CreateDummyFiles(Path.Combine(guestFolder, "In 15x21"), 12);

        var draft = (await _customerBillingService.BuildGuestBillDraftAsync(new[] { guestFolder })).Draft;
        var locked = await _customerBillingService.LockBillAsync(draft);

        locked.Status.Should().Be(CustomerBillStatus.Locked);
        locked.LockedAt.Should().NotBeNull();
        locked.BillType.Should().Be(BillType.Guest);
        locked.CustomerId.Should().BeNull();

        // Customer table must remain clean
        (await _customerRepo.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ConvertGuestBillToCustomer_CreatesCustomerAndLinksBillAndOrders()
    {
        await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "In 20x20",
            UnitPrice = 15000
        });

        string guestFolder = Path.Combine(_tempRoot, "Chi Mai Wedding");
        CreateDummyFiles(Path.Combine(guestFolder, "In 20x20"), 4);

        // 1. Create and lock guest bill
        var draft = (await _customerBillingService.BuildGuestBillDraftAsync(new[] { guestFolder })).Draft;
        await _customerBillingService.LockBillAsync(draft);

        // 2. Action: Convert to Customer "Studio Mai Wedding"
        var customer = await _customerBillingService.ConvertGuestBillToCustomerAsync(draft.Id, "Studio Mai Wedding");

        // 3. Assert: Customer created
        customer.Should().NotBeNull();
        customer.CanonicalName.Should().Be("Studio Mai Wedding");

        // Assert: Bill updated
        var updatedBill = await _customerBillRepo.GetByIdAsync(draft.Id);
        updatedBill.Should().NotBeNull();
        updatedBill!.CustomerId.Should().Be(customer.Id);
        updatedBill.BillType.Should().Be(BillType.Customer);
        updatedBill.CustomerNameSnapshot.Should().Be("Studio Mai Wedding");
        updatedBill.GrandTotal.Should().Be(4 * 15000);

        // Assert: Associated orders updated
        var linkedOrders = await _orderRepo.GetOrdersByCustomerIdAsync(customer.Id);
        linkedOrders.Should().HaveCount(1);
        linkedOrders[0].CustomerId.Should().Be(customer.Id);
    }

    [Fact]
    public async Task CustomerIsolation_GuestBillsNotAggregatedIntoCustomerSummaryOrCustomerBills()
    {
        // 1. Create registered customer
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Đức Anh Studio" });

        // 2. Create guest bill
        await _productRepo.CreateSpecificationAsync(new PrintSpecification { CanonicalName = "In 10x15", UnitPrice = 3000 });
        string guestFolder = Path.Combine(_tempRoot, "Khach Vang Lai");
        CreateDummyFiles(Path.Combine(guestFolder, "In 10x15"), 10);
        var guestDraft = (await _customerBillingService.BuildGuestBillDraftAsync(new[] { guestFolder })).Draft;
        await _customerBillingService.LockBillAsync(guestDraft);

        // 3. Assert: Customer unbilled summary is untouched (0 unbilled orders)
        var summary = await _customerBillingService.GetCustomerUnbilledSummaryAsync(customer.Id);
        summary.UnbilledOrderCount.Should().Be(0);
        summary.UnbilledJobCount.Should().Be(0);
        summary.EstimatedTotal.Should().Be(0);

        // Customer's bills list is empty
        var customerBills = await _customerBillRepo.GetBillsByCustomerIdAsync(customer.Id);
        customerBills.Should().BeEmpty();

        // Repository filtering: Guest bills vs Customer bills
        var onlyGuestBills = await _customerBillRepo.GetAllBillsAsync(BillType.Guest);
        onlyGuestBills.Should().HaveCount(1);
        onlyGuestBills[0].BillNumber.Should().Be(guestDraft.BillNumber);

        var onlyCustomerBills = await _customerBillRepo.GetAllBillsAsync(BillType.Customer);
        onlyCustomerBills.Should().BeEmpty();
    }

    [Fact]
    public void PathNormalizer_WindowsPathEquivalence_NormalizesCorrectly()
    {
        string p1 = @"D:\Photos\Chi Lan\";
        string p2 = @"d:/photos/chi lan";
        string p3 = @"D:\Photos\Chi Lan";

        PathNormalizer.AreEqual(p1, p2).Should().BeTrue();
        PathNormalizer.AreEqual(p2, p3).Should().BeTrue();
        PathNormalizer.Normalize(p1).Should().Be(PathNormalizer.Normalize(p2));
    }

    [Fact]
    public async Task WpfJpegBillExporter_ExportsGuestBill_WithDeterministicSlugFilename()
    {
        await _productRepo.CreateSpecificationAsync(new PrintSpecification { CanonicalName = "In 10x15", UnitPrice = 3000 });
        string guestFolder = Path.Combine(_tempRoot, "Chị Lan Q7");
        CreateDummyFiles(Path.Combine(guestFolder, "In 10x15"), 5);

        var draft = (await _customerBillingService.BuildGuestBillDraftAsync(new[] { guestFolder })).Draft;

        // Action: Export to JPEG
        string exportPath = await _jpegExporter.ExportBillToJpegAsync(draft);

        // Assert
        File.Exists(exportPath).Should().BeTrue();
        Path.GetFileName(exportPath).Should().StartWith(draft.BillNumber);
        Path.GetFileName(exportPath).Should().Contain("Chi-Lan-Q7");
        Path.GetExtension(exportPath).Should().Be(".jpg");

        draft.Status.Should().Be(CustomerBillStatus.Exported);
        draft.ExportFilePath.Should().Be(exportPath);
    }

    [Fact]
    public void QuickBillSetupViewModel_HasFolders_TracksSourceFoldersProperly()
    {
        var vm = new LalabAutoReport.UI.ViewModels.QuickBillSetupViewModel(_customerBillingService, _customerBillRepo, _jpegExporter, _settingsRepo);
        vm.HasFolders.Should().BeFalse();

        vm.SourceFolders.Add(@"D:\TestData\Folder1");
        vm.HasFolders.Should().BeTrue();

        vm.SourceFolders.Remove(@"D:\TestData\Folder1");
        vm.HasFolders.Should().BeFalse();
    }

    [Fact]
    public async Task QuickBill_FromRightClickFolder_ResolvesCustomerNameAndProducesValidDraft()
    {
        await _productRepo.CreateSpecificationAsync(new PrintSpecification { CanonicalName = "In 15x21", UnitPrice = 5000 });
        string guestFolder = Path.Combine(_tempRoot, "Khách Lẻ Tuấn 0909123456");
        CreateDummyFiles(Path.Combine(guestFolder, "In 15x21"), 8);

        // When launching quick bill from right-click folder:
        string folderName = Path.GetFileName(guestFolder);
        var result = await _customerBillingService.BuildGuestBillDraftAsync(new[] { guestFolder }, folderName);

        result.Draft.Should().NotBeNull();
        result.Draft.CustomerNameSnapshot.Should().Be("Khách Lẻ Tuấn 0909123456");
        result.Draft.BillType.Should().Be(BillType.Guest);
        result.Draft.Orders.Should().HaveCount(1);
        result.Draft.Lines.Should().HaveCount(1);
        result.Draft.Lines[0].BilledQuantity.Should().Be(8);
        result.Draft.Lines[0].LineTotal.Should().Be(40000);
        result.Draft.GrandTotal.Should().Be(40000);
    }

    [Fact]
    public async Task BuildGuestBillDraftAsync_WhenFolderMatchesExistingCustomer_ShouldKeepGuestTypeAndSuggestCustomer()
    {
        // 1. Setup registered customer
        var customer = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Quang Studio"
        });

        // 2. Setup print spec
        await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "In 15x21",
            UnitPrice = 5000
        });

        // 3. Create folder matching customer name
        string customerFolder = Path.Combine(_tempRoot, "Quang Studio");
        CreateDummyFiles(Path.Combine(customerFolder, "In 15x21"), 10);

        // 4. Action: Build draft with folder name
        var result = await _customerBillingService.BuildGuestBillDraftAsync(new[] { customerFolder }, "Quang Studio");

        // 5. Assert: Draft must remain Guest with SuggestedCustomer, NOT auto-converted
        result.Draft.Should().NotBeNull();
        result.Draft.BillType.Should().Be(BillType.Guest);
        result.Draft.CustomerId.Should().BeNull();
        result.Draft.CustomerNameSnapshot.Should().Be("Quang Studio");
        result.Draft.Orders.Should().HaveCount(1);
        result.SuggestedCustomer.Should().NotBeNull();
        result.SuggestedCustomer!.Id.Should().Be(customer.Id);
        result.Warnings.Should().Contain(w => w.Contains("Quang Studio"));

        // 6. Explicit conversion must succeed
        var convertedCustomer = await _customerBillingService.ConvertGuestBillToCustomerAsync(result.Draft.Id, "Quang Studio");
        convertedCustomer.Should().NotBeNull();
        convertedCustomer.Id.Should().Be(customer.Id);

        var updatedBill = await _customerBillRepo.GetByIdAsync(result.Draft.Id);
        updatedBill.Should().NotBeNull();
        updatedBill!.BillType.Should().Be(BillType.Customer);
        updatedBill.CustomerId.Should().Be(customer.Id);
    }

    [Fact]
    public async Task ScanService_WhenScanningGuestGroupFolder_ResolvesItemsAndSetsStatusToReady()
    {
        // 1. Create physical folder: 2026-09-29\KHACH_LE\Chi Lan 1\13x18 in
        string dateDir = Path.Combine(_tempRoot, "2026-09-29");
        string guestGroupDir = Path.Combine(dateDir, "KHACH_LE");
        string explicitGuestDir = Path.Combine(guestGroupDir, "Chi Lan 1");
        string specDir = Path.Combine(explicitGuestDir, "13x18 in");
        CreateDummyFiles(specDir, 10);

        // 2. Action: Scan date
        var scannedOrders = await _scanService.ScanDateAsync("2026-09-29");

        // 3. Assert
        scannedOrders.Should().HaveCount(1);
        var order = scannedOrders[0];
        order.OriginalFolderName.Should().Be("KHACH_LE");
        order.OrderName.Should().Be("Chi Lan 1");
        order.IsGuest.Should().BeTrue();
        order.CustomerId.Should().BeNull();
        order.Status.Should().Be(OrderStatus.Ready); // Must be Ready, NOT NeedsReview!

        order.Items.Should().HaveCount(1);
        var item = order.Items[0];
        item.PrintCount.Should().Be(10);
        item.BillQuantity.Should().Be(10);
        item.PrintFolderStatus.Should().Be(PrintFolderResolutionStatus.Resolved);
    }

    [Fact]
    public async Task ScanService_WhenScanningRegularUnregisteredCustomer_SetsStatusToNeedsReview()
    {
        // 1. Create physical folder: 2026-09-29\Studio Sen Vang\13x18 in
        string dateDir = Path.Combine(_tempRoot, "2026-09-29");
        string studioDir = Path.Combine(dateDir, "Studio Sen Vang");
        string specDir = Path.Combine(studioDir, "13x18 in");
        CreateDummyFiles(specDir, 12);

        // 2. Action: Scan date
        var scannedOrders = await _scanService.ScanDateAsync("2026-09-29");

        // 3. Assert
        var studioOrder = scannedOrders.FirstOrDefault(o => o.OriginalFolderName == "Studio Sen Vang");
        studioOrder.Should().NotBeNull();
        studioOrder!.IsGuest.Should().BeFalse();
        studioOrder.CustomerId.Should().BeNull();
        studioOrder.Status.Should().Be(OrderStatus.NeedsReview); // Regular unknown studio MUST be NeedsReview!
    }

    [Fact]
    public async Task ScanService_WhenRescanningExistingNeedsReviewGuestOrder_UpdatesStatusToReady()
    {
        // 1. Create physical folder: 2026-09-29\KHACH_LE\Chi Lan them\13x18 in
        string dateDir = Path.Combine(_tempRoot, "2026-09-29");
        string guestGroupDir = Path.Combine(dateDir, "KHACH_LE");
        string explicitGuestDir = Path.Combine(guestGroupDir, "Chi Lan them");
        string specDir = Path.Combine(explicitGuestDir, "13x18 in");
        CreateDummyFiles(specDir, 8);

        // 2. Pre-insert order with status NeedsReview into DB
        var existingOrder = new Order
        {
            WorkDate = "2026-09-29",
            OriginalFolderName = "KHACH_LE",
            RelativePath = @"2026-09-29\KHACH_LE\Chi Lan them",
            OrderKind = OrderKind.Explicit,
            OrderName = "Chi Lan them",
            Status = OrderStatus.NeedsReview,
            CustomerId = null
        };
        await _orderRepo.SaveOrderAsync(existingOrder, new ScanSnapshot());

        // 3. Action: Rescan single order
        var rescanned = await _scanService.ScanOrderAsync(@"2026-09-29\KHACH_LE\Chi Lan them");

        // 4. Assert
        rescanned.Should().NotBeNull();
        rescanned!.Status.Should().Be(OrderStatus.Ready);

        // Check DB persisted status
        var fromDb = await _orderRepo.GetOrderByIdAsync(existingOrder.Id);
        fromDb.Should().NotBeNull();
        fromDb!.Status.Should().Be(OrderStatus.Ready);
    }

    [Fact]
    public async Task BuildGuestBillDraftAsync_WhenGivenRootFolder_ThrowsInvalidOperationException()
    {
        // Calling Quick Bill draft with root folder itself must throw InvalidOperationException
        var act = async () => await _customerBillingService.BuildGuestBillDraftAsync(new[] { _tempRoot });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Thư Mục Gốc*");
    }

    [Fact]
    public async Task BuildGuestBillDraftAsync_WhenGivenDateFolderUnderRoot_ThrowsInvalidOperationException()
    {
        // Calling Quick Bill draft with a Date folder (e.g. 2026-09-30) directly must throw InvalidOperationException
        string dateDir = Path.Combine(_tempRoot, "2026-09-30");
        Directory.CreateDirectory(dateDir);

        var act = async () => await _customerBillingService.BuildGuestBillDraftAsync(new[] { dateDir });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Thư Mục Ngày*");
    }

    [Fact]
    public async Task SqliteOrderRepository_DeleteOrderAsync_WhenOrderNotLocked_DeletesSuccessfully()
    {
        var order = new Order
        {
            WorkDate = "2026-09-30",
            OriginalFolderName = "Test Guest Delete",
            RelativePath = @"2026-09-30\Test Guest Delete",
            Status = OrderStatus.NeedsReview
        };
        await _orderRepo.SaveOrderAsync(order, new ScanSnapshot());

        var loaded = await _orderRepo.GetOrderByIdAsync(order.Id);
        loaded.Should().NotBeNull();

        await _orderRepo.DeleteOrderAsync(order.Id);

        var afterDelete = await _orderRepo.GetOrderByIdAsync(order.Id);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task SqliteOrderRepository_DeleteOrderAsync_WhenOrderLocked_ThrowsInvalidOperationException()
    {
        var order = new Order
        {
            WorkDate = "2026-09-30",
            OriginalFolderName = "Locked Order",
            RelativePath = @"2026-09-30\Locked Order",
            Status = OrderStatus.Locked
        };
        await _orderRepo.SaveOrderAsync(order, new ScanSnapshot());

        var act = async () => await _orderRepo.DeleteOrderAsync(order.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Không thể xóa đơn hàng đã chốt hoặc đã khóa hóa đơn*");
    }
}



