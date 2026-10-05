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

public class DiverseDateFormatTests
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

    [Theory]
    [InlineData("2026-09-29", "2026-09-29")]
    [InlineData("2026_09_29", "2026-09-29")]
    [InlineData("2026.09.29", "2026-09-29")]
    [InlineData("29-09-2026", "2026-09-29")]
    [InlineData("29_09_2026", "2026-09-29")]
    [InlineData("29.09.2026", "2026-09-29")]
    [InlineData("4-9-2026", "2026-09-04")]
    [InlineData("04-09-2026", "2026-09-04")]
    public void TryParseDateFolder_ValidFullDates_ParsesCorrectly(string folderName, string expectedCanonical)
    {
        bool success = FolderStructureParser.TryParseDateFolder(folderName, null, null, out var parsedDate, out string canonical);
        success.Should().BeTrue();
        canonical.Should().Be(expectedCanonical);
    }

    [Theory]
    [InlineData("29-09", 2026, "2026-09-29")]
    [InlineData("04-09", 2026, "2026-09-04")]
    [InlineData("4-9", 2026, "2026-09-04")]
    [InlineData("29_09", 2026, "2026-09-29")]
    [InlineData("29.09", 2026, "2026-09-29")]
    public void TryParseDateFolder_DayMonth_UsesTargetYear(string folderName, int targetYear, string expectedCanonical)
    {
        bool success = FolderStructureParser.TryParseDateFolder(folderName, null, targetYear, out var parsedDate, out string canonical);
        success.Should().BeTrue();
        canonical.Should().Be(expectedCanonical);
    }

    [Fact]
    public void TryParseDateFolder_DayMonth_ExtractsYearFromPath()
    {
        string path = @"D:\_TINIX_HINH IN\2026\THANG 09";
        bool success = FolderStructureParser.TryParseDateFolder("29-09", path, null, out var parsedDate, out string canonical);
        success.Should().BeTrue();
        canonical.Should().Be("2026-09-29");
    }

    [Theory]
    [InlineData("13-18")]
    [InlineData("20-30")]
    [InlineData("13x18")]
    [InlineData("0904 Như")]
    [InlineData("31-02-2026")]
    [InlineData("32-01-2026")]
    [InlineData("random_folder")]
    public void TryParseDateFolder_InvalidOrSizeFolders_ReturnsFalse(string folderName)
    {
        bool success = FolderStructureParser.TryParseDateFolder(folderName, null, 2026, out _, out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void DiscoverDateFolders_FindsAndNormalizesDiverseFormats()
    {
        using var fixture = new TestFileSystemFixture();
        fixture.CreateDirectory("2026-09-25");
        fixture.CreateDirectory("29-09-2026");
        fixture.CreateDirectory("04-09");
        fixture.CreateDirectory("0904 Như"); // should not be treated as date folder
        fixture.CreateDirectory("Bills"); // non-date folder

        var parser = new FolderStructureParser(_fileSystem);
        var dates = parser.DiscoverDateFolders(fixture.RootPath);

        dates.Should().Contain("2026-09-25");
        dates.Should().Contain("2026-09-29");
        dates.Should().NotContain("0904 Như");
        dates.Should().NotContain("Bills");
    }

    [Fact]
    public async Task ScanDateAsync_RootWithDmyFolder_SuccessfullyDiscoversOrders()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "dmy_test.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var repo = new SqliteProductRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);

        // Workshop reality: folder named 29-09-2026 (DD-MM-YYYY)
        fixture.CreateFile(@"29-09-2026\0929 Như Ý\15x21\img1.jpg");
        fixture.CreateFile(@"29-09-2026\0929 Như Ý\6x9\img2.jpg");

        var parser = new FolderStructureParser(_fileSystem, repo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(repo);
        var settingsRepo = new TestSettingsRepo(fixture.RootPath);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, custResolver, specResolver);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        orders.Should().HaveCount(1);
        var order = orders[0];
        order.WorkDate.Should().Be("2026-09-29");
        order.OriginalFolderName.Should().Be("0929 Như Ý");
        order.RelativePath.Should().Be(Path.Combine("29-09-2026", "0929 Như Ý"));
        order.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ScanDateAsync_MultiRootWithDifferentFormats_DiscoversAllOrders()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "multiroot_dates.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var rootRepo = new SqliteRootFolderRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);
        var prodRepo = new SqliteProductRepository(connFactory);

        string root1 = Path.Combine(fixture.RootPath, "Root1");
        string root2 = Path.Combine(fixture.RootPath, "Root2");

        // Root 1 uses ISO: 2026-09-29
        fixture.CreateFile(Path.Combine("Root1", "2026-09-29", "Khach A", "13x18 in", "a.jpg"));
        // Root 2 uses VN format: 29-09-2026
        fixture.CreateFile(Path.Combine("Root2", "29-09-2026", "Khach B", "13x18 in", "b.jpg"));

        var r1 = await rootRepo.InsertAsync(new RootFolder { Name = "Kho 1", FullPath = root1, IsActive = true });
        var r2 = await rootRepo.InsertAsync(new RootFolder { Name = "Kho 2", FullPath = root2, IsActive = true });

        var parser = new FolderStructureParser(_fileSystem, prodRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var settingsRepo = new TestSettingsRepo(root1);

        var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo, rootFolderRepository: rootRepo);
        var orders = await scanner.ScanDateAsync("2026-09-29");

        orders.Should().HaveCount(2);
        orders.Select(o => o.OriginalFolderName).Should().Contain("Khach A");
        orders.Select(o => o.OriginalFolderName).Should().Contain("Khach B");

        // Scanning date 2026-09-28 that only exists in Root1 (or neither) should not fail with DirectoryNotFoundException
        var emptyOrders = await scanner.ScanDateAsync("2026-09-28");
        emptyOrders.Should().BeEmpty();
    }
}
