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

public class CustomerResolutionTests
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
    public async Task CustomerResolver_ExactAndNormalizedAlias_ShouldAutoMap()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "cust_test.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var custRepo = new SqliteCustomerRepository(connFactory);

        // Create Canonical Customer: Văn An with aliases: Anh An, A.An
        var vanAn = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Văn An" });
        await custRepo.AddAliasAsync(vanAn.Id, "Anh An");
        await custRepo.AddAliasAsync(vanAn.Id, "A.An");

        var resolver = new CustomerResolver(custRepo);

        // Test exact
        var res1 = await resolver.ResolveCustomerAsync("Văn An");
        res1.Status.Should().Be(CustomerResolutionStatus.ExactMatch);
        res1.ResolvedCustomer!.Id.Should().Be(vanAn.Id);

        // Test alias "Anh An"
        var res2 = await resolver.ResolveCustomerAsync("Anh An");
        res2.ResolvedCustomer!.Id.Should().Be(vanAn.Id);

        // Test normalized alias "a.an" with spaces / casing
        var res3 = await resolver.ResolveCustomerAsync("  A. An ");
        res3.ResolvedCustomer!.Id.Should().Be(vanAn.Id);
    }

    [Fact]
    public async Task CustomerResolver_FuzzyMatch_MustNeverAutoMerge_ReturnsSuggestionsOnly()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "fuzzy_test.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var custRepo = new SqliteCustomerRepository(connFactory);
        var vanAn = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Văn An" });

        var resolver = new CustomerResolver(custRepo);

        // "Văn Ánh" is similar but not an alias
        var res = await resolver.ResolveCustomerAsync("Văn Ánh");

        res.Status.Should().Be(CustomerResolutionStatus.FuzzySuggested);
        res.ResolvedCustomer.Should().BeNull("Fuzzy matches must NEVER automatically merge customer identity");
        res.SuggestedCustomers.Should().Contain(c => c.Id == vanAn.Id);
    }

    [Fact]
    public async Task CustomerResolver_UnknownCustomer_ShouldReturnUnresolved()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "unknown_test.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var custRepo = new SqliteCustomerRepository(connFactory);
        var resolver = new CustomerResolver(custRepo);

        var res = await resolver.ResolveCustomerAsync("Người Lạ Hoàn Toàn XYZ");
        res.Status.Should().Be(CustomerResolutionStatus.Unresolved);
        res.ResolvedCustomer.Should().BeNull();
    }

    [Fact]
    public async Task PrintSpecResolver_KnownAndUnknownSpecs()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "spec_test.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var resolver = new PrintSpecificationResolver(specRepo);

        // "13x18 in" seeded in migration
        var res1 = await resolver.ResolveSpecificationAsync("13x18 in");
        res1.Status.Should().Be(PrintSpecificationResolutionStatus.Resolved);
        res1.ResolvedSpecification!.UnitPrice.Should().Be(5000);

        // Alias
        await specRepo.AddAliasAsync(res1.ResolvedSpecification.Id, "13x18");
        var res2 = await resolver.ResolveSpecificationAsync("13x18");
        res2.Status.Should().Be(PrintSpecificationResolutionStatus.Resolved);
        res2.ResolvedSpecification!.Id.Should().Be(res1.ResolvedSpecification.Id);

        // Unknown
        var resUnknown = await resolver.ResolveSpecificationAsync("Khổ Lạ 99x99");
        resUnknown.Status.Should().Be(PrintSpecificationResolutionStatus.Unknown);
        resUnknown.ResolvedSpecification.Should().BeNull();
    }

    [Fact]
    public async Task ScanDate_SameCustomerMultipleFolders_ShouldRetainSeparateOrdersWithResolvedCustomer()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "multi_orders.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var custRepo = new SqliteCustomerRepository(connFactory);
        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);

        // CUST-0001: Văn An with aliases Anh An and A.An
        var vanAn = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Văn An" });
        await custRepo.AddAliasAsync(vanAn.Id, "Anh An");
        await custRepo.AddAliasAsync(vanAn.Id, "A.An");

        // Create 3 physical folders under 2026-09-28
        fixture.CreateFile(@"2026-09-28\Văn An\13x18 in\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Văn An\13x18 in\retouch\s1.jpg");

        fixture.CreateFile(@"2026-09-28\Anh An\20x30\s1.jpg");
        fixture.CreateFile(@"2026-09-28\Anh An\20x30\retouch\s1.jpg");

        fixture.CreateFile(@"2026-09-28\A.An\40x60 TG\s1.jpg");
        fixture.CreateFile(@"2026-09-28\A.An\40x60 TG\retouch\s1.jpg");

        var parser = new FolderStructureParser(_fileSystem);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(specRepo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(
            _fileSystem, parser, printResolver, settingsRepo,
            orderRepo, custResolver, specResolver
        );

        var orders = await scanner.ScanDateAsync("2026-09-28");

        // Verify: 3 distinct physical orders
        orders.Should().HaveCount(3);
        orders.Select(o => o.OriginalFolderName).Should().BeEquivalentTo(new[] { "Văn An", "Anh An", "A.An" });

        // Verify: ALL 3 orders resolve to Customer ID of Văn An!
        orders.All(o => o.CustomerId == vanAn.Id).Should().BeTrue("All orders must map to canonical customer CUST-0001");
        orders.All(o => o.Customer?.CanonicalName == "Văn An").Should().BeTrue();

        // Verify: each order points to its distinct physical path
        orders.Select(o => o.RelativePath).Distinct().Should().HaveCount(3);

        // Verify: all specs resolved unit prices
        orders.Single(o => o.OriginalFolderName == "Văn An").Items[0].PrintSpecification!.UnitPrice.Should().Be(5000);  // 13x18 in
        orders.Single(o => o.OriginalFolderName == "Anh An").Items[0].PrintSpecification!.UnitPrice.Should().Be(15000); // 20x30
        orders.Single(o => o.OriginalFolderName == "A.An").Items[0].PrintSpecification!.UnitPrice.Should().Be(80000); // 40x60 TG
    }

    [Fact]
    public async Task CustomerRepository_UpdateAlias_SuccessAndResolution()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "cust_update_alias.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var custRepo = new SqliteCustomerRepository(connFactory);
        var customer = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Ánh Dương" });
        await custRepo.AddAliasAsync(customer.Id, "Anh Duong Cu");

        var allAliases = await custRepo.GetAllAliasesAsync();
        var alias = allAliases.Single(a => a.AliasText == "Anh Duong Cu");

        var resolver = new CustomerResolver(custRepo);

        // Before edit: "Anh Duong Cu" matches, "Anh Duong Moi" is unresolved
        var beforeMatch = await resolver.ResolveCustomerAsync("Anh Duong Cu");
        beforeMatch.Status.Should().Be(CustomerResolutionStatus.ExactMatch);
        beforeMatch.ResolvedCustomer!.Id.Should().Be(customer.Id);

        var beforeUnknown = await resolver.ResolveCustomerAsync("Anh Duong Moi");
        beforeUnknown.Status.Should().NotBe(CustomerResolutionStatus.ExactMatch);

        // Act: Edit the saved alias to "Anh Duong Moi"
        await custRepo.UpdateAliasAsync(alias.Id, "Anh Duong Moi");

        // Verify in DB
        var updatedCustomer = await custRepo.GetByIdAsync(customer.Id);
        updatedCustomer!.Aliases.Should().Contain(a => a.AliasText == "Anh Duong Moi");
        updatedCustomer.Aliases.Should().NotContain(a => a.AliasText == "Anh Duong Cu");

        // After edit: "Anh Duong Moi" matches, "Anh Duong Cu" is unresolved
        var afterMatch = await resolver.ResolveCustomerAsync("Anh Duong Moi");
        afterMatch.Status.Should().Be(CustomerResolutionStatus.ExactMatch);
        afterMatch.ResolvedCustomer!.Id.Should().Be(customer.Id);

        var afterOld = await resolver.ResolveCustomerAsync("Anh Duong Cu");
        afterOld.ResolvedCustomer.Should().BeNull();
    }

    [Fact]
    public async Task CustomerRepository_UpdateAlias_Duplicate_ThrowsInvalidOperationException()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "cust_duplicate_alias.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var custRepo = new SqliteCustomerRepository(connFactory);
        var c1 = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Hàng 1" });
        var c2 = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Hàng 2" });

        await custRepo.AddAliasAsync(c1.Id, "Alias A");
        await custRepo.AddAliasAsync(c2.Id, "Alias B");

        var allAliases = await custRepo.GetAllAliasesAsync();
        var aliasB = allAliases.Single(a => a.AliasText == "Alias B");

        // Attempting to update Alias B to "alias a" (conflicts with c1's alias)
        var act = async () => await custRepo.UpdateAliasAsync(aliasB.Id, "alias a");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*đã tồn tại*");
    }

    [Fact]
    public async Task CustomerRepository_UpdateAlias_SameTextOrCasing_Allowed()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "cust_casing_alias.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var custRepo = new SqliteCustomerRepository(connFactory);
        var c = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Hàng C" });
        await custRepo.AddAliasAsync(c.Id, "thu trang");

        var allAliases = await custRepo.GetAllAliasesAsync();
        var alias = allAliases.Single(a => a.AliasText == "thu trang");

        // Update casing: "thu trang" -> "Thu Trang"
        await custRepo.UpdateAliasAsync(alias.Id, "Thu Trang");

        var updated = await custRepo.GetByIdAsync(c.Id);
        updated!.Aliases.Should().Contain(a => a.AliasText == "Thu Trang");
    }

    [Fact]
    public async Task CustomerRepository_RemoveAlias_RemovesFromResolution()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "cust_remove_alias.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var custRepo = new SqliteCustomerRepository(connFactory);
        var c = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Hàng D" });
        await custRepo.AddAliasAsync(c.Id, "Alias Tam Thoi");

        var allAliases = await custRepo.GetAllAliasesAsync();
        var alias = allAliases.Single(a => a.AliasText == "Alias Tam Thoi");

        await custRepo.RemoveAliasAsync(alias.Id);

        var updated = await custRepo.GetByIdAsync(c.Id);
        updated!.Aliases.Should().NotContain(a => a.AliasText == "Alias Tam Thoi");

        var resolver = new CustomerResolver(custRepo);
        var res = await resolver.ResolveCustomerAsync("Alias Tam Thoi");
        res.ResolvedCustomer.Should().BeNull();
    }

    [Fact]
    public async Task PrintSpecRepository_UpdateAndRemoveAlias_WorksCorrectly()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "spec_alias_test.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var specRepo = new SqlitePrintSpecificationRepository(connFactory);
        var spec = await specRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "Khổ Đặc Biệt Siêu To",
            UnitPrice = 25000
        });

        await specRepo.AddAliasAsync(spec.Id, "Khổ Siêu To");

        var allAliases = await specRepo.GetAllAliasesAsync();
        var alias = allAliases.Single(a => a.AliasText == "Khổ Siêu To");

        // Update alias
        await specRepo.UpdateAliasAsync(alias.Id, "Khổ Siêu To Nhanh");

        var resolver = new PrintSpecificationResolver(specRepo);
        var resUpdated = await resolver.ResolveSpecificationAsync("Khổ Siêu To Nhanh");
        resUpdated.Status.Should().Be(PrintSpecificationResolutionStatus.Resolved);
        resUpdated.ResolvedSpecification!.Id.Should().Be(spec.Id);

        // Remove alias
        await specRepo.RemoveAliasAsync(alias.Id);
        var resRemoved = await resolver.ResolveSpecificationAsync("Khổ Siêu To Nhanh");
        resRemoved.ResolvedSpecification.Should().BeNull();
    }

    [Fact]
    public async Task CustomerRepository_FindAliasByText_And_ReassignAlias_Success()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "reassign_alias_test.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var custRepo = new SqliteCustomerRepository(connFactory);
        var custA = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách A" });
        var custB = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách B" });

        await custRepo.AddAliasAsync(custA.Id, "AliasChung");

        // Find alias
        var found = await custRepo.FindAliasByTextAsync("AliasChung");
        found.Should().NotBeNull();
        found!.CustomerId.Should().Be(custA.Id);

        // Reassign to custB
        await custRepo.ReassignAliasAsync(found.Id, custB.Id);

        var reassigned = await custRepo.FindAliasByTextAsync("AliasChung");
        reassigned.Should().NotBeNull();
        reassigned!.CustomerId.Should().Be(custB.Id);
    }

    [Fact]
    public async Task OrderRepository_UpdateFolderCustomerId_UpdatesMatchingOrdersExceptLocked()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "update_folder_cust_test.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);

        var cust = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Test" });

        // Create 2 normal orders and 1 locked order with original_folder_name = "Anh An"
        var snapshot = new ScanSnapshot { Scope = ScanScope.Date, Status = ScanStatus.Success };
        var order1 = new Order
        {
            WorkDate = "2026-09-29",
            OriginalFolderName = "Anh An",
            RelativePath = @"2026-09-29\Anh An",
            Status = OrderStatus.Ready
        };
        await orderRepo.SaveOrderAsync(order1, snapshot);

        var order2 = new Order
        {
            WorkDate = "2026-09-29",
            OriginalFolderName = "Anh An",
            RelativePath = @"2026-09-29\Anh An\Don 01",
            Status = OrderStatus.Ready
        };
        await orderRepo.SaveOrderAsync(order2, snapshot);

        var orderLocked = new Order
        {
            WorkDate = "2026-09-29",
            OriginalFolderName = "Anh An",
            RelativePath = @"2026-09-29\Anh An\Don Khoa",
            Status = OrderStatus.Locked
        };
        await orderRepo.SaveOrderAsync(orderLocked, snapshot);

        // Act: Update folder customer ID
        await orderRepo.UpdateFolderCustomerIdAsync("2026-09-29", "Anh An", cust.Id);

        // Assert: order1 and order2 updated, orderLocked unaffected
        var updated1 = await orderRepo.GetOrderByRelativePathAsync(@"2026-09-29\Anh An");
        updated1!.CustomerId.Should().Be(cust.Id);

        var updated2 = await orderRepo.GetOrderByRelativePathAsync(@"2026-09-29\Anh An\Don 01");
        updated2!.CustomerId.Should().Be(cust.Id);

        var updatedLocked = await orderRepo.GetOrderByRelativePathAsync(@"2026-09-29\Anh An\Don Khoa");
        updatedLocked!.CustomerId.Should().BeNull();
    }
}
