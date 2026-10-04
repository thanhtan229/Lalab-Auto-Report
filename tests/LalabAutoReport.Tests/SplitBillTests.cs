using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Dapper;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using LalabAutoReport.Infrastructure.Reporting;
using LalabAutoReport.UI.ViewModels;
using Xunit;

namespace LalabAutoReport.Tests;

public class SplitBillTests : IDisposable
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

    public SplitBillTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_SplitBillTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _dbPath = Path.Combine(_tempRoot, "test_split_billing.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _orderRepo = new SqliteOrderRepository(_connectionFactory);
        _billRepo = new SqliteBillRepository(_connectionFactory);
        _customerBillRepo = new SqliteCustomerBillRepoWrapper(_connectionFactory);
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
            _billRepo,
            customerBillRepository: _customerBillRepo
        );

        _customerBillingService = new CustomerBillingService(
            _customerRepo,
            _orderRepo,
            _customerBillRepo,
            _productRepo,
            _scanService
        );
    }

    private class SqliteCustomerBillRepoWrapper : SqliteCustomerBillRepository
    {
        public SqliteCustomerBillRepoWrapper(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }
    }

    [Theory]
    [InlineData("BEFORE UPDATE ON customer_bills")]
    [InlineData("BEFORE UPDATE ON order_item_scans")]
    public async Task ProductionSplit_WriteFailureRollsBackNewBillAndMembership(string triggerTarget)
    {
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Atomic split" });
        await _customerRepo.AddAliasAsync(customer.Id, "Atomic split 2");
        CreateDummyFiles(Path.Combine(_tempRoot, "2026-10-02", "Atomic split", "13x18 in"), 2);
        CreateDummyFiles(Path.Combine(_tempRoot, "2026-10-02", "Atomic split 2", "13x18 in"), 3);
        await _scanService.ScanDateAsync("2026-10-02");
        var bill = (await _customerBillingService.BuildOrRefreshDraftAsync(customer.Id)).Draft;
        long moved = bill.Orders[1].OrderId;
        using var connection = _connectionFactory.CreateConnection();
        long count = await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM customer_bills");
        await connection.ExecuteAsync($"CREATE TRIGGER reject_split {triggerTarget} BEGIN SELECT RAISE(ABORT, 'injected split failure'); END");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => _customerBillingService.SplitOrdersToNewBillAsync(bill.Id, new[] { moved }));
        Assert.Equal(count, await connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM customer_bills"));
        Assert.Equal(2, (await _customerBillRepo.GetByIdAsync(bill.Id))!.Orders.Count);
        Assert.Equal(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM customer_bill_orders WHERE order_id=@Id", new { Id = moved }));
        Assert.Empty(await connection.QueryAsync("PRAGMA foreign_key_check"));
        await connection.ExecuteAsync("DROP TRIGGER reject_split");
        var split = await _customerBillingService.SplitOrdersToNewBillAsync(bill.Id, new[] { moved });
        Assert.NotEqual(bill.Id, split.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _customerBillingService.SplitOrdersToNewBillAsync(bill.Id, new[] { moved }));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
    public async Task SplitOrdersToNewBillAsync_MovesSelectedOrder_AndRecalculatesBothBills()
    {
        // 1. Setup Customer
        var customer = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Studio Cưới Hoàng Gia",
            Phone = "0988888888",
            Address = "123 Phố Huế, Hà Nội",
            PriceTier = PriceTier.Studio
        });
        await _customerRepo.AddAliasAsync(customer.Id, "Hoàng Gia 2");
        await _customerRepo.AddAliasAsync(customer.Id, "Hoàng Gia 3");

        // 2. Setup 3 physical order folders on disk
        string date = "2026-10-01";

        string folder1 = Path.Combine(_tempRoot, date, "Studio Cưới Hoàng Gia", "In 13x18", "final");
        CreateDummyFiles(folder1, 10);

        string folder2 = Path.Combine(_tempRoot, date, "Hoàng Gia 2", "In 13x18", "final");
        CreateDummyFiles(folder2, 20);

        string folder3 = Path.Combine(_tempRoot, date, "Hoàng Gia 3", "In 13x18", "final");
        CreateDummyFiles(folder3, 30);

        // Scan all 3 orders
        var scan1 = await _scanService.ScanDateAsync(date);
        scan1.Should().HaveCount(3);

        // Build Draft Bill for this customer
        var draftResult = await _customerBillingService.BuildOrRefreshDraftAsync(customer.Id);
        var initialBill = draftResult.Draft;

        initialBill.Orders.Should().HaveCount(3);
        initialBill.Lines.Should().HaveCount(3);

        long order1Id = initialBill.Orders[0].OrderId;
        long order2Id = initialBill.Orders[1].OrderId;
        long order3Id = initialBill.Orders[2].OrderId;

        long initialTotal = initialBill.GrandTotal;
        long order2Subtotal = initialBill.Orders[1].Subtotal;
        order2Subtotal.Should().BeGreaterThan(0);

        // 3. Act: Split order2 into a brand new Bill
        var newBill = await _customerBillingService.SplitOrdersToNewBillAsync(initialBill.Id, new[] { order2Id });

        // 4. Assert New Bill
        newBill.Should().NotBeNull();
        newBill.Id.Should().NotBe(initialBill.Id);
        newBill.BillNumber.Should().NotBe(initialBill.BillNumber);
        newBill.CustomerId.Should().Be(customer.Id);
        newBill.Orders.Should().HaveCount(1);
        newBill.Orders[0].OrderId.Should().Be(order2Id);
        newBill.Lines.Should().HaveCount(1);
        newBill.Lines[0].OrderId.Should().Be(order2Id);
        newBill.GrandTotal.Should().Be(order2Subtotal);

        // 5. Assert Original Bill
        var updatedOriginal = await _customerBillRepo.GetByIdAsync(initialBill.Id);
        updatedOriginal.Should().NotBeNull();
        updatedOriginal!.Orders.Should().HaveCount(2);
        updatedOriginal.Orders.Select(o => o.OrderId).Should().NotContain(order2Id);
        updatedOriginal.Orders.Select(o => o.OrderId).Should().Contain(new[] { order1Id, order3Id });
        updatedOriginal.GrandTotal.Should().Be(initialTotal - order2Subtotal);

        // 6. Verify order item scans reference new bill
        var movedOrder = await _orderRepo.GetOrderByIdAsync(order2Id);
        movedOrder.Should().NotBeNull();
        foreach (var item in movedOrder!.Items)
        {
            item.CustomerBillId.Should().Be(newBill.Id);
        }
    }

    [Fact]
    public async Task SplitOrdersToNewBillAsync_ThrowsIfAllOrdersSelected()
    {
        var customer = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Studio Sao Mai",
            PriceTier = PriceTier.Retail
        });
        await _customerRepo.AddAliasAsync(customer.Id, "Sao Mai 2");

        string date = "2026-10-02";

        string folder1 = Path.Combine(_tempRoot, date, "Studio Sao Mai", "In 13x18", "final");
        CreateDummyFiles(folder1, 5);

        string folder2 = Path.Combine(_tempRoot, date, "Sao Mai 2", "In 13x18", "final");
        CreateDummyFiles(folder2, 5);

        await _scanService.ScanDateAsync(date);
        var draftResult = await _customerBillingService.BuildOrRefreshDraftAsync(customer.Id);
        var bill = draftResult.Draft;

        var allOrderIds = bill.Orders.Select(o => o.OrderId).ToList();

        // Attempting to move all orders must throw InvalidOperationException
        var act = async () => await _customerBillingService.SplitOrdersToNewBillAsync(bill.Id, allOrderIds);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*giữ lại ít nhất 1 đơn hàng*");
    }

    [Fact]
    public async Task SplitOrdersToNewBillAsync_ThrowsIfBillIsLocked()
    {
        var customer = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Studio Bình Minh",
            PriceTier = PriceTier.Retail
        });
        await _customerRepo.AddAliasAsync(customer.Id, "Bình Minh 2");

        string date = "2026-10-03";

        string folder1 = Path.Combine(_tempRoot, date, "Studio Bình Minh", "In 13x18", "final");
        CreateDummyFiles(folder1, 5);

        string folder2 = Path.Combine(_tempRoot, date, "Bình Minh 2", "In 13x18", "final");
        CreateDummyFiles(folder2, 5);

        await _scanService.ScanDateAsync(date);
        var draftResult = await _customerBillingService.BuildOrRefreshDraftAsync(customer.Id);
        var bill = draftResult.Draft;

        // Lock the bill
        bill = await _customerBillingService.LockBillAsync(bill);
        bill.Status.Should().Be(CustomerBillStatus.Locked);

        // Attempting to split a locked bill must throw InvalidOperationException
        var act = async () => await _customerBillingService.SplitOrdersToNewBillAsync(bill.Id, new[] { bill.Orders[0].OrderId });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mở khóa*");
    }

    [Fact]
    public async Task SplitOrdersToNewBillAsync_ThrowsIfBillIsExported()
    {
        var customer = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Studio Ánh Sao",
            PriceTier = PriceTier.Retail
        });
        await _customerRepo.AddAliasAsync(customer.Id, "Ánh Sao 2");

        string date = "2026-10-04";

        string folder1 = Path.Combine(_tempRoot, date, "Studio Ánh Sao", "In 13x18", "final");
        CreateDummyFiles(folder1, 5);

        string folder2 = Path.Combine(_tempRoot, date, "Ánh Sao 2", "In 13x18", "final");
        CreateDummyFiles(folder2, 5);

        await _scanService.ScanDateAsync(date);
        var draftResult = await _customerBillingService.BuildOrRefreshDraftAsync(customer.Id);
        var bill = draftResult.Draft;

        // Lock & Export the bill
        bill = await _customerBillingService.LockBillAsync(bill);
        await _customerBillingService.RecordBillExportedAsync(bill.Id, @"C:\fake\export.jpg");
        var exportedBill = await _customerBillRepo.GetByIdAsync(bill.Id);
        exportedBill!.Status.Should().Be(CustomerBillStatus.Exported);

        // Attempting to split an exported bill must throw InvalidOperationException
        var act = async () => await _customerBillingService.SplitOrdersToNewBillAsync(exportedBill.Id, new[] { exportedBill.Orders[0].OrderId });
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mở khóa*");
    }

    [Fact]
    public void CustomerBillReviewViewModel_CanSplitOrders_CorrectState()
    {
        var bill = new CustomerBill
        {
            Id = 1,
            BillNumber = "BILL-TEST-0001",
            Status = CustomerBillStatus.Draft,
            Orders = new List<CustomerBillOrder>
            {
                new() { OrderId = 101, OrderNameSnapshot = "Đơn 1", Subtotal = 50000 },
                new() { OrderId = 102, OrderNameSnapshot = "Đơn 2", Subtotal = 80000 }
            }
        };

        var vm = new CustomerBillReviewViewModel(
            bill,
            _customerBillingService,
            new WpfJpegBillExporter(_settingsRepo)
        );

        // With 2 orders and Draft status: CanSplitOrders is true
        vm.CanSplitOrders.Should().BeTrue();

        // When bill is Exported: CanSplitOrders is false
        bill.Status = CustomerBillStatus.Exported;
        vm.LoadFromBill(bill);
        vm.CanSplitOrders.Should().BeFalse();

        // Reset to Draft: CanSplitOrders is true
        bill.Status = CustomerBillStatus.Draft;
        vm.LoadFromBill(bill);
        vm.CanSplitOrders.Should().BeTrue();

        // When locked: CanSplitOrders is false
        vm.IsLocked = true;
        vm.CanSplitOrders.Should().BeFalse();

        // When single order: CanSplitOrders is false
        vm.IsLocked = false;
        bill.Orders.RemoveAt(1);
        vm.LoadFromBill(bill);
        vm.CanSplitOrders.Should().BeFalse();
    }

    [Fact]
    public async Task SplitOrdersToNewBillAsync_ThrowsIfEmptyOrderIds_OrNonExistentBill()
    {
        // Empty orderIds
        var actEmpty = async () => await _customerBillingService.SplitOrdersToNewBillAsync(12345, Array.Empty<long>());
        await actEmpty.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*không được rỗng*");

        // Non-existent bill
        var actNotFound = async () => await _customerBillingService.SplitOrdersToNewBillAsync(999999, new[] { 1L });
        await actNotFound.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Không tìm thấy hóa đơn*");
    }
}
