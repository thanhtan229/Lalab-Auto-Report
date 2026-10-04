using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.Services;

public class ThumbnailService : IThumbnailService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp"
    };

    private static readonly HashSet<string> UnsupportedHeavyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".psd", ".psb", ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".tif", ".tiff"
    };

    private readonly IFileSystemAdapter _fileSystem;
    private readonly ILogger<ThumbnailService>? _logger;
    private readonly string _cacheDirectory;

    // Single worker concurrency: only 1 image decoded at a time to prevent disk I/O thrashing
    private readonly SemaphoreSlim _workerLock = new(1, 1);

    // Pause/Resume coordination
    private readonly AsyncManualResetEvent _pauseGate = new(initialState: true);

    // Tier 1: In-memory cache for fast lookup (max ~100 items)
    private readonly ConcurrentDictionary<string, ThumbnailResult> _memoryCache = new(StringComparer.OrdinalIgnoreCase);

    public ThumbnailService(
        IFileSystemAdapter fileSystem,
        string? cacheDirectory = null,
        ILogger<ThumbnailService>? logger = null)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LalabAutoReport",
            "Thumbnails");

        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to create thumbnail cache directory '{Dir}'", _cacheDirectory);
        }
    }

    public async Task<ThumbnailResult> GetThumbnailAsync(string fullSourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullSourcePath))
        {
            return ThumbnailResult.NotFound();
        }

        // Fast-fail check
        string ext = Path.GetExtension(fullSourcePath);
        if (UnsupportedHeavyExtensions.Contains(ext))
        {
            return ThumbnailResult.Unsupported(ext.TrimStart('.').ToUpperInvariant());
        }

        if (!SupportedExtensions.Contains(ext))
        {
            return ThumbnailResult.Unsupported(ext.TrimStart('.').ToUpperInvariant());
        }

        if (!_fileSystem.FileExists(fullSourcePath))
        {
            return ThumbnailResult.NotFound();
        }

        // Calculate cache key based on file metadata (path, length, last write time)
        string cacheKey;
        string cachedFilePath;
        try
        {
            var fi = new FileInfo(fullSourcePath);
            if (!fi.Exists)
            {
                return ThumbnailResult.NotFound();
            }

            string rawKey = $"{fullSourcePath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|v1";
            using var md5 = MD5.Create();
            string hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
            cacheKey = hash;
            cachedFilePath = Path.Combine(_cacheDirectory, $"{hash}.jpg");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to inspect file '{Path}' for thumbnail", fullSourcePath);
            return ThumbnailResult.FailedResult(ex.Message);
        }

        // Check Tier 1 (RAM)
        if (_memoryCache.TryGetValue(cacheKey, out var memCached))
        {
            if (memCached.CachedFilePath != null && File.Exists(memCached.CachedFilePath))
            {
                return memCached;
            }
        }

        // Check Tier 2 (Disk)
        if (File.Exists(cachedFilePath))
        {
            var diskResult = ThumbnailResult.SuccessResult(cachedFilePath);
            AddToMemoryCache(cacheKey, diskResult);
            return diskResult;
        }

        // Wait if paused (e.g. active scan or order lock in progress)
        await _pauseGate.WaitAsync(cancellationToken);

        // Queue to single worker to generate thumbnail
        await _workerLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check disk cache inside lock in case another task generated it
            if (File.Exists(cachedFilePath))
            {
                var diskResult = ThumbnailResult.SuccessResult(cachedFilePath);
                AddToMemoryCache(cacheKey, diskResult);
                return diskResult;
            }

            // Also check pause state inside lock
            await _pauseGate.WaitAsync(cancellationToken);

            // Decode image using WIC with downsampling
            var result = await Task.Run(() => GenerateThumbnailCore(fullSourcePath, cachedFilePath), cancellationToken);
            if (result.Status == ThumbnailStatus.Success)
            {
                AddToMemoryCache(cacheKey, result);
            }
            return result;
        }
        finally
        {
            _workerLock.Release();
        }
    }

    private ThumbnailResult GenerateThumbnailCore(string sourcePath, string targetCachedPath)
    {
        string tempPath = Path.Combine(_cacheDirectory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var decoder = BitmapDecoder.Create(
                    fileStream,
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);

                if (decoder.Frames.Count == 0)
                {
                    return ThumbnailResult.FailedResult("No frames found in image");
                }

                var frame = decoder.Frames[0];
                double origW = frame.PixelWidth;
                double origH = frame.PixelHeight;

                if (origW <= 0 || origH <= 0)
                {
                    return ThumbnailResult.FailedResult("Invalid image dimensions");
                }

                const double maxDim = 320.0;
                double scale = Math.Min(maxDim / origW, maxDim / origH);
                if (scale > 1.0)
                {
                    scale = 1.0; // Do not upscale smaller images
                }

                BitmapSource outputSource;
                if (scale < 0.999)
                {
                    var transformed = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    transformed.Freeze();
                    outputSource = transformed;
                }
                else
                {
                    outputSource = frame;
                }

                var encoder = new JpegBitmapEncoder
                {
                    QualityLevel = 80
                };
                encoder.Frames.Add(BitmapFrame.Create(outputSource));

                using (var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(outStream);
                }
            }

            if (File.Exists(targetCachedPath))
            {
                File.Delete(targetCachedPath);
            }
            File.Move(tempPath, targetCachedPath);

            return ThumbnailResult.SuccessResult(targetCachedPath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error decoding thumbnail for '{Path}'", sourcePath);
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }

            return ThumbnailResult.FailedResult(ex.Message);
        }
    }

    private void AddToMemoryCache(string key, ThumbnailResult result)
    {
        if (_memoryCache.Count > 100)
        {
            _memoryCache.Clear();
        }
        _memoryCache[key] = result;
    }

    public void Pause()
    {
        _pauseGate.Reset();
    }

    public void Resume()
    {
        _pauseGate.Set();
    }

    public void ClearMemoryCache()
    {
        _memoryCache.Clear();
    }

    public void ClearDiskCache()
    {
        _memoryCache.Clear();
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.*");
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error clearing disk cache in '{Dir}'", _cacheDirectory);
        }
    }

    public long GetDiskCacheSizeBytes()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory)) return 0;
            var files = Directory.GetFiles(_cacheDirectory, "*.*");
            long total = 0;
            foreach (var f in files)
            {
                try
                {
                    total += new FileInfo(f).Length;
                }
                catch { }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    public void CleanupExpiredDiskCache(TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory)) return;
            var threshold = DateTime.UtcNow - maxAge;
            var files = Directory.GetFiles(_cacheDirectory, "*.*");
            foreach (var f in files)
            {
                try
                {
                    var fi = new FileInfo(f);
                    var lastUsedUtc = fi.LastWriteTimeUtc > fi.LastAccessTimeUtc ? fi.LastWriteTimeUtc : fi.LastAccessTimeUtc;
                    if (lastUsedUtc < threshold)
                    {
                        fi.Delete();
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error cleaning expired disk cache");
        }
    }

    private class AsyncManualResetEvent
    {
        private volatile TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AsyncManualResetEvent(bool initialState)
        {
            if (initialState)
            {
                _tcs.TrySetResult(true);
            }
        }

        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            return _tcs.Task.WaitAsync(cancellationToken);
        }

        public void Set()
        {
            _tcs.TrySetResult(true);
        }

        public void Reset()
        {
            while (true)
            {
                var current = _tcs;
                if (!current.Task.IsCompleted) return;
                var next = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (Interlocked.CompareExchange(ref _tcs, next, current) == current) return;
            }
        }
    }
}
