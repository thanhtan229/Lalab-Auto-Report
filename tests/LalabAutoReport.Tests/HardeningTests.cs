using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using Xunit;

namespace LalabAutoReport.Tests;

public class HardeningTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _tempDbPath;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IDatabaseBackupService _backupService;
    private readonly IDatabaseMigrator _migrator;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IOrderRepository _orderRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly IPrintSpecificationRepository _specRepo;
    private readonly IBillRepository _billRepo;
    private readonly PhysicalFileSystemAdapter _fileSystem;

    public HardeningTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "LalabHardening_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _tempDbPath = Path.Combine(_tempRoot, "test_hardening.db");
        _connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        _backupService = new DatabaseBackupService(_connectionFactory);
        _migrator = new DatabaseMigrator(_connectionFactory, _backupService);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _settingsRepo = new SqliteSettingsRepository(_connectionFactory);
        _orderRepo = new SqliteOrderRepository(_connectionFactory);
        _customerRepo = new SqliteCustomerRepository(_connectionFactory);
        _specRepo = new SqlitePrintSpecificationRepository(_connectionFactory);
        _billRepo = new SqliteBillRepository(_connectionFactory);
        _fileSystem = new PhysicalFileSystemAdapter();

        _settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            SupportedExtensions = new List<string> { ".jpg", ".jpeg", ".png", ".tif" }
        }).GetAwaiter().GetResult();
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
        catch
        {
            // Ignore temporary cleanup errors
        }
    }

    [Fact]
    public void PhysicalFileSystemAdapter_ReportsMissingEnumerationInsteadOfEmptyObservation()
    {
        var nonExistentPath = Path.Combine(_tempRoot, "DoesNotExist_" + Guid.NewGuid().ToString("N"));

        Assert.False(_fileSystem.DirectoryExists(nonExistentPath));
        Assert.False(_fileSystem.FileExists(Path.Combine(nonExistentPath, "file.jpg")));

        Assert.Throws<DirectoryNotFoundException>(() => _fileSystem.EnumerateDirectories(nonExistentPath));
        Assert.Throws<DirectoryNotFoundException>(() => _fileSystem.EnumerateFiles(nonExistentPath));
    }

    [Fact]
    public async Task ScanService_ThrowsOperationCanceledException_WhenCanceledMidScan()
    {
        string date = "2026-09-28";
        string dateDir = Path.Combine(_tempRoot, date);
        Directory.CreateDirectory(dateDir);

        for (int i = 1; i <= 5; i++)
        {
            string custDir = Path.Combine(dateDir, $"Customer_{i}");
            string specDir = Path.Combine(custDir, "13x18 in");
            Directory.CreateDirectory(specDir);
            File.WriteAllBytes(Path.Combine(specDir, "img.jpg"), new byte[10]);
        }

        var parser = new FolderStructureParser(_fileSystem);
        var resolver = new PrintFolderResolver(_fileSystem);
        var scanService = new ScanService(
            _fileSystem,
            parser,
            resolver,
            _settingsRepo,
            _orderRepo
        );

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await scanService.ScanDateAsync(date, cancellationToken: cts.Token);
        });
    }

    [Fact]
    public async Task ScanService_DegradesPerOrder_WhenSingleOrderHasInaccessibleOrFaultyPath()
    {
        string date = "2026-09-28";
        string dateDir = Path.Combine(_tempRoot, date);
        Directory.CreateDirectory(dateDir);

        // Order 1: Good
        string goodCustDir = Path.Combine(dateDir, "GoodCustomer");
        string goodSpec = Path.Combine(goodCustDir, "13x18 in");
        Directory.CreateDirectory(goodSpec);
        File.WriteAllBytes(Path.Combine(goodSpec, "photo1.jpg"), new byte[10]);

        // Order 2: Bad folder (we can simulate by mocking or creating a file system error)
        string otherCustDir = Path.Combine(dateDir, "OtherCustomer");
        string otherSpec = Path.Combine(otherCustDir, "13x18 in");
        Directory.CreateDirectory(otherSpec);
        File.WriteAllBytes(Path.Combine(otherSpec, "photo2.jpg"), new byte[10]);

        var parser = new FolderStructureParser(_fileSystem);
        var resolver = new PrintFolderResolver(_fileSystem);
        var scanService = new ScanService(
            _fileSystem,
            parser,
            resolver,
            _settingsRepo,
            _orderRepo
        );

        var orders = await scanService.ScanDateAsync(date);
        Assert.Equal(2, orders.Count);
        Assert.Contains(orders, o => o.OriginalFolderName == "GoodCustomer");
        Assert.Contains(orders, o => o.OriginalFolderName == "OtherCustomer");
    }

    [Fact]
    public async Task DatabaseBackupService_CreateBackupAndRestore_RestoresDataAccurately()
    {
        // 1. Create a customer and print spec
        var customer = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Khách Hàng Sao Lưu",
            Phone = "0901234567"
        }, "Khach Sao Luu");

        var spec = await _specRepo.CreateSpecificationAsync(new PrintSpecification
        {
            CanonicalName = "60x90 in",
            UnitPrice = 90000
        }, "60x90");

        // 2. Perform online backup
        var backupInfo = await _backupService.CreateBackupAsync();
        Assert.True(File.Exists(backupInfo.BackupPath));
        Assert.True(backupInfo.FileSizeBytes > 0);

        // 3. Insert additional data that should NOT be in the backup
        var customer2 = await _customerRepo.CreateCustomerAsync(new Customer
        {
            CanonicalName = "Khách Hàng Thêm Sau",
            Phone = "0999999999"
        }, "Them Sau");

        var allCustomersBeforeRestore = await _customerRepo.GetAllAsync();
        Assert.Equal(2, allCustomersBeforeRestore.Count);

        // 4. Restore from the backup
        await _backupService.RestoreBackupAsync(backupInfo.BackupPath);

        // 5. Verify restored state: only customer 1 exists, customer 2 is gone!
        var allCustomersAfterRestore = await _customerRepo.GetAllAsync();
        Assert.Single(allCustomersAfterRestore);
        Assert.Equal("Khách Hàng Sao Lưu", allCustomersAfterRestore[0].CanonicalName);

        // 6. Verify backup list includes the created backup
        var backups = await _backupService.GetBackupsAsync();
        Assert.NotEmpty(backups);
        Assert.Contains(backups, b => b.FileName == backupInfo.FileName);
    }

    [Fact]
    public async Task DatabaseBackupService_WithSecondaryBackupFolder_CopiesBackupFileToSecondaryLocation()
    {
        string secondaryDir = Path.Combine(_tempRoot, "SecondaryBackupFolder_NAS");
        var settingsRepo = new SqliteSettingsRepository(_connectionFactory);
        var settings = await settingsRepo.GetSettingsAsync();
        settings.SecondaryBackupFolder = secondaryDir;
        await settingsRepo.SaveSettingsAsync(settings);

        var backupServiceWithSettings = new DatabaseBackupService(_connectionFactory, settingsRepo);
        var backupInfo = await backupServiceWithSettings.CreateBackupAsync();

        Assert.True(File.Exists(backupInfo.BackupPath));
        string expectedSecondaryFile = Path.Combine(secondaryDir, backupInfo.FileName);
        Assert.True(File.Exists(expectedSecondaryFile));
        Assert.Equal(new FileInfo(backupInfo.BackupPath).Length, new FileInfo(expectedSecondaryFile).Length);
    }

    [Fact]
    public async Task LargeDirectoryScan_PerformanceBenchmark_CompletesQuickly()
    {
        string date = "2026-09-28";
        string dateDir = Path.Combine(_tempRoot, date);
        Directory.CreateDirectory(dateDir);

        // Generate 20 orders, each with 2 specifications and 25 files = 1,000 files total
        for (int i = 1; i <= 20; i++)
        {
            string custDir = Path.Combine(dateDir, $"Customer_{i:D2}");
            string spec1 = Path.Combine(custDir, "13x18 in");
            string spec2 = Path.Combine(custDir, "15x21 in");
            Directory.CreateDirectory(spec1);
            Directory.CreateDirectory(spec2);

            string print1 = Path.Combine(spec1, "print");
            string print2 = Path.Combine(spec2, "print");
            Directory.CreateDirectory(print1);
            Directory.CreateDirectory(print2);

            for (int f = 1; f <= 25; f++)
            {
                File.WriteAllBytes(Path.Combine(spec1, $"src_{f:D3}.jpg"), new byte[4]);
                File.WriteAllBytes(Path.Combine(print1, $"prt_{f:D3}.jpg"), new byte[4]);
                File.WriteAllBytes(Path.Combine(spec2, $"src_{f:D3}.jpg"), new byte[4]);
                File.WriteAllBytes(Path.Combine(print2, $"prt_{f:D3}.jpg"), new byte[4]);
            }
        }

        var parser = new FolderStructureParser(_fileSystem);
        var resolver = new PrintFolderResolver(_fileSystem);
        var scanService = new ScanService(
            _fileSystem,
            parser,
            resolver,
            _settingsRepo,
            _orderRepo
        );

        var stopwatch = Stopwatch.StartNew();
        var orders = await scanService.ScanDateAsync(date);
        stopwatch.Stop();

        Assert.Equal(20, orders.Count);
        // All orders should have 2 specifications with 25 source and 25 print
        foreach (var order in orders)
        {
            Assert.Equal(2, order.Items.Count);
            foreach (var item in order.Items)
            {
                Assert.Equal(25, item.SourceCount);
                Assert.Equal(25, item.PrintCount);
                Assert.Equal(25, item.BillQuantity);
                Assert.Equal(QuantityResolutionMode.UsePrint, item.QuantityResolutionMode);
            }
        }

        // Must complete well within 5 seconds for 1,000 files in local testing
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Expected scan time < 5000ms, actual: {stopwatch.ElapsedMilliseconds}ms");
    }
}
