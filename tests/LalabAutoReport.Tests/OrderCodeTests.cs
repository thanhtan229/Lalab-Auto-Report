using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using LalabAutoReport.UI.ViewModels;
using Xunit;

namespace LalabAutoReport.Tests;

public class OrderCodeTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();

    private class TestSettingsRepo : LalabAutoReport.Core.Interfaces.ISettingsRepository
    {
        private readonly string _rootFolder;

        public TestSettingsRepo(string rootFolder)
        {
            _rootFolder = rootFolder;
        }

        public Task<AppSettings> GetSettingsAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AppSettings
            {
                RootFolder = _rootFolder,
                SupportedExtensions = new() { ".jpg", ".jpeg", ".png" }
            });
        }

        public Task SaveSettingsAsync(AppSettings settings, System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void OrderCodeGenerator_ShouldFormatCorrectly()
    {
        // 1 -> 001
        OrderCodeGenerator.Generate("2026-09-29", 1).Should().Be("DH-260929-001");
        // 99 -> 099
        OrderCodeGenerator.Generate("2026-09-29", 99).Should().Be("DH-260929-099");
        // 100 -> 100
        OrderCodeGenerator.Generate("2026-09-29", 100).Should().Be("DH-260929-100");
        // 999 -> 999
        OrderCodeGenerator.Generate("2026-09-29", 999).Should().Be("DH-260929-999");
        // 1000 -> 1000 (graceful expansion beyond 999)
        OrderCodeGenerator.Generate("2026-09-29", 1000).Should().Be("DH-260929-1000");

        // TryParseSequence
        bool parsed1 = OrderCodeGenerator.TryParseSequence("DH-260929-001", "2026-09-29", out int seq1);
        parsed1.Should().BeTrue();
        seq1.Should().Be(1);

        bool parsed1000 = OrderCodeGenerator.TryParseSequence("DH-260929-1000", "2026-09-29", out int seq1000);
        parsed1000.Should().BeTrue();
        seq1000.Should().Be(1000);

        // Different date returns false
        bool parsedDiffDate = OrderCodeGenerator.TryParseSequence("DH-260929-001", "2026-09-30", out _);
        parsedDiffDate.Should().BeFalse();
    }

    [Fact]
    public async Task SqliteOrderRepository_ShouldGenerateSequentialCodes_ForSameDate_AndResetForNewDate()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"test_ordercode_{Guid.NewGuid():N}.db");
        try
        {
            var connFactory = new SqliteConnectionFactory(dbPath);
            var migrator = new DatabaseMigrator(connFactory);
            await migrator.MigrateAsync();

            var orderRepo = new SqliteOrderRepository(connFactory);

            // Order 1 on 2026-09-29
            var order1 = new Order
            {
                WorkDate = "2026-09-29",
                OriginalFolderName = "Customer A",
                RelativePath = @"2026-09-29\Customer A"
            };
            await orderRepo.SaveOrderAsync(order1, new ScanSnapshot());
            order1.OrderCode.Should().Be("DH-260929-001");

            // Order 2 on 2026-09-29
            var order2 = new Order
            {
                WorkDate = "2026-09-29",
                OriginalFolderName = "Customer B",
                RelativePath = @"2026-09-29\Customer B"
            };
            await orderRepo.SaveOrderAsync(order2, new ScanSnapshot());
            order2.OrderCode.Should().Be("DH-260929-002");

            // Order 3 on 2026-09-29
            var order3 = new Order
            {
                WorkDate = "2026-09-29",
                OriginalFolderName = "Customer C",
                RelativePath = @"2026-09-29\Customer C"
            };
            await orderRepo.SaveOrderAsync(order3, new ScanSnapshot());
            order3.OrderCode.Should().Be("DH-260929-003");

            // Order on 2026-09-30 (new date) resets to 001
            var order4 = new Order
            {
                WorkDate = "2026-09-30",
                OriginalFolderName = "Customer D",
                RelativePath = @"2026-09-30\Customer D"
            };
            await orderRepo.SaveOrderAsync(order4, new ScanSnapshot());
            order4.OrderCode.Should().Be("DH-260930-001");

            // Query by code
            var fetchedByCode = await orderRepo.GetOrderByCodeAsync("DH-260929-002");
            fetchedByCode.Should().NotBeNull();
            fetchedByCode!.OriginalFolderName.Should().Be("Customer B");

            // Max sequence
            int maxSeq29 = await orderRepo.GetMaxOrderSequenceForDateAsync("2026-09-29");
            maxSeq29.Should().Be(3);

            int maxSeq30 = await orderRepo.GetMaxOrderSequenceForDateAsync("2026-09-30");
            maxSeq30.Should().Be(1);
        }
        finally
        {
            SafeDeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task SqliteOrderRepository_Rescan_ShouldPreserveOrderCode()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"test_ordercode_rescan_{Guid.NewGuid():N}.db");
        try
        {
            var connFactory = new SqliteConnectionFactory(dbPath);
            var migrator = new DatabaseMigrator(connFactory);
            await migrator.MigrateAsync();

            var orderRepo = new SqliteOrderRepository(connFactory);

            var order = new Order
            {
                WorkDate = "2026-09-29",
                OriginalFolderName = "Customer X",
                RelativePath = @"2026-09-29\Customer X"
            };
            await orderRepo.SaveOrderAsync(order, new ScanSnapshot());
            string assignedCode = order.OrderCode;
            assignedCode.Should().Be("DH-260929-001");

            // Rescan / update order
            order.Status = OrderStatus.Ready;
            order.LastScanAt = DateTimeOffset.UtcNow;
            await orderRepo.SaveOrderAsync(order, new ScanSnapshot());

            var reloaded = await orderRepo.GetOrderByIdAsync(order.Id);
            reloaded.Should().NotBeNull();
            reloaded!.OrderCode.Should().Be("DH-260929-001");
        }
        finally
        {
            SafeDeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task ScanService_SubOrders_ShouldEachReceiveDistinctSequentialCode()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(Path.GetTempPath(), $"test_suborders_{Guid.NewGuid():N}.db");
        try
        {
            var connFactory = new SqliteConnectionFactory(dbPath);
            var migrator = new DatabaseMigrator(connFactory);
            await migrator.MigrateAsync();

            var orderRepo = new SqliteOrderRepository(connFactory);
            var settingsRepo = new TestSettingsRepo(fixture.RootPath);
            var parser = new FolderStructureParser(_fileSystem);
            var printResolver = new PrintFolderResolver(_fileSystem);
            var scanner = new ScanService(_fileSystem, parser, printResolver, settingsRepo, orderRepo);

            // Folder 1: Single order (An Studio)
            fixture.CreateFile(@"2026-09-29\An Studio\13x18\img1.jpg");

            // Folder 2: Explicit customer with 2 sub-orders (Binh Studio: Don 01, Don 02)
            fixture.CreateFile(@"2026-09-29\Binh Studio\Don 01\13x18\img2.jpg");
            fixture.CreateFile(@"2026-09-29\Binh Studio\Don 02\13x18\img3.jpg");

            var scanned = await scanner.ScanDateAsync("2026-09-29");
            scanned.Count.Should().Be(3);

            // Verify all 3 have distinct sequential codes: 001, 002, 003
            var codes = scanned.Select(o => o.OrderCode).ToList();
            codes.Should().Contain("DH-260929-001");
            codes.Should().Contain("DH-260929-002");
            codes.Should().Contain("DH-260929-003");
            codes.Distinct().Count().Should().Be(3);

            // Rescan should preserve exact same codes
            var rescanned = await scanner.ScanDateAsync("2026-09-29");
            var rescannedCodes = rescanned.Select(o => o.OrderCode).ToList();
            rescannedCodes.Should().BeEquivalentTo(codes);
        }
        finally
        {
            SafeDeleteDb(dbPath);
        }
    }

    [Fact]
    public void DashboardViewModel_SearchByOrderCode_ShouldFilterCorrectly()
    {
        var vm = new DashboardViewModel(
            scanService: null!,
            settingsRepository: null!,
            orderRepository: null!,
            fileSystem: _fileSystem,
            lockingService: null!
        );

        var order1 = new Order
        {
            Id = 1,
            OrderCode = "DH-260929-001",
            WorkDate = "2026-09-29",
            OriginalFolderName = "Studio A"
        };
        var order2 = new Order
        {
            Id = 2,
            OrderCode = "DH-260929-002",
            WorkDate = "2026-09-29",
            OriginalFolderName = "Studio B"
        };

        var dm1 = new OrderDisplayModel(order1);
        var dm2 = new OrderDisplayModel(order2);

        vm.AllOrders.Add(dm1);
        vm.AllOrders.Add(dm2);

        // Search by sequence number "002"
        vm.SearchText = "002";
        vm.FilteredOrders.Count.Should().Be(1);
        vm.FilteredOrders.First().OrderCode.Should().Be("DH-260929-002");

        // Search by full code "dh-260929-001"
        vm.SearchText = "dh-260929-001";
        vm.FilteredOrders.Count.Should().Be(1);
        vm.FilteredOrders.First().OrderCode.Should().Be("DH-260929-001");

        // Clear search
        vm.SearchText = "";
        vm.FilteredOrders.Count.Should().Be(2);
    }

    private static void SafeDeleteDb(string dbPath)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
        catch { }
    }
}
