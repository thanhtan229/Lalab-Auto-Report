using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using LalabAutoReport.Infrastructure.Services;
using Xunit;

namespace LalabAutoReport.Tests;

public class ThumbnailServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _cacheDir;
    private readonly PhysicalFileSystemAdapter _fileSystem;
    private readonly ThumbnailService _thumbnailService;

    public ThumbnailServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_ThumbTest_" + Guid.NewGuid().ToString("N"));
        _cacheDir = Path.Combine(_tempRoot, "ThumbnailCache");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_cacheDir);

        _fileSystem = new PhysicalFileSystemAdapter();
        _thumbnailService = new ThumbnailService(_fileSystem, _cacheDir);
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

    private string CreateTestJpeg(string fileName, int width = 100, int height = 100)
    {
        string filePath = Path.Combine(_tempRoot, fileName);
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
        using var stream = File.Create(filePath);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(wb));
        encoder.Save(stream);
        return filePath;
    }

    [Fact]
    public void NaturalStringComparer_SortsNumericChunksNaturally()
    {
        var input = new List<string>
        {
            "img_10.jpg",
            "img_2.jpg",
            "img_1.jpg",
            "img_20.jpg",
            "img_3.jpg"
        };

        var sorted = input.OrderBy(x => x, NaturalStringComparer.Instance).ToList();

        sorted.Should().Equal("img_1.jpg", "img_2.jpg", "img_3.jpg", "img_10.jpg", "img_20.jpg");
    }

    [Fact]
    public void NaturalStringComparer_HandlesCameraNumbering()
    {
        var input = new List<string>
        {
            "DSC_100.JPG",
            "DSC_002.JPG",
            "DSC_010.JPG",
            "DSC_001.JPG"
        };

        var sorted = input.OrderBy(x => x, NaturalStringComparer.Instance).ToList();

        sorted.Should().Equal("DSC_001.JPG", "DSC_002.JPG", "DSC_010.JPG", "DSC_100.JPG");
    }

    [Fact]
    public void NaturalStringComparer_HandlesNullAndCaseInsensitivity()
    {
        var comparer = NaturalStringComparer.Instance;

        comparer.Compare(null, null).Should().Be(0);
        comparer.Compare(null, "a").Should().BeLessThan(0);
        comparer.Compare("a", null).Should().BeGreaterThan(0);
        comparer.Compare("IMG_01.JPG", "img_01.jpg").Should().Be(0);
    }

    [Fact]
    public async Task GetThumbnailAsync_WhenFileNotFound_ReturnsFileNotFoundStatus()
    {
        string nonExistent = Path.Combine(_tempRoot, "non_existent.jpg");

        var result = await _thumbnailService.GetThumbnailAsync(nonExistent);

        result.Status.Should().Be(ThumbnailStatus.FileNotFound);
        result.CachedFilePath.Should().BeNull();
    }

    [Theory]
    [InlineData("test.psd", "PSD")]
    [InlineData("test.raw", "RAW")]
    [InlineData("test.cr2", "CR2")]
    [InlineData("test.tif", "TIF")]
    [InlineData("test.tiff", "TIFF")]
    [InlineData("test.pdf", "PDF")]
    public async Task GetThumbnailAsync_WhenUnsupportedFormat_ReturnsUnsupportedFormatWithoutDecoding(string fileName, string expectedExt)
    {
        string filePath = Path.Combine(_tempRoot, fileName);
        await File.WriteAllTextAsync(filePath, "dummy file content");

        var result = await _thumbnailService.GetThumbnailAsync(filePath);

        result.Status.Should().Be(ThumbnailStatus.UnsupportedFormat);
        result.FormatExtension.Should().Be(expectedExt);
        result.CachedFilePath.Should().BeNull();
    }

    [Fact]
    public async Task GetThumbnailAsync_WhenValidJpeg_DecodesAndCreatesDiskCache()
    {
        string jpgPath = CreateTestJpeg("photo_01.jpg", 200, 200);

        var result = await _thumbnailService.GetThumbnailAsync(jpgPath);

        result.Status.Should().Be(ThumbnailStatus.Success);
        result.CachedFilePath.Should().NotBeNullOrEmpty();
        File.Exists(result.CachedFilePath).Should().BeTrue();

        // Verify disk cache file exists
        var cacheFiles = Directory.GetFiles(_cacheDir, "*.jpg");
        cacheFiles.Should().HaveCount(1);
        new FileInfo(cacheFiles[0]).Length.Should().BeGreaterThan(0);

        // Verify byte size report
        long size = _thumbnailService.GetDiskCacheSizeBytes();
        size.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetThumbnailAsync_WhenCalledRepeatedly_ServesFromCache()
    {
        string jpgPath = CreateTestJpeg("repeat.jpg", 120, 120);

        var firstResult = await _thumbnailService.GetThumbnailAsync(jpgPath);
        firstResult.Status.Should().Be(ThumbnailStatus.Success);

        // Second call should return quickly from memory cache
        var secondResult = await _thumbnailService.GetThumbnailAsync(jpgPath);
        secondResult.Status.Should().Be(ThumbnailStatus.Success);
        secondResult.CachedFilePath.Should().Be(firstResult.CachedFilePath);

        // After clearing memory cache, should load from disk cache without re-decoding from original
        _thumbnailService.ClearMemoryCache();
        var thirdResult = await _thumbnailService.GetThumbnailAsync(jpgPath);
        thirdResult.Status.Should().Be(ThumbnailStatus.Success);
        thirdResult.CachedFilePath.Should().Be(firstResult.CachedFilePath);
    }

    [Fact]
    public async Task PauseAndResume_ControlsThumbnailQueueExecution()
    {
        string jpgPath = CreateTestJpeg("pause_test.jpg", 80, 80);

        _thumbnailService.Pause();

        var loadTask = Task.Run(async () => await _thumbnailService.GetThumbnailAsync(jpgPath));

        // Ensure task has not completed while paused
        await Task.Delay(100);
        loadTask.IsCompleted.Should().BeFalse();

        // Resume and verify task completes
        _thumbnailService.Resume();
        var result = await loadTask;

        result.Status.Should().Be(ThumbnailStatus.Success);
        result.CachedFilePath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ClearDiskCache_DeletesCachedFilesAndResetsSize()
    {
        string jpgPath = CreateTestJpeg("to_clear.jpg", 100, 100);
        await _thumbnailService.GetThumbnailAsync(jpgPath);

        _thumbnailService.GetDiskCacheSizeBytes().Should().BeGreaterThan(0);
        Directory.GetFiles(_cacheDir).Should().NotBeEmpty();

        _thumbnailService.ClearDiskCache();

        _thumbnailService.GetDiskCacheSizeBytes().Should().Be(0);
        Directory.GetFiles(_cacheDir).Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupExpiredDiskCache_DeletesOnlyFilesExceedingAge()
    {
        string jpgPath = CreateTestJpeg("age_test.jpg", 100, 100);
        await _thumbnailService.GetThumbnailAsync(jpgPath);

        var cacheFiles = Directory.GetFiles(_cacheDir, "*.jpg");
        cacheFiles.Should().HaveCount(1);
        string cacheFile = cacheFiles[0];

        // Backdate file to 40 days ago
        File.SetLastWriteTime(cacheFile, DateTime.Now.AddDays(-40));
        File.SetLastAccessTime(cacheFile, DateTime.Now.AddDays(-40));

        // Cleanup expired with 30-day threshold
        _thumbnailService.CleanupExpiredDiskCache(TimeSpan.FromDays(30));

        Directory.GetFiles(_cacheDir, "*.jpg").Should().BeEmpty();
    }

    [Fact]
    public async Task ScanService_AssignsCandidateImageNaturallySorted()
    {
        var connFactory = new SqliteConnectionFactory(Path.Combine(_tempRoot, "test_scan.db"));
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var orderRepo = new SqliteOrderRepository(connFactory);
        var productRepo = new SqliteProductRepository(connFactory);
        var settingsRepo = new SqliteSettingsRepository(connFactory);
        var customerRepo = new SqliteCustomerRepository(connFactory);
        var customerResolver = new CustomerResolver(customerRepo);
        var specResolver = new PrintSpecificationResolver(productRepo);
        var parser = new FolderStructureParser(_fileSystem);
        var folderResolver = new PrintFolderResolver(_fileSystem);

        await settingsRepo.SaveSettingsAsync(new AppSettings
        {
            RootFolder = _tempRoot,
            SupportedExtensions = new() { ".jpg", ".jpeg", ".png" }
        });

        var scanService = new ScanService(
            _fileSystem,
            parser,
            folderResolver,
            settingsRepo,
            orderRepo,
            customerResolver,
            specResolver);

        // Setup order directory structure: 2026-10-01 / NguyenVanA / 13x18 / In
        string dateDir = Path.Combine(_tempRoot, "2026-10-01");
        string customerDir = Path.Combine(dateDir, "NguyenVanA");
        string specDir = Path.Combine(customerDir, "13x18");
        string printDir = Path.Combine(specDir, "In");
        Directory.CreateDirectory(printDir);

        // Create images: img_10.jpg, img_2.jpg, img_1.jpg in print folder
        var img10 = CreateTestJpeg(Path.Combine("2026-10-01", "NguyenVanA", "13x18", "In", "img_10.jpg"), 50, 50);
        var img2 = CreateTestJpeg(Path.Combine("2026-10-01", "NguyenVanA", "13x18", "In", "img_2.jpg"), 50, 50);
        var img1 = CreateTestJpeg(Path.Combine("2026-10-01", "NguyenVanA", "13x18", "In", "img_1.jpg"), 50, 50);

        var orders = await scanService.ScanDateAsync("2026-10-01");

        orders.Should().HaveCount(1);
        var scannedOrder = orders[0];

        // The candidate thumbnail relative path should be img_1.jpg (natural sort: 1 before 2, 2 before 10)
        scannedOrder.ThumbnailCandidateRelativePath.Should().NotBeNull();
        scannedOrder.ThumbnailCandidateRelativePath.Should().EndWith("img_1.jpg");
    }
}
