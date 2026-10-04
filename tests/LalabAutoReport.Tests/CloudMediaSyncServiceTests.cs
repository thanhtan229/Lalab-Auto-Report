using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using LalabAutoReport.Infrastructure.Services;
using Xunit;

namespace LalabAutoReport.Tests;

public class CloudMediaSyncServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _cacheDir;
    private readonly PhysicalFileSystemAdapter _fileSystem;
    private readonly ThumbnailService _thumbnailService;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteSettingsRepository _settingsRepo;

    public CloudMediaSyncServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "CloudMediaTests_" + Guid.NewGuid().ToString("N"));
        _cacheDir = Path.Combine(_tempRoot, "ThumbnailCache");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_cacheDir);

        _fileSystem = new PhysicalFileSystemAdapter();
        _thumbnailService = new ThumbnailService(_fileSystem, _cacheDir);

        string dbPath = Path.Combine(_tempRoot, "test_settings.db");
        _connectionFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(_connectionFactory);
        migrator.MigrateAsync().GetAwaiter().GetResult();
        _settingsRepo = new SqliteSettingsRepository(_connectionFactory);

        // Seed root folder
        var settings = _settingsRepo.GetSettingsAsync().GetAwaiter().GetResult();
        settings.RootFolder = _tempRoot;
        _settingsRepo.SaveSettingsAsync(settings).GetAwaiter().GetResult();
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

    private string CreateTestJpeg(string relativePath, int width = 50, int height = 50)
    {
        string fullPath = Path.Combine(_tempRoot, relativePath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
        using var stream = File.Create(fullPath);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(wb));
        encoder.Save(stream);

        return fullPath;
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public ConcurrentQueue<HttpRequestMessage> CapturedRequests { get; } = new();
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\":true}")
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequests.Enqueue(request);
            return Task.FromResult(ResponseToReturn);
        }
    }

    [Fact]
    public async Task MissingKnownRoot_DoesNotReadOrUploadDefaultRootImage()
    {
        CreateTestJpeg("2026-10-02/C/photo.jpg");
        var handler = new MockHttpMessageHandler();
        var service = new CloudMediaSyncService(_thumbnailService, _settingsRepo, new SqliteRootFolderRepository(_connectionFactory), new HttpClient(handler));
        var result = await service.EnsureOrderThumbnailUploadedAsync(new Order { Id=1, RootFolderId=999,
            RelativePath="2026-10-02/C", ThumbnailCandidateRelativePath="2026-10-02/C/photo.jpg" }, "https://fixture", "secret");
        Assert.False(result.Uploaded); Assert.Equal("PENDING", result.Status); Assert.Empty(handler.CapturedRequests);
    }

    [Fact]
    public async Task EnsureOrderThumbnailUploadedAsync_WhenOrderHasNoImage_ReturnsNone()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var service = new CloudMediaSyncService(_thumbnailService, _settingsRepo, null, httpClient);

        var order = new Order
        {
            Id = 101,
            OrderCode = "ORD-101",
            RelativePath = "NonExistentFolder"
        };

        var result = await service.EnsureOrderThumbnailUploadedAsync(order, "https://api.test", "secret");

        result.Status.Should().Be("NONE");
        result.Key.Should().BeNull();
        result.Uploaded.Should().BeFalse();
        handler.CapturedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureOrderThumbnailUploadedAsync_WhenImageExists_UploadsToWorkerAndReturnsReadyWithKeyAndVersion()
    {
        string relImg = Path.Combine("2026-10-02", "CustomerA", "photo1.jpg");
        CreateTestJpeg(relImg);

        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var service = new CloudMediaSyncService(_thumbnailService, _settingsRepo, null, httpClient);

        var order = new Order
        {
            Id = 202,
            OrderCode = "ORD-202",
            ThumbnailCandidateRelativePath = relImg
        };

        var result = await service.EnsureOrderThumbnailUploadedAsync(order, "https://api.test", "secret");

        result.Status.Should().Be("READY");
        result.Key.Should().StartWith("thumbnails/202/v_");
        result.Key.Should().EndWith(".jpg");
        result.Version.Should().NotBeNullOrEmpty();
        result.Uploaded.Should().BeTrue();
        result.LocalCachePath.Should().NotBeNull();
        File.Exists(result.LocalCachePath).Should().BeTrue();

        handler.CapturedRequests.Should().HaveCount(1);
        var req = handler.CapturedRequests.Single();
        req.Method.Should().Be(HttpMethod.Put);
        req.RequestUri!.ToString().Should().Be($"https://api.test/api/sync/media/{result.Key}");
        req.Headers.GetValues("X-Sync-Secret").Should().Contain("secret");
        req.Content!.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task EnsureOrderThumbnailUploadedAsync_WhenAlreadyUploaded_SkipsHttpUploadAndReturnsCachedInfo()
    {
        string relImg = Path.Combine("2026-10-02", "CustomerB", "photo2.jpg");
        CreateTestJpeg(relImg);

        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var service = new CloudMediaSyncService(_thumbnailService, _settingsRepo, null, httpClient);

        var order = new Order
        {
            Id = 303,
            OrderCode = "ORD-303",
            ThumbnailCandidateRelativePath = relImg
        };

        // First upload
        var first = await service.EnsureOrderThumbnailUploadedAsync(order, "https://api.test", "secret");
        first.Uploaded.Should().BeTrue();
        handler.CapturedRequests.Should().HaveCount(1);

        // Second call with same order/image
        var second = await service.EnsureOrderThumbnailUploadedAsync(order, "https://api.test", "secret");
        second.Status.Should().Be("READY");
        second.Key.Should().Be(first.Key);
        second.Uploaded.Should().BeFalse(); // Did not re-upload
        handler.CapturedRequests.Should().HaveCount(1); // No new HTTP request
    }

    [Fact]
    public async Task EnsureOrderThumbnailUploadedAsync_WhenWorkerReturnsError_ReturnsPending()
    {
        string relImg = Path.Combine("2026-10-02", "CustomerC", "photo3.jpg");
        CreateTestJpeg(relImg);

        var handler = new MockHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("R2 Error")
            }
        };
        var httpClient = new HttpClient(handler);
        var service = new CloudMediaSyncService(_thumbnailService, _settingsRepo, null, httpClient);

        var order = new Order
        {
            Id = 404,
            OrderCode = "ORD-404",
            ThumbnailCandidateRelativePath = relImg
        };

        var result = await service.EnsureOrderThumbnailUploadedAsync(order, "https://api.test", "secret");

        result.Status.Should().Be("PENDING");
        result.Uploaded.Should().BeFalse();
        handler.CapturedRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnsureThumbnailsUploadedForOrdersAsync_UploadsMultipleOrdersCorrectly()
    {
        string rel1 = Path.Combine("2026-10-02", "CustomerD", "p1.jpg");
        string rel2 = Path.Combine("2026-10-02", "CustomerE", "p2.jpg");
        CreateTestJpeg(rel1);
        CreateTestJpeg(rel2);

        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var service = new CloudMediaSyncService(_thumbnailService, _settingsRepo, null, httpClient);

        var orders = new List<Order>
        {
            new Order { Id = 501, ThumbnailCandidateRelativePath = rel1 },
            new Order { Id = 502, ThumbnailCandidateRelativePath = rel2 },
            new Order { Id = 503, RelativePath = "EmptyOrder" }
        };

        var dict = await service.EnsureThumbnailsUploadedForOrdersAsync(orders, "https://api.test", "secret");

        dict.Should().HaveCount(3);
        dict[501].Status.Should().Be("READY");
        dict[502].Status.Should().Be("READY");
        dict[503].Status.Should().Be("NONE");
        handler.CapturedRequests.Should().HaveCount(2);
    }
}
