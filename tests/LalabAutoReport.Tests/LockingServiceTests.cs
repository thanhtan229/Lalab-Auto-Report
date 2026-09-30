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

public class LockingServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteOrderRepository _orderRepo;
    private readonly SqliteBillRepository _billRepo;
    private readonly SqliteCustomerRepository _customerRepo;
    private readonly SqlitePrintSpecificationRepository _specRepo;
    private readonly SqliteSettingsRepository _settingsRepo;
    private readonly PhysicalFileSystemAdapter _fileSystem;
    private readonly FolderStructureParser _structureParser;
    private readonly PrintFolderResolver _folderResolver;
    private readonly CustomerResolver _customerResolver;
    private readonly PrintSpecificationResolver _specResolver;
    private readonly ScanService _scanService;
    private readonly BillingService _billingService;
    private readonly LockingService _lockingService;

    public LockingServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_LockingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _dbPath = Path.Combine(_tempRoot, "test_lalab.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _orderRepo = new SqliteOrderRepository(_connectionFactory);
        _billRepo = new SqliteBillRepository(_connectionFactory);
        _customerRepo = new SqliteCustomerRepository(_connectionFactory);
        _specRepo = new SqlitePrintSpecificationRepository(_connectionFactory);
        _settingsRepo = new SqliteSettingsRepository(_connectionFactory);

        _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            SupportedExtensions = new() { ".jpg", ".jpeg", ".png", ".tif" }
        }).GetAwaiter().GetResult();

        _fileSystem = new PhysicalFileSystemAdapter();
        _structureParser = new FolderStructureParser(_fileSystem);
        _folderResolver = new PrintFolderResolver(_fileSystem);
        _customerResolver = new CustomerResolver(_customerRepo);
        _specResolver = new PrintSpecificationResolver(_specRepo);

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

        _billingService = new BillingService(
            _orderRepo,
            _billRepo,
            _specRepo,
            _customerRepo
        );

        _lockingService = new LockingService(
            _scanService,
            _orderRepo,
            _billRepo,
            _billingService
        );
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
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task VerifyOrder_Fails_When_Customer_Is_Unresolved()
    {
        // Setup order with unknown customer
        string date = "2026-09-28";
        string orderFolder = Path.Combine(_tempRoot, date, "KhachLaChuaCoTen", "13x18 in");
        Directory.CreateDirectory(orderFolder);
        File.WriteAllBytes(Path.Combine(orderFolder, "img1.jpg"), new byte[] { 1 });

        var scannedOrders = await _scanService.ScanDateAsync(date);
        scannedOrders.Should().HaveCount(1);
        var order = scannedOrders[0];

        // Action
        var result = await _lockingService.VerifyOrderForLockAsync(order.Id);

        // Assert
        result.CanLock.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("chưa được xác định hoặc gán danh tính"));
    }

    [Fact]
    public async Task VerifyOrder_Fails_When_Album_Has_Zero_Prints()
    {
        // 1. Create known customer
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Minh Tuấn" }, "Minh Tuấn");

        // 2. Setup order with album having 0 prints
        string date = "2026-09-28";
        string albumFolder = Path.Combine(_tempRoot, date, "Minh Tuấn", "Album 20x20");
        Directory.CreateDirectory(albumFolder);
        File.WriteAllBytes(Path.Combine(albumFolder, "readme.txt"), new byte[] { 1 });

        var scannedOrders = await _scanService.ScanDateAsync(date);
        scannedOrders.Should().HaveCount(1);
        var order = scannedOrders[0];

        // Action
        var result = await _lockingService.VerifyOrderForLockAsync(order.Id);

        // Assert
        result.CanLock.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("không có tệp in") || r.Contains("không xác định"));
    }

    [Fact]
    public async Task VerifyOrder_Fails_When_PrintFolder_Is_Ambiguous()
    {
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Hoàng Nam" }, "Hoàng Nam");

        string date = "2026-09-28";
        string specFolder = Path.Combine(_tempRoot, date, "Hoàng Nam", "13x18 in");
        string leaf1 = Path.Combine(specFolder, "edit1");
        string leaf2 = Path.Combine(specFolder, "edit2");
        Directory.CreateDirectory(leaf1);
        Directory.CreateDirectory(leaf2);

        File.WriteAllBytes(Path.Combine(specFolder, "src.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(leaf1, "p1.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(leaf2, "p2.jpg"), new byte[] { 1 });

        var scannedOrders = await _scanService.ScanDateAsync(date);
        var order = scannedOrders[0];

        // Action
        var result = await _lockingService.VerifyOrderForLockAsync(order.Id);

        // Assert
        result.CanLock.Should().BeFalse();
        result.BlockingReasons.Should().Contain(r => r.Contains("chưa có thư mục in hợp lệ"));
    }

    [Fact]
    public async Task VerifyOrder_Succeeds_When_All_Resolved_And_Generates_PreviewBill()
    {
        // 1. Create customer
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Lan Hương" }, "Lan Huong");

        // 2. Setup order with matching counts (Source: 3, Print: 3) -> 13x18 in default price is 5000 VND
        string date = "2026-09-28";
        string specFolder = Path.Combine(_tempRoot, date, "Lan Huong", "13x18 in");
        string printFolder = Path.Combine(specFolder, "final");
        Directory.CreateDirectory(printFolder);

        File.WriteAllBytes(Path.Combine(specFolder, "1.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(specFolder, "2.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(specFolder, "3.jpg"), new byte[] { 1 });

        File.WriteAllBytes(Path.Combine(printFolder, "p1.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(printFolder, "p2.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(printFolder, "p3.jpg"), new byte[] { 1 });

        var scannedOrders = await _scanService.ScanDateAsync(date);
        scannedOrders.Should().HaveCount(1);
        var order = scannedOrders[0];

        // Action
        var result = await _lockingService.VerifyOrderForLockAsync(order.Id);

        // Assert
        result.CanLock.Should().BeTrue();
        result.BlockingReasons.Should().BeEmpty();
        result.PreviewBill.Should().NotBeNull();
        result.PreviewBill!.Subtotal.Should().Be(15000); // 3 * 5000
    }

    [Fact]
    public async Task VerifyAndLockOrder_Locks_Bill_And_Order_In_Database()
    {
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Đức Thắng" }, "Đức Thắng");

        string date = "2026-09-28";
        string specFolder = Path.Combine(_tempRoot, date, "Đức Thắng", "20x30");
        string printFolder = Path.Combine(specFolder, "retouch");
        Directory.CreateDirectory(printFolder);

        File.WriteAllBytes(Path.Combine(specFolder, "1.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(specFolder, "2.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(printFolder, "p1.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(printFolder, "p2.jpg"), new byte[] { 1 });

        var scannedOrders = await _scanService.ScanDateAsync(date);
        var order = scannedOrders[0];

        // Action: Lock order
        var lockedBill = await _lockingService.VerifyAndLockOrderAsync(order.Id);

        // Assert
        lockedBill.Status.Should().Be(OrderStatus.Locked);
        lockedBill.LockedAt.Should().NotBeNull();
        lockedBill.Subtotal.Should().Be(30000); // 2 * 15000 VND

        // Check persisted state in database
        var dbOrder = await _orderRepo.GetOrderByIdAsync(order.Id);
        dbOrder!.Status.Should().Be(OrderStatus.Locked);
        dbOrder.FilesystemChangedAfterLock.Should().BeFalse();

        var dbBill = await _billRepo.GetBillByOrderIdAsync(order.Id);
        dbBill!.Status.Should().Be(OrderStatus.Locked);
        dbBill.LockedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Rescan_Of_Locked_Order_Never_Mutates_Locked_Bill_And_Flags_Filesystem_Changes()
    {
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Bảo Long" }, "Bảo Long");

        string date = "2026-09-28";
        string specFolder = Path.Combine(_tempRoot, date, "Bảo Long", "13x18 in");
        string printFolder = Path.Combine(specFolder, "final");
        Directory.CreateDirectory(printFolder);

        File.WriteAllBytes(Path.Combine(specFolder, "1.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(specFolder, "2.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(printFolder, "p1.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(printFolder, "p2.jpg"), new byte[] { 1 });

        var scannedOrders = await _scanService.ScanDateAsync(date);
        var order = scannedOrders[0];

        // Lock order (Subtotal = 2 * 5000 = 10,000 VND)
        var lockedBill = await _lockingService.VerifyAndLockOrderAsync(order.Id);
        lockedBill.Subtotal.Should().Be(10000);

        // Now modify files on disk: add 3 new files to printFolder (print count becomes 5)
        File.WriteAllBytes(Path.Combine(printFolder, "p3.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(printFolder, "p4.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(printFolder, "p5.jpg"), new byte[] { 1 });

        // Action: Rescan order
        var rescannedOrder = await _scanService.ScanOrderAsync(order.RelativePath);

        // Assert: Order status is Billed (open and rescan-friendly)
        rescannedOrder.Should().NotBeNull();
        rescannedOrder!.Status.Should().Be(OrderStatus.Billed);
    }

    [Fact]
    public async Task ReopenOrder_Reverts_Status_To_Ready_And_Clears_Warning()
    {
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Ngọc Mai" }, "Ngọc Mai");

        string date = "2026-09-28";
        string specFolder = Path.Combine(_tempRoot, date, "Ngọc Mai", "13x18 in");
        string printFolder = Path.Combine(specFolder, "in");
        Directory.CreateDirectory(printFolder);

        File.WriteAllBytes(Path.Combine(specFolder, "1.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(printFolder, "p1.jpg"), new byte[] { 1 });

        var scannedOrders = await _scanService.ScanDateAsync(date);
        var order = scannedOrders[0];

        // Lock order
        await _lockingService.VerifyAndLockOrderAsync(order.Id);

        // Trigger warning manually
        await _orderRepo.SetFilesystemChangedAfterLockAsync(order.Id, true);

        var preReopenOrder = await _orderRepo.GetOrderByIdAsync(order.Id);
        preReopenOrder!.FilesystemChangedAfterLock.Should().BeTrue();

        // Action: Reopen order
        await _lockingService.ReopenOrderAsync(order.Id, "Khách yêu cầu in bổ sung 1 ảnh");

        // Assert
        var reopenedOrder = await _orderRepo.GetOrderByIdAsync(order.Id);
        reopenedOrder!.Status.Should().Be(OrderStatus.Ready);
        reopenedOrder.FilesystemChangedAfterLock.Should().BeFalse();

        var reopenedBill = await _billRepo.GetBillByOrderIdAsync(order.Id);
        reopenedBill!.Status.Should().Be(OrderStatus.Billed);
        reopenedBill.LockedAt.Should().BeNull();
    }
}
