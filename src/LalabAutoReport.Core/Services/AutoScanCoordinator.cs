using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class AutoScanCoordinator : IAutoScanCoordinator
{
    private readonly IScanService _scanService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IFolderStructureParser _structureParser;
    private readonly IFileSystemAdapter _fileSystem;
    private readonly IFolderFingerprintService _fingerprintService;
    private readonly IIdleDetectionService _idleDetectionService;
    private readonly IPrintStatusService? _printStatusService;
    private readonly ILogger<AutoScanCoordinator>? _logger;

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private bool _hasScannedCurrentIdleSession;
    private bool _isPaused;
    private bool _isScanning;
    private bool _disposed;

    public event Action<string>? StatusChanged;
    public event Action<int>? OrdersUpdated;

    public AutoScanCoordinator(
        IScanService scanService,
        ISettingsRepository settingsRepository,
        IOrderRepository orderRepository,
        IFolderStructureParser structureParser,
        IFileSystemAdapter fileSystem,
        IFolderFingerprintService fingerprintService,
        IIdleDetectionService idleDetectionService,
        IPrintStatusService? printStatusService = null,
        ILogger<AutoScanCoordinator>? logger = null)
    {
        _scanService = scanService;
        _settingsRepository = settingsRepository;
        _orderRepository = orderRepository;
        _structureParser = structureParser;
        _fileSystem = fileSystem;
        _fingerprintService = fingerprintService;
        _idleDetectionService = idleDetectionService;
        _printStatusService = printStatusService;
        _logger = logger;
    }

    public void Start()
    {
        if (_monitorTask != null) return;

        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        _logger?.LogInformation("AutoScanCoordinator started idle monitor loop.");
    }

    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        _monitorTask = null;
        _logger?.LogInformation("AutoScanCoordinator stopped.");
    }

    public void SetPaused(bool isPaused)
    {
        _isPaused = isPaused;
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);

                var idleTime = _idleDetectionService.GetIdleTime();

                // Reset session flag when user returns and performs active input (< 30s of idle)
                if (idleTime.TotalSeconds < 30)
                {
                    if (_hasScannedCurrentIdleSession)
                    {
                        _hasScannedCurrentIdleSession = false;
                        _logger?.LogDebug("User returned to PC; reset idle scan session flag.");
                    }
                }

                if (_isPaused || _isScanning || _hasScannedCurrentIdleSession)
                {
                    continue;
                }

                var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
                if (!settings.EnableIdleScan)
                {
                    continue;
                }

                int thresholdMinutes = Math.Max(1, settings.IdleThresholdMinutes);
                if (idleTime.TotalMinutes >= thresholdMinutes)
                {
                    // User reached idle threshold! Run exactly ONCE for this idle session
                    _hasScannedCurrentIdleSession = true;
                    _logger?.LogInformation("Idle threshold ({Minutes}m) reached (Actual: {Actual:F1}m). Initiating idle smart scan...",
                        thresholdMinutes, idleTime.TotalMinutes);

                    await PerformIdleScanAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error in idle monitor loop");
            }
        }
    }

    public async Task<int> PerformStartupScanAsync(CancellationToken cancellationToken = default)
    {
        if (_isScanning) return 0;

        try
        {
            _isScanning = true;
            var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
            if (!settings.AutoScanStartup)
            {
                return 0;
            }

            string rootFolder = settings.RootFolder;
            if (string.IsNullOrWhiteSpace(rootFolder) || !_fileSystem.DirectoryExists(rootFolder))
            {
                return 0;
            }

            var datesToCheck = new List<string> { DateTime.Today.ToString("yyyy-MM-dd") };
            if (settings.StartupIncludeYesterday)
            {
                datesToCheck.Add(DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"));
            }

            int updated = await ScanScopeWithFingerprintsAsync(rootFolder, datesToCheck, cancellationToken);
            if (updated > 0)
            {
                StatusChanged?.Invoke($"Khởi động: Đã cập nhật {updated} đơn hàng.");
                OrdersUpdated?.Invoke(updated);
            }
            return updated;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during startup quick scan");
            return 0;
        }
        finally
        {
            _isScanning = false;
        }
    }

    public async Task<int> PerformIdleScanAsync(CancellationToken cancellationToken = default)
    {
        if (_isScanning) return 0;

        try
        {
            _isScanning = true;
            var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
            string rootFolder = settings.RootFolder;

            if (string.IsNullOrWhiteSpace(rootFolder) || !_fileSystem.DirectoryExists(rootFolder))
            {
                return 0;
            }

            int windowDays = Math.Max(1, settings.IdleScanWindowDays);
            var datesToCheck = new List<string>();
            for (int i = 0; i <= windowDays; i++)
            {
                datesToCheck.Add(DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd"));
            }

            int updated = await ScanScopeWithFingerprintsAsync(rootFolder, datesToCheck, cancellationToken);
            if (updated > 0)
            {
                StatusChanged?.Invoke($"Đã tự động cập nhật {updated} đơn hàng lúc {DateTime.Now:HH:mm}.");
                OrdersUpdated?.Invoke(updated);
            }
            else
            {
                StatusChanged?.Invoke($"Tự động kiểm tra lúc {DateTime.Now:HH:mm} (Không có thay đổi).");
            }

            return updated;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during idle smart scan");
            return 0;
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task<int> ScanScopeWithFingerprintsAsync(string rootFolder, IEnumerable<string> dates, CancellationToken cancellationToken)
    {
        int updatedCount = 0;

        foreach (var date in dates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string datePath = _fileSystem.Combine(rootFolder, date);
            if (!_fileSystem.DirectoryExists(datePath))
            {
                continue;
            }

            var discoveredOrders = _structureParser.DiscoverOrdersForDate(rootFolder, date);
            foreach (var discOrder in discoveredOrders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var existingOrder = await _orderRepository.GetOrderByRelativePathAsync(discOrder.RelativePath, cancellationToken);

                // 1. Exclude 100% of Billed or Locked orders from auto-scanning
                if (existingOrder != null && (existingOrder.Status == OrderStatus.Locked || existingOrder.Status == OrderStatus.Billed))
                {
                    continue;
                }

                // 2. Exclude 100% of PRINTED orders from auto-scanning
                if (existingOrder != null && existingOrder.IsPrinted)
                {
                    _logger?.LogDebug("Auto-scan: Skipping PRINTED order '{Path}'", discOrder.RelativePath);
                    continue;
                }

                string fullOrderPath = _fileSystem.Combine(rootFolder, discOrder.RelativePath);
                if (_printStatusService != null && await _printStatusService.IsFolderPrintedAsync(fullOrderPath, cancellationToken))
                {
                    _logger?.LogDebug("Auto-scan: Skipping PRINTED folder '{Path}'", fullOrderPath);
                    continue;
                }

                // 3. Compute lightweight folder fingerprint
                var currentFp = _fingerprintService.ComputeOrderFingerprint(rootFolder, discOrder.RelativePath);

                // 3. Compare with stored fingerprint
                if (existingOrder != null && !_fingerprintService.HasChanged(existingOrder.Fingerprint, currentFp))
                {
                    // No changes detected -> skip deep rescan
                    continue;
                }

                // 4. Fingerprint changed or brand new order -> rescan this order
                try
                {
                    _logger?.LogInformation("Auto-scan: Rescanning changed order '{Path}'", discOrder.RelativePath);
                    await _scanService.ScanOrderAsync(discOrder.RelativePath, cancellationToken: cancellationToken);
                    updatedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to rescan order '{Path}' during auto-scan", discOrder.RelativePath);
                }
            }
        }

        return updatedCount;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
