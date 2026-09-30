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

public class ReportServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteOrderRepository _orderRepo;
    private readonly SqliteBillRepository _billRepo;
    private readonly SqliteCustomerBillRepository _customerBillRepo;
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
    private readonly ReportService _reportService;

    public ReportServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_ReportTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _dbPath = Path.Combine(_tempRoot, "test_lalab.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _orderRepo = new SqliteOrderRepository(_connectionFactory);
        _billRepo = new SqliteBillRepository(_connectionFactory);
        _customerBillRepo = new SqliteCustomerBillRepository(_connectionFactory);
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

        _reportService = new ReportService(
            _orderRepo,
            _billRepo,
            _billingService,
            _specRepo,
            _customerRepo,
            _scanService,
            _customerBillRepo
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
            // best effort cleanup
        }
    }

    [Fact]
    public async Task Customer_With_Three_Physical_Folders_Appears_As_Three_Orders_Plus_Aggregated_Total()
    {
        // 1. Create canonical customer with aliases: "Văn An", "Anh An", "A.An"
        var cust = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Văn An" }, "Văn An");
        await _customerRepo.AddAliasAsync(cust.Id, "Anh An");
        await _customerRepo.AddAliasAsync(cust.Id, "A.An");

        string date = "2026-09-28";

        // Order 1: "Văn An" -> 13x18 in (25 prints * 5000 = 125,000 VND)
        CreateOrderFixture(date, "Văn An", "13x18 in", 25, 25);

        // Order 2: "Anh An" -> 13x18 in (16 prints * 5000 = 80,000 VND)
        CreateOrderFixture(date, "Anh An", "13x18 in", 16, 16);

        // Order 3: "A.An" -> 20x30 (14 prints * 15000 = 210,000 VND)
        CreateOrderFixture(date, "A.An", "20x30", 14, 14);

        // Scan all 3 orders
        var scanned = await _scanService.ScanDateAsync(date);
        scanned.Should().HaveCount(3);

        // Calculate bills
        foreach (var order in scanned)
        {
            await _billingService.CalculateBillForOrderAsync(order.Id);
        }

        // Action: Generate Daily Report
        var dailyReport = await _reportService.GetDailyReportAsync(date);

        // Assert
        dailyReport.TotalOrders.Should().Be(3);
        dailyReport.TotalCustomers.Should().Be(1); // Grouped under canonical "Văn An"
        dailyReport.TotalBillQuantity.Should().Be(55); // 25 + 16 + 14
        dailyReport.TotalAmount.Should().Be(415000); // 125,000 + 80,000 + 210,000

        var vanAnSummary = dailyReport.Customers.Should().ContainSingle().Subject;
        vanAnSummary.DisplayName.Should().Be("Văn An");
        vanAnSummary.Orders.Should().HaveCount(3);
        vanAnSummary.TotalAmount.Should().Be(415000);

        // Check spec breakdowns
        dailyReport.Specifications.Should().HaveCount(2);
        var spec13 = dailyReport.Specifications.First(s => s.SpecificationName == "13x18 in");
        spec13.TotalBillQuantity.Should().Be(41);
        spec13.TotalAmount.Should().Be(205000);

        var spec20 = dailyReport.Specifications.First(s => s.SpecificationName == "20x30");
        spec20.TotalBillQuantity.Should().Be(14);
        spec20.TotalAmount.Should().Be(210000);
    }

    [Fact]
    public async Task MonthlyReport_Operates_From_Database_Without_Scanning_Raw_Filesystem()
    {
        // Setup data in DB
        var cust = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Mai" }, "Studio Mai");
        string date = "2026-09-15";
        CreateOrderFixture(date, "Studio Mai", "13x18 in", 10, 10);

        var scanned = await _scanService.ScanDateAsync(date);
        await _billingService.CalculateBillForOrderAsync(scanned[0].Id);

        // Delete root folder to prove report does NOT scan the filesystem
        string ordersDir = Path.Combine(_tempRoot, date);
        Directory.Delete(ordersDir, true);

        // Action: Monthly report should query SQLite cleanly
        var report = await _reportService.GetMonthlyReportAsync(2026, 9);

        // Assert
        report.Should().NotBeNull();
        report.TotalOrders.Should().Be(1);
        report.TotalAmount.Should().Be(50000); // 10 * 5000 VND
        report.TotalBillQuantity.Should().Be(10);
        report.CustomerSummaries.Should().HaveCount(1);
        report.CustomerSummaries[0].DisplayName.Should().Be("Studio Mai");
    }

    [Fact]
    public async Task MonthlyReport_Identifies_Missing_Scan_Days()
    {
        // Create 3 date folders on disk
        string day1 = Path.Combine(_tempRoot, "2026-09-01", "Order1", "13x18 in");
        string day2 = Path.Combine(_tempRoot, "2026-09-02", "Order2", "13x18 in");
        string day3 = Path.Combine(_tempRoot, "2026-09-03", "Order3", "13x18 in");
        Directory.CreateDirectory(day1);
        Directory.CreateDirectory(day2);
        Directory.CreateDirectory(day3);

        File.WriteAllBytes(Path.Combine(day3, "img.jpg"), new byte[] { 1 });

        // Scan only day 3
        await _scanService.ScanDateAsync("2026-09-03");

        // Action: Monthly report
        var report = await _reportService.GetMonthlyReportAsync(2026, 9);

        // Assert: Days 01 and 02 are reported as missing scan days
        report.MissingScanDays.Should().Contain("2026-09-01");
        report.MissingScanDays.Should().Contain("2026-09-02");
        report.MissingScanDays.Should().NotContain("2026-09-03");
    }

    [Fact]
    public async Task DateRangeReport_Accurately_Filters_And_Aggregates()
    {
        var cust = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Lan Vy" }, "Lan Vy");

        // Date 1: 2026-09-10 (Inside range)
        CreateOrderFixture("2026-09-10", "Lan Vy", "13x18 in", 4, 4);
        var s1 = await _scanService.ScanDateAsync("2026-09-10");
        await _billingService.CalculateBillForOrderAsync(s1[0].Id);

        // Date 2: 2026-09-12 (Inside range)
        CreateOrderFixture("2026-09-12", "Lan Vy", "20x30", 2, 2);
        var s2 = await _scanService.ScanDateAsync("2026-09-12");
        await _billingService.CalculateBillForOrderAsync(s2[0].Id);

        // Date 3: 2026-09-25 (Outside range)
        CreateOrderFixture("2026-09-25", "Lan Vy", "13x18 in", 10, 10);
        var s3 = await _scanService.ScanDateAsync("2026-09-25");
        await _billingService.CalculateBillForOrderAsync(s3[0].Id);

        // Action: Query range 2026-09-09 to 2026-09-15
        var report = await _reportService.GetDateRangeReportAsync("2026-09-09", "2026-09-15");

        // Assert
        report.TotalOrders.Should().Be(2); // Only Sept 10 and Sept 12
        report.TotalBillQuantity.Should().Be(6); // 4 + 2
        report.TotalAmount.Should().Be(50000); // 4*5000 + 2*15000 = 20,000 + 30,000 = 50,000 VND
        report.DailySummaries.Should().HaveCount(2);
    }

    [Fact]
    public async Task ScanDate_WithoutManualBilling_ReportReflectsRevenueAndQuantityFromScanServiceAutoCalculate()
    {
        var cust = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Kim" }, "Studio Kim");
        string date = "2026-09-20";
        CreateOrderFixture(date, "Studio Kim", "13x18 in", 8, 8);

        // Scan date — ScanService now automatically calculates bill for Ready orders
        var scanned = await _scanService.ScanDateAsync(date);
        scanned.Should().HaveCount(1);
        scanned[0].Status.Should().Be(OrderStatus.Ready);

        // Daily report
        var daily = await _reportService.GetDailyReportAsync(date);
        daily.TotalOrders.Should().Be(1);
        daily.TotalBillQuantity.Should().Be(8);
        daily.TotalAmount.Should().Be(40000); // 8 * 5000 VND

        // Monthly report
        var monthly = await _reportService.GetMonthlyReportAsync(2026, 9);
        monthly.TotalOrders.Should().Be(1);
        monthly.TotalBillQuantity.Should().Be(8);
        monthly.TotalAmount.Should().Be(40000);
    }

    [Fact]
    public async Task CustomerBill_WithAdjustments_ReportReflectsGrandTotalAndQuantities()
    {
        var cust = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Mai" }, "Studio Mai");
        string date = "2026-09-22";
        CreateOrderFixture(date, "Studio Mai", "13x18 in", 10, 10);

        var scanned = await _scanService.ScanDateAsync(date);

        var billLine = new CustomerBillLine
        {
            OrderId = scanned[0].Id,
            ProductJobId = scanned[0].Items[0].Id,
            ProductNameSnapshot = "13x18 in",
            BilledQuantity = 10,
            BilledUnitPrice = 5000,
            LineTotal = 50000,
            IsIncluded = true
        };

        var bill = new CustomerBill
        {
            BillNumber = "BILL-20260922-0001",
            BillType = BillType.Customer,
            CustomerId = cust.Id,
            CustomerNameSnapshot = "Studio Mai",
            PeriodStart = date,
            PeriodEnd = date,
            Status = CustomerBillStatus.Locked,
            ProductSubtotal = 50000,
            AdjustmentsTotal = 20000, // +30k shipping, -10k discount
            GrandTotal = 70000,
            Orders = new()
            {
                new CustomerBillOrder
                {
                    OrderId = scanned[0].Id,
                    OrderDateSnapshot = date,
                    OrderNameSnapshot = "Studio Mai",
                    OriginalFolderNameSnapshot = "Studio Mai",
                    Subtotal = 50000,
                    IsIncluded = true,
                    Lines = new() { billLine }
                }
            },
            Lines = new() { billLine },
            Adjustments = new()
            {
                new BillAdjustment { Type = AdjustmentType.Shipping, Label = "Ship", Direction = AdjustmentDirection.Add, Amount = 30000 },
                new BillAdjustment { Type = AdjustmentType.Discount, Label = "Giam gia", Direction = AdjustmentDirection.Deduct, Amount = 10000 }
            }
        };

        await _customerBillRepo.SaveBillAsync(bill);

        var daily = await _reportService.GetDailyReportAsync(date);
        daily.TotalOrders.Should().Be(1);
        daily.TotalBillQuantity.Should().Be(10);
        daily.TotalAmount.Should().Be(70000); // 50k + 20k adjustment

        var monthly = await _reportService.GetMonthlyReportAsync(2026, 9);
        monthly.TotalAmount.Should().Be(70000);
        monthly.TotalBillQuantity.Should().Be(10);
    }

    [Fact]
    public async Task MixedScenario_LockedCustomerBill_And_UnbilledReadyOrder_AggregatesWithoutDoubleCounting()
    {
        var cust1 = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Đã Chốt" }, "Khách Đã Chốt");
        var cust2 = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Chưa Chốt" }, "Khách Chưa Chốt");

        string date = "2026-09-25";
        CreateOrderFixture(date, "Khách Đã Chốt", "13x18 in", 10, 10);
        CreateOrderFixture(date, "Khách Chưa Chốt", "20x30", 2, 2);

        var scanned = await _scanService.ScanDateAsync(date);
        var order1 = scanned.First(o => o.OriginalFolderName == "Khách Đã Chốt");
        var order2 = scanned.First(o => o.OriginalFolderName == "Khách Chưa Chốt");

        // Customer 1 has locked customer bill
        var line1 = new CustomerBillLine
        {
            OrderId = order1.Id,
            ProductJobId = order1.Items[0].Id,
            ProductNameSnapshot = "13x18 in",
            BilledQuantity = 10,
            BilledUnitPrice = 5000,
            LineTotal = 50000,
            IsIncluded = true
        };

        var bill = new CustomerBill
        {
            BillNumber = "BILL-20260925-0001",
            BillType = BillType.Customer,
            CustomerId = cust1.Id,
            CustomerNameSnapshot = "Khách Đã Chốt",
            PeriodStart = date,
            PeriodEnd = date,
            Status = CustomerBillStatus.Locked,
            ProductSubtotal = 50000,
            AdjustmentsTotal = 0,
            GrandTotal = 50000,
            Orders = new()
            {
                new CustomerBillOrder
                {
                    OrderId = order1.Id,
                    OrderDateSnapshot = date,
                    OrderNameSnapshot = "Khách Đã Chốt",
                    OriginalFolderNameSnapshot = "Khách Đã Chốt",
                    Subtotal = 50000,
                    IsIncluded = true,
                    Lines = new() { line1 }
                }
            },
            Lines = new() { line1 }
        };
        await _customerBillRepo.SaveBillAsync(bill);

        // Daily report
        var daily = await _reportService.GetDailyReportAsync(date);
        daily.TotalOrders.Should().Be(2);
        // Order 1: 10 prints @ 50,000 VND
        // Order 2: 2 prints @ 30,000 VND (2 * 15,000)
        daily.TotalBillQuantity.Should().Be(12);
        daily.TotalAmount.Should().Be(80000);
        daily.Customers.Should().HaveCount(2);

        // Monthly report
        var monthly = await _reportService.GetMonthlyReportAsync(2026, 9);
        monthly.TotalOrders.Should().Be(2);
        monthly.TotalBillQuantity.Should().Be(12);
        monthly.TotalAmount.Should().Be(80000);
    }

    private void CreateOrderFixture(string date, string customerFolder, string specFolder, int sourceCount, int printCount)
    {
        string baseDir = Path.Combine(_tempRoot, date, customerFolder, specFolder);
        string printDir = Path.Combine(baseDir, "in");
        Directory.CreateDirectory(printDir);

        for (int i = 1; i <= sourceCount; i++)
        {
            File.WriteAllBytes(Path.Combine(baseDir, $"src_{i}.jpg"), new byte[] { 1 });
        }

        for (int i = 1; i <= printCount; i++)
        {
            File.WriteAllBytes(Path.Combine(printDir, $"print_{i}.jpg"), new byte[] { 1 });
        }
    }
}
