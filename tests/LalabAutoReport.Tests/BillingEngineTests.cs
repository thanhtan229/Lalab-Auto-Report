using System;
using System.Collections.Generic;
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

public class BillingEngineTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();

    private class TestSettingsRepo : LalabAutoReport.Core.Interfaces.ISettingsRepository
    {
        private readonly string _rootFolder;
        public TestSettingsRepo(string rootFolder) => _rootFolder = rootFolder;
        public Task<AppSettings> GetSettingsAsync(System.Threading.CancellationToken ct = default) =>
            Task.FromResult(new AppSettings { RootFolder = _rootFolder });
        public Task SaveSettingsAsync(AppSettings settings, System.Threading.CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task Billing_AutoMatch_CalculatesCorrectTotals()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "bill_automatch.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        // Seed customer and spec
        var cust = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Văn An" });
        var spec = (await specRepo.GetAllAsync()).First(s => s.CanonicalName == "13x18 in"); // 5,000 đ

        // Setup folder: Source=100, Print=100
        for (int i = 1; i <= 100; i++)
        {
            fixture.CreateFile($@"2026-09-28\Văn An\13x18 in\s{i}.jpg");
            fixture.CreateFile($@"2026-09-28\Văn An\13x18 in\retouch\p{i}.jpg");
        }

        var parser = new FolderStructureParser(_fileSystem);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-28");
        var order = orders[0];

        var billing = new BillingService(orderRepo, billRepo, specRepo, custRepo);
        var bill = await billing.CalculateBillForOrderAsync(order.Id);

        bill.Should().NotBeNull();
        bill.Subtotal.Should().Be(500000); // 100 * 5,000 = 500,000 VND
        bill.Lines.Should().HaveCount(1);

        var line = bill.Lines[0];
        line.SourceCount.Should().Be(100);
        line.PrintCount.Should().Be(100);
        line.BillQuantity.Should().Be(100);
        line.QuantityResolutionMode.Should().Be(QuantityResolutionMode.UsePrint);
        line.UnitPrice.Should().Be(5000);
        line.LineTotal.Should().Be(500000);
    }

    [Fact]
    public async Task Billing_V2_Calculates_From_PrintFolder_RegardlessOfSourceCount()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "bill_v2_direct.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Minh" });

        // Source = 100, Print = 98 (In V2, only print folder counts!)
        for (int i = 1; i <= 100; i++) fixture.CreateFile($@"2026-09-28\Studio Minh\13x18 in\s{i}.jpg");
        for (int i = 1; i <= 98; i++) fixture.CreateFile($@"2026-09-28\Studio Minh\13x18 in\retouch\p{i}.jpg");

        var parser = new FolderStructureParser(_fileSystem);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-28");
        var order = orders[0];

        var billing = new BillingService(orderRepo, billRepo, specRepo, custRepo);
        var bill = await billing.CalculateBillForOrderAsync(order.Id);

        bill.Should().NotBeNull();
        bill.Subtotal.Should().Be(98 * 5000);
        bill.Lines[0].BillQuantity.Should().Be(98);
    }

    [Fact]
    public async Task Billing_AlbumWithZeroPrints_MustThrowException()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "bill_album_zero.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khach Hang A" });

        // Album folder with no image files (only text file) -> PrintCount = 0
        fixture.CreateFile(@"2026-09-28\Khach Hang A\Album 20x20\readme.txt");

        var parser = new FolderStructureParser(_fileSystem);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-28");
        var order = orders[0];

        var billing = new BillingService(orderRepo, billRepo, specRepo, custRepo);

        var act = async () => await billing.CalculateBillForOrderAsync(order.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Billing_ResolutionModes_UsePrint_UseSource_Custom()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "bill_resolutions.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Hàng" });

        // Source = 100, Print = 98
        for (int i = 1; i <= 100; i++) fixture.CreateFile($@"2026-09-28\Khách Hàng\13x18 in\s{i}.jpg");
        for (int i = 1; i <= 98; i++) fixture.CreateFile($@"2026-09-28\Khách Hàng\13x18 in\retouch\p{i}.jpg");

        var parser = new FolderStructureParser(_fileSystem);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-28");
        var order = orders[0];
        long itemId = order.Items[0].Id;

        var billing = new BillingService(orderRepo, billRepo, specRepo, custRepo);

        // 1. Resolve with USE_PRINT (98)
        await orderRepo.UpdateOrderItemResolutionAsync(itemId, 98, QuantityResolutionMode.UsePrint, "Chọn bản in");
        var billPrint = await billing.CalculateBillForOrderAsync(order.Id);
        billPrint.Lines[0].BillQuantity.Should().Be(98);
        billPrint.Lines[0].QuantityResolutionMode.Should().Be(QuantityResolutionMode.UsePrint);
        billPrint.Subtotal.Should().Be(98 * 5000);

        // 2. Resolve with USE_SOURCE (100)
        await orderRepo.UpdateOrderItemResolutionAsync(itemId, 100, QuantityResolutionMode.UseSource, "In đủ file gốc");
        var billSource = await billing.CalculateBillForOrderAsync(order.Id);
        billSource.Lines[0].BillQuantity.Should().Be(100);
        billSource.Lines[0].QuantityResolutionMode.Should().Be(QuantityResolutionMode.UseSource);
        billSource.Subtotal.Should().Be(100 * 5000);

        // 3. Resolve with CUSTOM (99)
        await orderRepo.UpdateOrderItemResolutionAsync(itemId, 99, QuantityResolutionMode.Custom, "Hỏng 1 tấm bù sau");
        var billCustom = await billing.CalculateBillForOrderAsync(order.Id);
        billCustom.Lines[0].BillQuantity.Should().Be(99);
        billCustom.Lines[0].QuantityResolutionMode.Should().Be(QuantityResolutionMode.Custom);
        billCustom.Subtotal.Should().Be(99 * 5000);
    }

    [Fact]
    public async Task Billing_DailyCustomerAggregation_AggregatesThreeOrdersAccurately()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "bill_agg.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        // Canonical Customer: Văn An with aliases Anh An and A.An
        var vanAn = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Văn An" });
        await custRepo.AddAliasAsync(vanAn.Id, "Anh An");
        await custRepo.AddAliasAsync(vanAn.Id, "A.An");

        // Order 1: folder Văn An, spec 13x18 in (5,000 đ) x 25 = 125,000 đ
        for (int i = 1; i <= 25; i++)
        {
            fixture.CreateFile($@"2026-09-28\Văn An\13x18 in\s{i}.jpg");
            fixture.CreateFile($@"2026-09-28\Văn An\13x18 in\retouch\p{i}.jpg");
        }

        // Order 2: folder Anh An, spec 40x60 TG (80,000 đ) x 1 = 80,000 đ
        fixture.CreateFile(@"2026-09-28\Anh An\40x60 TG\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Anh An\40x60 TG\retouch\p1.jpg");

        // Order 3: folder A.An, spec 20x30 (15,000 đ) x 14 = 210,000 đ
        for (int i = 1; i <= 14; i++)
        {
            fixture.CreateFile($@"2026-09-28\A.An\20x30\s{i}.jpg");
            fixture.CreateFile($@"2026-09-28\A.An\20x30\retouch\p{i}.jpg");
        }

        var parser = new FolderStructureParser(_fileSystem);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var scannedOrders = await scanner.ScanDateAsync("2026-09-28");
        scannedOrders.Should().HaveCount(3);

        var billing = new BillingService(orderRepo, billRepo, specRepo, custRepo);

        // Bill each order individually
        foreach (var order in scannedOrders)
        {
            await billing.CalculateBillForOrderAsync(order.Id);
        }

        // Aggregate daily total for canonical customer "Văn An"
        var dailyAggs = await billing.GetDailyCustomerAggregationAsync("2026-09-28");

        dailyAggs.Should().HaveCount(1, "All 3 orders belong to canonical customer Văn An");
        var agg = dailyAggs[0];

        agg.DisplayName.Should().Be("Văn An");
        agg.Orders.Should().HaveCount(3, "Provenance of all 3 physical orders must be retained");
        agg.Bills.Should().HaveCount(3);

        // Totals:
        // Order 1: 125,000
        // Order 2:  80,000
        // Order 3: 210,000
        // Total = 415,000 VND (exact match to PLAN_lalab.md Section 12.2!)
        agg.TotalAmount.Should().Be(415000);
        agg.TotalBillQuantity.Should().Be(25 + 1 + 14); // 40
        agg.HasUnresolvedIssues.Should().BeFalse();
    }
}
