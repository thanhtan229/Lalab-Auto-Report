using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.Services;

public class CloudMediaSyncService : ICloudMediaSyncService
{
    private readonly IThumbnailService _thumbnailService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IRootFolderRepository? _rootFolderRepository;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CloudMediaSyncService>? _logger;

    // Track successfully uploaded keys in memory to prevent re-uploading the same key repeatedly
    private readonly ConcurrentDictionary<string, bool> _uploadedKeys = new(StringComparer.OrdinalIgnoreCase);

    public CloudMediaSyncService(
        IThumbnailService thumbnailService,
        ISettingsRepository settingsRepository,
        IRootFolderRepository? rootFolderRepository = null,
        HttpClient? httpClient = null,
        ILogger<CloudMediaSyncService>? logger = null)
    {
        _thumbnailService = thumbnailService;
        _settingsRepository = settingsRepository;
        _rootFolderRepository = rootFolderRepository;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _logger = logger;
    }

    public async Task<OrderThumbnailInfo> EnsureOrderThumbnailUploadedAsync(
        Order order,
        string apiUrl,
        string secret,
        CancellationToken cancellationToken = default)
    {
        var info = new OrderThumbnailInfo
        {
            OrderId = order.Id,
            Status = "NONE"
        };

        try
        {
            var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
            string root = settings.RootFolder;
            if (order.RootFolderId.HasValue)
            {
                var rf = _rootFolderRepository == null ? null : await _rootFolderRepository.GetByIdAsync(order.RootFolderId.Value, cancellationToken);
                if (rf == null || string.IsNullOrWhiteSpace(rf.FullPath))
                    throw new InvalidOperationException($"Root #{order.RootFolderId} is unavailable; thumbnail fallback refused.");
                root = rf.FullPath;
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                return info;
            }

            string? candidateRel = order.ThumbnailCandidateRelativePath;
            if (string.IsNullOrWhiteSpace(candidateRel) && order.Items != null && order.Items.Count > 0)
            {
                candidateRel = order.Items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.ThumbnailCandidateRelativePath))?.ThumbnailCandidateRelativePath;
            }

            if (string.IsNullOrWhiteSpace(candidateRel) && !string.IsNullOrWhiteSpace(order.RelativePath))
            {
                string orderFolder = Path.Combine(root, order.RelativePath);
                candidateRel = FindFirstImageRelativePath(orderFolder, root);
            }

            if (string.IsNullOrWhiteSpace(candidateRel))
            {
                return info;
            }

            string fullImagePath = Path.Combine(root, candidateRel);
            if (!File.Exists(fullImagePath))
            {
                return info;
            }

            var fi = new FileInfo(fullImagePath);
            string version = fi.LastWriteTimeUtc.ToString("yyyyMMddHHmmss");
            string objectKey = $"thumbnails/{order.Id}/v_{version}.jpg";

            info.Key = objectKey;
            info.Version = version;

            // Check if already uploaded in this session
            if (_uploadedKeys.ContainsKey(objectKey))
            {
                info.Status = "READY";
                info.Uploaded = false; // already up, no new upload needed
                return info;
            }

            // Generate thumbnail JPEG using local ThumbnailService
            var thumbResult = await _thumbnailService.GetThumbnailAsync(fullImagePath, cancellationToken);
            if (thumbResult.Status != ThumbnailStatus.Success || string.IsNullOrWhiteSpace(thumbResult.CachedFilePath) || !File.Exists(thumbResult.CachedFilePath))
            {
                if (thumbResult.Status == ThumbnailStatus.UnsupportedFormat)
                {
                    info.Status = "NONE";
                }
                else
                {
                    info.Status = "PENDING";
                }
                return info;
            }

            info.LocalCachePath = thumbResult.CachedFilePath;

            // Upload to R2 via Worker endpoint PUT /api/sync/media/{key}
            byte[] fileBytes = await File.ReadAllBytesAsync(thumbResult.CachedFilePath, cancellationToken);
            using var req = new HttpRequestMessage(HttpMethod.Put, $"{apiUrl.TrimEnd('/')}/api/sync/media/{objectKey}")
            {
                Content = new ByteArrayContent(fileBytes)
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            req.Headers.Add("X-Sync-Secret", secret);

            var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                _uploadedKeys[objectKey] = true;
                info.Status = "READY";
                info.Uploaded = true;
            }
            else
            {
                string err = await resp.Content.ReadAsStringAsync(cancellationToken);
                _logger?.LogWarning("Upload thumbnail for order {OrderId} failed with status {StatusCode}: {Error}", order.Id, resp.StatusCode, err);
                info.Status = "PENDING";
                info.Uploaded = false;
            }

            return info;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error processing thumbnail upload for order {OrderId}", order.Id);
            info.Status = "PENDING";
            return info;
        }
    }

    public async Task<Dictionary<long, OrderThumbnailInfo>> EnsureThumbnailsUploadedForOrdersAsync(
        IEnumerable<Order> orders,
        string apiUrl,
        string secret,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<long, OrderThumbnailInfo>();
        var orderList = orders.ToList();
        if (orderList.Count == 0) return result;

        // Controlled concurrency: max 3 uploads at a time
        using var semaphore = new SemaphoreSlim(3, 3);
        var tasks = orderList.Select(async o =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var info = await EnsureOrderThumbnailUploadedAsync(o, apiUrl, secret, cancellationToken);
                return info;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var info in results)
        {
            result[info.OrderId] = info;
        }

        return result;
    }

    private static string? FindFirstImageRelativePath(string orderFolder, string rootFolder)
    {
        try
        {
            if (!Directory.Exists(orderFolder)) return null;

            var extSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".psd", ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng"
            };

            var files = Directory.EnumerateFiles(orderFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => extSet.Contains(Path.GetExtension(f)))
                .ToList();

            if (files.Count == 0) return null;

            var preferred = files.Where(f =>
            {
                var ext = Path.GetExtension(f);
                return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
            }).ToList();

            var targetList = preferred.Count > 0 ? preferred : files;
            targetList.Sort(NaturalStringComparer.Instance);

            return Path.GetRelativePath(rootFolder, targetList[0]);
        }
        catch
        {
            return null;
        }
    }
}
