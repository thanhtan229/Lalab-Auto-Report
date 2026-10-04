using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.Reporting;
using Xunit;

namespace LalabAutoReport.Tests;

public class ShippingLabelTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteCustomerRepository _customerRepo;
    private readonly SqliteCustomerBillRepository _customerBillRepo;
    private readonly SqliteSettingsRepository _settingsRepo;

    public ShippingLabelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_ShippingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _dbPath = Path.Combine(_tempRoot, "test_shipping.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _customerRepo = new SqliteCustomerRepository(_connectionFactory);
        _customerBillRepo = new SqliteCustomerBillRepository(_connectionFactory);
        _settingsRepo = new SqliteSettingsRepository(_connectionFactory);
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

    [Fact]
    public async Task CustomerAddress_CanBeSavedAndRetrieved_InCustomerRepository()
    {
        // 1. Create customer with address
        var customer = new Customer
        {
            CanonicalName = "Studio Tuấn Hải",
            Phone = "0988776655",
            Address = "Số 123 Phố Huế, Hai Bà Trưng, Hà Nội",
            Note = "Giao giờ hành chính"
        };

        var created = await _customerRepo.CreateCustomerAsync(customer);
        created.Address.Should().Be("Số 123 Phố Huế, Hai Bà Trưng, Hà Nội");

        // 2. Fetch from DB
        var fetched = await _customerRepo.GetByIdAsync(created.Id);
        fetched.Should().NotBeNull();
        fetched!.Address.Should().Be("Số 123 Phố Huế, Hai Bà Trưng, Hà Nội");

        // 3. Update customer address
        fetched.Address = "Tòa Discovery Complex, 302 Cầu Giấy, Hà Nội";
        await _customerRepo.UpdateCustomerAsync(fetched);

        var updated = await _customerRepo.GetByIdAsync(created.Id);
        updated!.Address.Should().Be("Tòa Discovery Complex, 302 Cầu Giấy, Hà Nội");
    }

    [Fact]
    public async Task ShippingAddressSnapshot_CanBeSavedAndRetrieved_InCustomerBillRepository()
    {
        var bill = new CustomerBill
        {
            BillNumber = "BILL-20261001-9999",
            CustomerNameSnapshot = "Studio Kim Long",
            PhoneSnapshot = "0912345678",
            ShippingAddressSnapshot = "Số 88 Lê Duẩn, Hoàn Kiếm, Hà Nội",
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Draft,
            ProductSubtotal = 500000,
            GrandTotal = 500000,
            IsPaid = false
        };

        await _customerBillRepo.SaveBillAsync(bill);
        bill.Id.Should().BeGreaterThan(0);

        var fetched = await _customerBillRepo.GetByIdAsync(bill.Id);
        fetched.Should().NotBeNull();
        fetched!.ShippingAddressSnapshot.Should().Be("Số 88 Lê Duẩn, Hoàn Kiếm, Hà Nội");
    }

    [Fact]
    public async Task WorkshopSettings_CanBeSavedAndRetrieved_InSettingsRepository()
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        settings.WorkshopName = "XƯỞNG IN ẢNH NGHỆ THUẬT LALAB PRO";
        settings.WorkshopPhone = "0909.123.456";
        settings.WorkshopAddress = "Số 10 Tràng Thi, Hoàn Kiếm, Hà Nội";

        await _settingsRepo.SaveSettingsAsync(settings);

        var loaded = await _settingsRepo.GetSettingsAsync();
        loaded.WorkshopName.Should().Be("XƯỞNG IN ẢNH NGHỆ THUẬT LALAB PRO");
        loaded.WorkshopPhone.Should().Be("0909.123.456");
        loaded.WorkshopAddress.Should().Be("Số 10 Tràng Thi, Hoàn Kiếm, Hà Nội");
    }

    [Fact]
    public async Task WpfShippingLabelExporter_ExportShippingLabelImage_CreatesValidImageFile()
    {
        var bill = new CustomerBill
        {
            BillNumber = "BILL-20261001-0088",
            CustomerNameSnapshot = "Studio Mai Vàng",
            PhoneSnapshot = "0987654321",
            ShippingAddressSnapshot = "Số 25 Lạc Long Quân, Tây Hồ, Hà Nội",
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Locked,
            ProductSubtotal = 320000,
            GrandTotal = 320000,
            IsPaid = false,
            Note = "Hàng dễ vỡ, xin nhẹ tay"
        };

        var order = new CustomerBillOrder
        {
            OrderId = 1,
            OrderNameSnapshot = "Bộ ảnh cưới 60x90",
            Subtotal = 320000,
            IsIncluded = true
        };

        order.Lines.Add(new CustomerBillLine
        {
            ProductNameSnapshot = "Ảnh ép gỗ tráng gương",
            VariantSnapshot = "60x90",
            BilledQuantity = 1,
            BilledUnitPrice = 250000,
            LineTotal = 250000,
            IsIncluded = true
        });

        order.Lines.Add(new CustomerBillLine
        {
            ProductNameSnapshot = "Ảnh để bàn",
            VariantSnapshot = "15x21",
            BilledQuantity = 2,
            BilledUnitPrice = 35000,
            LineTotal = 70000,
            IsIncluded = true
        });

        bill.Orders.Add(order);

        var exporter = new WpfShippingLabelExporter(_settingsRepo);
        string exportDir = Path.Combine(_tempRoot, "Labels");

        string imagePath = await exporter.ExportShippingLabelImageAsync(bill, exportDir);

        File.Exists(imagePath).Should().BeTrue();
        new FileInfo(imagePath).Length.Should().BeGreaterThan(1000);
        Path.GetExtension(imagePath).Should().Be(".png");
    }

    [Fact]
    public void WpfShippingLabelExporter_PaidAndUnpaidBills_RenderDrawingVisuals()
    {
        var settings = new AppSettings
        {
            WorkshopName = "LALAB TEST WORKSHOP",
            WorkshopPhone = "0911223344",
            EnableVietQrOnBill = true,
            BankAccountNumber = "123456789"
        };

        var billUnpaid = new CustomerBill
        {
            BillNumber = "BILL-UNPAID-01",
            CustomerNameSnapshot = "Khách Chưa Trả Tiền",
            GrandTotal = 150000,
            IsPaid = false
        };

        var billPaid = new CustomerBill
        {
            BillNumber = "BILL-PAID-01",
            CustomerNameSnapshot = "Khách Đã Chuyển Khoản",
            GrandTotal = 150000,
            IsPaid = true
        };

        // Render unpaid visual
        var visualUnpaid = WpfShippingLabelExporter.CreateLabelVisual(billUnpaid, settings);
        visualUnpaid.Should().NotBeNull();

        // Render paid visual
        var visualPaid = WpfShippingLabelExporter.CreateLabelVisual(billPaid, settings);
        visualPaid.Should().NotBeNull();
    }

    [Fact]
    public async Task SettingsRepository_ThermalPrinterAndAutoDeliverySettings_CanBeSavedAndRetrieved()
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        settings.ThermalPrinterName = "Xprinter XP-350B (75x100mm)";
        settings.AutoMarkDeliveredOnPrint = true;

        await _settingsRepo.SaveSettingsAsync(settings);

        var loaded = await _settingsRepo.GetSettingsAsync();
        loaded.ThermalPrinterName.Should().Be("Xprinter XP-350B (75x100mm)");
        loaded.AutoMarkDeliveredOnPrint.Should().BeTrue();

        // Test updating to false / different printer
        loaded.ThermalPrinterName = "HPRT N41";
        loaded.AutoMarkDeliveredOnPrint = false;
        await _settingsRepo.SaveSettingsAsync(loaded);

        var loaded2 = await _settingsRepo.GetSettingsAsync();
        loaded2.ThermalPrinterName.Should().Be("HPRT N41");
        loaded2.AutoMarkDeliveredOnPrint.Should().BeFalse();
    }

    [Fact]
    public void WpfShippingLabelExporter_CreateOrderLabelVisual_And_TestSample_RenderValidVisuals()
    {
        var settings = new AppSettings
        {
            WorkshopName = "XƯỞNG IN ẢNH LALAB",
            WorkshopPhone = "0988.111.222",
            WorkshopAddress = "123 Đường Láng, Đống Đa, Hà Nội",
            EnableVietQrOnBill = true,
            BankAccountNumber = "99998888",
            BankBinOrCode = "970422"
        };

        var order = new Order
        {
            Id = 55,
            WorkDate = "2026-10-01",
            OrderCode = "ORD-20261001-0055",
            OriginalFolderName = "Studio Hoàng Phúc",
            Customer = new Customer
            {
                CanonicalName = "Studio Hoàng Phúc",
                Phone = "0933.444.555",
                Address = "456 Giải Phóng, Thanh Xuân, Hà Nội"
            },
            Note = "Giao buổi sáng, gọi trước 15 phút",
            Items = new System.Collections.Generic.List<OrderItemScan>
            {
                new OrderItemScan
                {
                    SpecificationFolderName = "13x18 Go",
                    PrintCount = 20,
                    BillQuantity = 20
                },
                new OrderItemScan
                {
                    SpecificationFolderName = "60x90 Cong",
                    PrintCount = 1,
                    BillQuantity = 1
                }
            }
        };

        // 1. Render order visual
        var orderVisual = WpfShippingLabelExporter.CreateOrderLabelVisual(order, settings);
        orderVisual.Should().NotBeNull();

        // 2. Render test sample label visual
        var sampleVisual = WpfShippingLabelExporter.CreateTestSampleLabelVisual(settings, "Máy In Mẫu");
        sampleVisual.Should().NotBeNull();
    }

    [Fact]
    public async Task WpfShippingLabelExporter_ExportOrderShippingLabelImage_CreatesValidImageFile()
    {
        var order = new Order
        {
            Id = 66,
            WorkDate = "2026-10-01",
            OrderCode = "ORD-20261001-0066",
            OriginalFolderName = "Studio Ánh Dương",
            Customer = new Customer
            {
                CanonicalName = "Studio Ánh Dương",
                Phone = "0977.888.999",
                Address = "789 Hoàng Hoa Thám, Ba Đình, Hà Nội"
            },
            Note = "Đóng gói cẩn thận chống nước"
        };

        var exporter = new WpfShippingLabelExporter(_settingsRepo);
        string exportDir = Path.Combine(_tempRoot, "OrderLabels");

        string imagePath = await exporter.ExportOrderShippingLabelImageAsync(order, exportDir);

        File.Exists(imagePath).Should().BeTrue();
        new FileInfo(imagePath).Length.Should().BeGreaterThan(1000);
        Path.GetExtension(imagePath).Should().Be(".png");
    }
}

