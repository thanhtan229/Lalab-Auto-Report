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
}
