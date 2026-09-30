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
using LalabAutoReport.Infrastructure.Reporting;
using LalabAutoReport.UI.ViewModels;
using Xunit;

namespace LalabAutoReport.Tests;

public class RenameAndCustomerEditTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteCustomerRepository _customerRepo;
    private readonly SqliteProductRepository _productRepo;
    private readonly SqliteBillRepository _billRepo;
    private readonly SqliteOrderRepository _orderRepo;
    private readonly SqliteCustomerBillRepository _customerBillRepo;
    private readonly SqliteSettingsRepository _settingsRepo;
    private readonly PhysicalFileSystemAdapter _fileSystem;
    private readonly CustomerBillingService _customerBillingService;
    private readonly WpfJpegBillExporter _jpegExporter;

    public RenameAndCustomerEditTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_RenameTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _dbPath = Path.Combine(_tempRoot, "test_rename.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _customerRepo = new SqliteCustomerRepository(_connectionFactory);
        _productRepo = new SqliteProductRepository(_connectionFactory);
        _billRepo = new SqliteBillRepository(_connectionFactory);
        _orderRepo = new SqliteOrderRepository(_connectionFactory);
        _customerBillRepo = new SqliteCustomerBillRepository(_connectionFactory);
        _settingsRepo = new SqliteSettingsRepository(_connectionFactory);
        _fileSystem = new PhysicalFileSystemAdapter();

        _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            BillExportFolder = Path.Combine(_tempRoot, "_BILLS"),
            SupportedExtensions = new() { ".jpg", ".jpeg", ".png", ".tif" }
        }).GetAwaiter().GetResult();

        _customerBillingService = new CustomerBillingService(
            _customerRepo,
            _orderRepo,
            _customerBillRepo,
            _productRepo,
            null
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
        catch
        {
            // Ignore cleanup failures
        }
    }

    [Fact]
    public async Task RenameProduct_UpdatesCanonicalNameAndSize_AndPreservesOldNameAsAlias()
    {
        // 1. Lấy quy cách '13x18 in' đã có sẵn từ migration
        var allSpecs = await _productRepo.GetAllAsync();
        var initial = allSpecs.First(s => s.CanonicalName == "13x18 in");

        var vm = new PriceListViewModel(_productRepo);
        await vm.LoadSpecificationsAsync();

        vm.SelectedSpec = vm.Specifications.First(s => s.Id == initial.Id);
        vm.SelectedSpec.CanonicalName.Should().Be("13x18 in");

        // 2. Thực hiện đổi tên thành "Ảnh In 13x18 Lab Pro"
        bool ok = await vm.RenameSpecificationAsync(vm.SelectedSpec, "Ảnh In 13x18 Lab Pro");
        ok.Should().BeTrue();

        // 3. Kiểm tra database sau khi đổi tên
        var updated = await _productRepo.GetByIdAsync(initial.Id);
        updated.Should().NotBeNull();
        updated!.CanonicalName.Should().Be("Ảnh In 13x18 Lab Pro");
        updated.CanonicalSize.Should().Be("13x18");

        // 4. Kiểm tra tên cũ "13x18 in" đã được tự động bảo toàn thành alias
        updated.Aliases.Should().Contain(a => a.AliasText == "13x18 in");
    }

    [Fact]
    public async Task RenameProduct_CollisionDetection_FailsGracefully()
    {
        // '13x18 in' (13x18) và '20x30' (20x30) đã có sẵn trong cùng dòng PhotoPrint
        var allSpecs = await _productRepo.GetAllAsync();
        var spec13x18 = allSpecs.First(s => s.CanonicalName == "13x18 in");

        var vm = new PriceListViewModel(_productRepo);
        await vm.LoadSpecificationsAsync();

        vm.SelectedSpec = vm.Specifications.First(s => s.Id == spec13x18.Id);

        // Đổi '13x18 in' thành '20x30 in' (sẽ trùng khổ chuẩn 20x30 với sản phẩm 20x30 đã có)
        bool ok = await vm.RenameSpecificationAsync(vm.SelectedSpec, "20x30 in");
        ok.Should().BeFalse();
        vm.StatusMessage.Should().Contain("Lỗi đổi tên");
    }

    [Fact]
    public async Task UpdateCustomer_UpdatesNamePhoneNote_AndPreservesOldNameAsAlias()
    {
        // 1. Tạo khách hàng ban đầu với alias ban đầu
        var initial = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Văn An",
            Phone = "0901111111",
            Note = "Khách thử nghiệm"
        }, "Anh An");

        var vm = new CustomersViewModel(
            _customerRepo,
            _customerBillingService,
            _customerBillRepo,
            _jpegExporter,
            _settingsRepo
        );

        await vm.LoadCustomersAsync();
        vm.SelectedCustomer.Should().NotBeNull();
        vm.EditCustomerName.Should().Be("Văn An");
        vm.EditCustomerPhone.Should().Be("0901111111");
        vm.EditCustomerNote.Should().Be("Khách thử nghiệm");

        // 2. Chỉnh sửa thông tin
        vm.EditCustomerName = "Văn An Studio";
        vm.EditCustomerPhone = "0909999999";
        vm.EditCustomerNote = "Khách VIP in nhiều";

        await vm.SaveCustomerInfoAsync();

        // 3. Kiểm tra thông tin đã cập nhật
        var updated = await _customerRepo.GetByIdAsync(initial.Id);
        updated.Should().NotBeNull();
        updated!.CanonicalName.Should().Be("Văn An Studio");
        updated.Phone.Should().Be("0909999999");
        updated.Note.Should().Be("Khách VIP in nhiều");

        // 4. Tên cũ "Văn An" tự động được thêm vào alias bên cạnh "Anh An"
        updated.Aliases.Should().Contain(a => a.AliasText == "Văn An");
        updated.Aliases.Should().Contain(a => a.AliasText == "Anh An");
    }

    [Fact]
    public async Task LockedBills_RemainUnchanged_WhenProductOrCustomerRenamed()
    {
        var cust = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Cũ", Phone = "0123" });
        var allSpecs = await _productRepo.GetAllAsync();
        var spec = allSpecs.First(s => s.CanonicalName == "13x18 in");

        // 1. Tạo và lưu Order trước để có order.Id hợp lệ
        var order = new Order
        {
            WorkDate = "2026-09-29",
            CustomerId = cust.Id,
            OriginalFolderName = "Khách Cũ",
            RelativePath = "2026-09-29/Khách Cũ",
            Status = OrderStatus.Locked
        };
        var snapshot = new ScanSnapshot
        {
            Scope = ScanScope.Order,
            Status = ScanStatus.Success
        };
        await _orderRepo.SaveOrderAsync(order, snapshot);

        // 2. Tạo 1 bill đã chốt snapshot
        var bill = new Bill
        {
            OrderId = order.Id,
            CustomerId = cust.Id,
            Status = OrderStatus.Locked,
            Subtotal = 50000,
            LockedAt = DateTimeOffset.UtcNow,
            Lines = new()
            {
                new BillLine
                {
                    PrintSpecificationId = spec.Id,
                    ProductNameSnapshot = "13x18 in",
                    BillQuantity = 10,
                    UnitPrice = 5000,
                    LineTotal = 50000,
                    QuantityResolutionMode = QuantityResolutionMode.AutoMatch,
                    SourceScanSnapshotId = snapshot.Id
                }
            }
        };
        await _billRepo.SaveBillAsync(bill);

        // Đổi tên khách hàng và sản phẩm
        cust.CanonicalName = "Khách Đã Đổi Tên";
        cust.Phone = "0999";
        await _customerRepo.UpdateCustomerAsync(cust);

        spec.CanonicalName = "13x18 Mới Toanh";
        await _productRepo.UpdateSpecificationAsync(spec);

        // Lấy lại Bill cũ: Snapshot không bị thay đổi!
        var reloadedBill = await _billRepo.GetBillByOrderIdAsync(order.Id);
        reloadedBill.Should().NotBeNull();
        reloadedBill!.Lines.First().ProductNameSnapshot.Should().Be("13x18 in");
        reloadedBill.Lines.First().LineTotal.Should().Be(50000);
    }

    [Fact]
    public async Task DeleteSpecification_NoHistory_HardDeletes()
    {
        // 1. Tạo 1 quy cách mới chưa từng có đơn hàng nào
        var spec = await _productRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "11x14 in",
            CanonicalSize = "11x14",
            UnitPrice = 7000
        }, "11x14");

        var vm = new PriceListViewModel(_productRepo);
        await vm.LoadSpecificationsAsync();
        vm.SelectedSpec = vm.Specifications.First(s => s.Id == spec.Id);

        // 2. Xóa quy cách
        bool ok = await vm.DeleteSelectedSpecAsync();
        ok.Should().BeTrue();

        // 3. Kiểm tra: Bản ghi bị hard delete hoàn toàn
        var reloaded = await _productRepo.GetByIdAsync(spec.Id);
        reloaded.Should().BeNull();

        await vm.LoadSpecificationsAsync();
        vm.Specifications.Should().NotContain(s => s.Id == spec.Id);
    }

    [Fact]
    public async Task DeleteSpecification_WithHistory_SoftDeletes_HidingFromCatalog()
    {
        // 1. Lấy quy cách 13x18 in đã có trong đơn hàng
        var allSpecs = await _productRepo.GetAllAsync();
        var spec = allSpecs.First(s => s.CanonicalName == "13x18 in");

        var cust = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Test Xóa Spec" });
        var order = new Order { WorkDate = "2026-09-29", CustomerId = cust.Id, RelativePath = "2026-09-29/KhachTestXoa" };
        var snapshot = new ScanSnapshot { Scope = ScanScope.Order, Status = ScanStatus.Success };
        await _orderRepo.SaveOrderAsync(order, snapshot);

        var bill = new Bill
        {
            OrderId = order.Id,
            CustomerId = cust.Id,
            Lines = new()
            {
                new BillLine
                {
                    PrintSpecificationId = spec.Id,
                    BillQuantity = 5,
                    UnitPrice = 5000,
                    LineTotal = 25000,
                    QuantityResolutionMode = QuantityResolutionMode.AutoMatch,
                    SourceScanSnapshotId = snapshot.Id
                }
            }
        };
        await _billRepo.SaveBillAsync(bill);

        var vm = new PriceListViewModel(_productRepo);
        await vm.LoadSpecificationsAsync();
        vm.SelectedSpec = vm.Specifications.First(s => s.Id == spec.Id);

        // 2. Xóa quy cách đã có lịch sử
        bool ok = await vm.DeleteSelectedSpecAsync();
        ok.Should().BeTrue();

        // 3. Kiểm tra: Quy cách bị soft delete (is_active = false)
        var rawSpec = await _productRepo.GetByIdAsync(spec.Id);
        rawSpec.Should().NotBeNull();
        rawSpec!.IsActive.Should().BeFalse();

        // Bảng giá hiển thị không còn chứa quy cách này
        await vm.LoadSpecificationsAsync();
        vm.Specifications.Should().NotContain(s => s.Id == spec.Id);
    }

    [Fact]
    public async Task DeleteCustomer_NoHistory_HardDeletes()
    {
        // 1. Tạo khách hàng mới tạo nhầm chưa có đơn hàng
        var cust = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Khách Tạo Nhầm",
            Phone = "0987654321"
        }, "TaoNham");

        var vm = new CustomersViewModel(
            _customerRepo,
            _customerBillingService,
            _customerBillRepo,
            _jpegExporter,
            _settingsRepo
        );
        await vm.LoadCustomersAsync();
        vm.SelectedCustomer = vm.AllCustomers.First(c => c.Id == cust.Id);

        // 2. Xóa khách hàng
        bool ok = await vm.DeleteCustomerAsync();
        ok.Should().BeTrue();

        // 3. Kiểm tra: Khách hàng và alias bị hard delete
        var reloaded = await _customerRepo.GetByIdAsync(cust.Id);
        reloaded.Should().BeNull();

        await vm.LoadCustomersAsync();
        vm.AllCustomers.Should().NotContain(c => c.Id == cust.Id);
    }

    [Fact]
    public async Task DeleteCustomer_WithHistory_IsBlocked()
    {
        // 1. Tạo khách hàng đã có đơn hàng
        var cust = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Có Lịch Sử" });
        var order = new Order { WorkDate = "2026-09-29", CustomerId = cust.Id, RelativePath = "2026-09-29/KhachCoLichSu" };
        var snapshot = new ScanSnapshot { Scope = ScanScope.Order, Status = ScanStatus.Success };
        await _orderRepo.SaveOrderAsync(order, snapshot);

        var vm = new CustomersViewModel(
            _customerRepo,
            _customerBillingService,
            _customerBillRepo,
            _jpegExporter,
            _settingsRepo
        );
        await vm.LoadCustomersAsync();
        vm.SelectedCustomer = vm.AllCustomers.First(c => c.Id == cust.Id);

        // 2. Thực hiện xóa khách hàng
        bool ok = await vm.DeleteCustomerAsync();
        ok.Should().BeFalse();
        vm.StatusMessage.Should().Contain("Không thể xóa khách hàng");

        // 3. Kiểm tra: Khách hàng vẫn còn nguyên trong cơ sở dữ liệu
        var reloaded = await _customerRepo.GetByIdAsync(cust.Id);
        reloaded.Should().NotBeNull();
        reloaded!.CanonicalName.Should().Be("Khách Có Lịch Sử");
    }
}
