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

public class OrderDetectionTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();

    private class TestSettingsRepo : LalabAutoReport.Core.Interfaces.ISettingsRepository
    {
        private readonly string _rootFolder;
        public TestSettingsRepo(string rootFolder) => _rootFolder = rootFolder;
        public Task<AppSettings> GetSettingsAsync(System.Threading.CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettings
            {
                RootFolder = _rootFolder,
                SupportedExtensions = new() { ".jpg", ".jpeg", ".png", ".tif", ".tiff" }
            });
        public Task SaveSettingsAsync(AppSettings settings, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task LegacyStructure_CustomerToProduct_CreatesImplicitOrder()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "legacy_order.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var repo = new SqliteProductRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Anh An" });

        // Structure: Date / Customer / Product
        fixture.CreateFile(@"2026-09-29\Anh An\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Anh An\Album 20x20\sheet1.jpg");

        var parser = new FolderStructureParser(_fileSystem, repo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(repo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        orders.Should().HaveCount(1);
        var order = orders[0];
        order.OrderKind.Should().Be(OrderKind.Implicit);
        order.OrderName.Should().Be("Anh An");
        order.OriginalFolderName.Should().Be("Anh An");
        order.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExplicitStructure_CustomerToOrderToProduct_CreatesExplicitOrders()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "explicit_order.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var repo = new SqliteProductRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Anh An" });

        // Structure: Date / Customer / Order / Product
        fixture.CreateFile(@"2026-09-29\Anh An\Don 01\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Anh An\Don 01\Album 20x20\sheet1.jpg");
        fixture.CreateFile(@"2026-09-29\Anh An\Don 02\20x30\img2.jpg");

        var parser = new FolderStructureParser(_fileSystem, repo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(repo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        orders.Should().HaveCount(2);
        orders.Should().OnlyContain(o => o.OrderKind == OrderKind.Explicit);

        var don01 = orders.First(o => o.OrderName == "Don 01");
        don01.Items.Should().HaveCount(2);

        var don02 = orders.First(o => o.OrderName == "Don 02");
        don02.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task MultipleCustomerAliases_MapToOneCustomer_RemainSeparateOrders()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "alias_orders.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var repo = new SqliteProductRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);

        // Canonical customer: Văn An with aliases Anh An and A.An
        var vanAn = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Văn An" });
        await custRepo.AddAliasAsync(vanAn.Id, "Anh An");
        await custRepo.AddAliasAsync(vanAn.Id, "A.An");

        // 3 physical folders for same customer on same date
        fixture.CreateFile(@"2026-09-29\Văn An\13x18 in\f1.jpg");
        fixture.CreateFile(@"2026-09-29\Anh An\Don 01\Album 20x20\sheet1.jpg");
        fixture.CreateFile(@"2026-09-29\A.An\Don 02\20x30\f2.jpg");

        var parser = new FolderStructureParser(_fileSystem, repo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(repo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        // Must remain 3 independent orders
        orders.Should().HaveCount(3);
        orders.Should().OnlyContain(o => o.CustomerId == vanAn.Id);

        // Calculate bills
        var billing = new BillingService(orderRepo, billRepo, repo, custRepo);
        foreach (var order in orders)
        {
            await billing.CalculateBillForOrderAsync(order.Id);
        }

        // Daily customer aggregation must combine all 3 orders into one customer total
        var aggregations = await billing.GetDailyCustomerAggregationAsync("2026-09-29");
        aggregations.Should().HaveCount(1);
        aggregations[0].CustomerId.Should().Be(vanAn.Id);
        aggregations[0].DisplayName.Should().Be("Văn An");
        aggregations[0].Orders.Should().HaveCount(3);
        aggregations[0].Bills.Should().HaveCount(3);
    }

    [Fact]
    public async Task ProductBoundary_RetouchSubfoldersNeverTreatedAsOrders()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "boundary.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var repo = new SqliteProductRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio C" });

        // Retouch deep chain inside Album 20x20: lan1 / sua / final
        fixture.CreateFile(@"2026-09-29\Studio C\Album 20x20\lan1\sua\final\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Studio C\Album 20x20\lan1\sua\final\img2.jpg");

        var parser = new FolderStructureParser(_fileSystem, repo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(repo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        // Exactly 1 order, exactly 1 product job (Album 20x20)
        orders.Should().HaveCount(1);
        orders[0].Items.Should().HaveCount(1);
        orders[0].Items[0].SpecificationFolderName.Should().Be("Album 20x20");
        orders[0].Items[0].PrintCount.Should().Be(2);
    }

    [Fact]
    public void SpecificationSubfolder_ResolvedToParentCustomerOrder_NotProductOrder()
    {
        using var fixture = new TestFileSystemFixture();
        fixture.CreateFile(@"2026-09-29\Quang Studio\40x60 TG\ban_mau_am\f1.jpg");
        fixture.CreateFile(@"2026-09-29\Quang Studio\40x60 TG\ban_mau_am\f2.jpg");

        var parser = new FolderStructureParser(_fileSystem, null);

        // 1. DiscoverOrdersInFolder on the product subfolder (e.g. QuickBill or context menu)
        string subfolderPath = Path.Combine(fixture.RootPath, @"2026-09-29\Quang Studio\40x60 TG");
        var ordersFromFolder = parser.DiscoverOrdersInFolder(fixture.RootPath, subfolderPath, "2026-09-29");

        ordersFromFolder.Should().HaveCount(1);
        ordersFromFolder[0].OriginalCustomerFolderName.Should().Be("Quang Studio");
        ordersFromFolder[0].OrderName.Should().Be("Quang Studio");
        ordersFromFolder[0].RelativePath.Should().Be(@"2026-09-29\Quang Studio");
        ordersFromFolder[0].Specifications.Should().HaveCount(1);
        ordersFromFolder[0].Specifications[0].FolderName.Should().Be("40x60 TG");

        // 2. DiscoverSingleOrder on the product subfolder path
        var singleFromSpec = parser.DiscoverSingleOrder(fixture.RootPath, @"2026-09-29\Quang Studio\40x60 TG");
        singleFromSpec.Should().NotBeNull();
        singleFromSpec!.OriginalCustomerFolderName.Should().Be("Quang Studio");
        singleFromSpec.RelativePath.Should().Be(@"2026-09-29\Quang Studio");
        singleFromSpec.Specifications.Should().HaveCount(1);
        singleFromSpec.Specifications[0].FolderName.Should().Be("40x60 TG");

        // 3. DiscoverSingleOrder on deep leaf path
        var singleFromLeaf = parser.DiscoverSingleOrder(fixture.RootPath, @"2026-09-29\Quang Studio\40x60 TG\ban_mau_am");
        singleFromLeaf.Should().NotBeNull();
        singleFromLeaf!.OriginalCustomerFolderName.Should().Be("Quang Studio");
        singleFromLeaf.RelativePath.Should().Be(@"2026-09-29\Quang Studio");
        singleFromLeaf.Specifications.Should().HaveCount(1);
        singleFromLeaf.Specifications[0].FolderName.Should().Be("40x60 TG");
    }

    [Fact]
    public async Task ScanDateAsync_PrunesObsoleteUnbilledOrders_PreservesLockedOrders()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "prune_test.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var repo = new SqliteProductRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Quang Studio" });

        // Real order on disk
        fixture.CreateFile(@"2026-09-29\Quang Studio\40x60 TG\img.jpg");

        // Obsolete unlocked order in DB that no longer exists on disk
        var obsoleteOrder = new Order
        {
            WorkDate = "2026-09-29",
            OriginalFolderName = "Deleted Customer",
            RelativePath = @"2026-09-29\Deleted Customer",
            Status = OrderStatus.NeedsReview,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await orderRepo.SaveOrderAsync(obsoleteOrder, new ScanSnapshot
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Scope = ScanScope.Date,
            Status = ScanStatus.Success
        });

        // Locked order in DB that is not on disk (historical snapshot must never be pruned)
        var lockedOrder = new Order
        {
            WorkDate = "2026-09-29",
            OriginalFolderName = "Historical Order",
            RelativePath = @"2026-09-29\Historical Order",
            Status = OrderStatus.Locked,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await orderRepo.SaveOrderAsync(lockedOrder, new ScanSnapshot
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Scope = ScanScope.Date,
            Status = ScanStatus.Success
        });

        var parser = new FolderStructureParser(_fileSystem, repo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(repo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var scanResults = await scanner.ScanDateAsync("2026-09-29");

        // Scan results should only contain current disk orders
        scanResults.Should().HaveCount(1);
        scanResults[0].OriginalFolderName.Should().Be("Quang Studio");

        // Verify DB state
        var dbOrders = await orderRepo.GetOrdersByDateAsync("2026-09-29");
        dbOrders.Should().NotContain(o => o.Id == obsoleteOrder.Id, "Obsolete unlocked order should have been pruned");
        dbOrders.Should().Contain(o => o.Id == lockedOrder.Id, "Locked historical order must be preserved");
    }

    [Fact]
    public async Task NumberedOrderFolders_SuchAsDon2030_AreRecognizedAsExplicitOrdersNotProducts()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "numbered_order.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var repo = new SqliteProductRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Quang Studio" });

        // Structure: Customer has explicit order folders named with dimension-like numbers
        fixture.CreateFile(@"2026-09-29\Quang Studio\Don 20-30\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-29\Quang Studio\Order 10-15\Album 20x20\sheet1.jpg");
        fixture.CreateFile(@"2026-09-29\Quang Studio\Hop 20-30\13x18 in\img2.jpg");

        var parser = new FolderStructureParser(_fileSystem, repo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(repo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        orders.Should().HaveCount(3);
        orders.Should().OnlyContain(o => o.OrderKind == OrderKind.Explicit);

        var don2030 = orders.FirstOrDefault(o => o.OrderName == "Don 20-30");
        don2030.Should().NotBeNull();
        don2030!.Items.Should().HaveCount(1);
        don2030.Items[0].SpecificationFolderName.Should().Be("13x18 in");

        var order1015 = orders.FirstOrDefault(o => o.OrderName == "Order 10-15");
        order1015.Should().NotBeNull();
        order1015!.Items.Should().HaveCount(1);
        order1015.Items[0].SpecificationFolderName.Should().Be("Album 20x20");

        var hop2030 = orders.FirstOrDefault(o => o.OrderName == "Hop 20-30");
        hop2030.Should().NotBeNull();
        hop2030!.Items.Should().HaveCount(1);
        hop2030.Items[0].SpecificationFolderName.Should().Be("13x18 in");
    }

    [Theory]
    [InlineData("Don 20-30", false)]
    [InlineData("Đơn 20-30", false)]
    [InlineData("Order 10-15", false)]
    [InlineData("Hop 20-30", false)]
    [InlineData("Hộp 20-30", false)]
    [InlineData("Set 20-30", false)]
    [InlineData("20x30", true)]
    [InlineData("20-30", true)]
    [InlineData("Album 20x20", true)]
    [InlineData("In 13x18", true)]
    public void HeuristicIsProductFolder_DistinguishesOrdersFromProducts(string folderName, bool expectedIsProduct)
    {
        bool actual = FolderStructureParser.HeuristicIsProductFolder(folderName);
        actual.Should().Be(expectedIsProduct);
    }
}

