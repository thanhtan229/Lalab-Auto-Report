using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class PrintStatusService : IPrintStatusService
{
    public const string MarkerFileName = ".lalab-printed";

    private readonly IFolderPrintRepository _folderPrintRepository;
    private readonly IFolderVisualMarkerService _visualMarkerService;
    private readonly IOrderRepository? _orderRepository;
    private readonly ISettingsRepository? _settingsRepository;
    private readonly ILogger<PrintStatusService>? _logger;

    public event Action<string, PrintStatus>? PrintStatusChanged;

    public PrintStatusService(
        IFolderPrintRepository folderPrintRepository,
        IFolderVisualMarkerService visualMarkerService,
        IOrderRepository? orderRepository = null,
        ISettingsRepository? settingsRepository = null,
        ILogger<PrintStatusService>? logger = null)
    {
        _folderPrintRepository = folderPrintRepository;
        _visualMarkerService = visualMarkerService;
        _orderRepository = orderRepository;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public async Task<PrintStatus> GetStatusAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return PrintStatus.NotPrinted;

        string norm = PathNormalizer.Normalize(folderPath);

        // 1. Check Primary Source of Truth: Database
        var record = await _folderPrintRepository.GetByNormalizedPathAsync(norm, cancellationToken);
        if (record != null)
        {
            if (record.Status == PrintStatus.Printed)
            {
                // Reconcile marker file if missing on disk
                if (Directory.Exists(folderPath))
                {
                    string markerPath = Path.Combine(folderPath, MarkerFileName);
                    if (!File.Exists(markerPath))
                    {
                        try
                        {
                            WriteMarkerFile(markerPath, record.MarkedAt, record.MarkedBy);
                        }
                        catch { }
                    }
                }
                return PrintStatus.Printed;
            }
            return PrintStatus.NotPrinted;
        }

        // 2. Check Secondary Recovery Hint: Filesystem marker file
        if (Directory.Exists(folderPath))
        {
            string markerPath = Path.Combine(folderPath, MarkerFileName);
            if (File.Exists(markerPath))
            {
                // Folder has marker on disk but DB has no record -> Reconcile into DB!
                try
                {
                    var recoveredRecord = new FolderPrintRecord
                    {
                        FolderPath = folderPath,
                        NormalizedPath = norm,
                        Status = PrintStatus.Printed,
                        MarkedAt = DateTimeOffset.UtcNow,
                        MarkedBy = "MarkerRecovery"
                    };
                    await _folderPrintRepository.SaveAsync(recoveredRecord, cancellationToken);
                    _logger?.LogInformation("Recovered printed status from marker file for '{Path}'", folderPath);
                    return PrintStatus.Printed;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to reconcile recovered marker for '{Path}'", folderPath);
                    return PrintStatus.Printed;
                }
            }
        }

        return PrintStatus.NotPrinted;
    }

    public async Task<bool> IsFolderPrintedAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(folderPath, cancellationToken);
        return status == PrintStatus.Printed;
    }

    public async Task<TogglePrintStatusResult> ToggleStatusAsync(
        string folderPath,
        string markedBy = "ExplorerContextMenu",
        CancellationToken cancellationToken = default)
    {
        var currentStatus = await GetStatusAsync(folderPath, cancellationToken);
        var targetStatus = currentStatus == PrintStatus.Printed ? PrintStatus.NotPrinted : PrintStatus.Printed;
        return await SetStatusAsync(folderPath, targetStatus, markedBy, cancellationToken);
    }

    public async Task<TogglePrintStatusResult> SetStatusAsync(
        string folderPath,
        PrintStatus targetStatus,
        string markedBy = "ExplorerContextMenu",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return new TogglePrintStatusResult
            {
                FolderPath = folderPath,
                IsSuccess = false,
                ErrorMessage = "Đường dẫn thư mục không được để trống."
            };
        }

        // Clean & sanitize path
        string cleanPath = folderPath.Trim().Trim('\"', '\'').TrimEnd('\\', '/');
        if (!Directory.Exists(cleanPath))
        {
            return new TogglePrintStatusResult
            {
                FolderPath = cleanPath,
                IsSuccess = false,
                ErrorMessage = $"Thư mục không tồn tại: {cleanPath}"
            };
        }

        string norm = PathNormalizer.Normalize(cleanPath);
        var currentStatus = await GetStatusAsync(cleanPath, cancellationToken);

        if (targetStatus == currentStatus)
        {
            // Already in desired status
            return new TogglePrintStatusResult
            {
                FolderPath = cleanPath,
                NormalizedPath = norm,
                PreviousStatus = currentStatus,
                NewStatus = targetStatus,
                IsSuccess = true,
                VisualIconUpdated = _visualMarkerService.HasVisualMarker(cleanPath)
            };
        }

        var result = new TogglePrintStatusResult
        {
            FolderPath = cleanPath,
            NormalizedPath = norm,
            PreviousStatus = currentStatus,
            NewStatus = targetStatus
        };

        if (targetStatus == PrintStatus.Printed)
        {
            // --- TURN ON: NOT_PRINTED -> PRINTED ---
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;

                // 1. Update Database (Primary Source of Truth)
                var record = new FolderPrintRecord
                {
                    FolderPath = cleanPath,
                    NormalizedPath = norm,
                    Status = PrintStatus.Printed,
                    MarkedAt = now,
                    MarkedBy = markedBy
                };
                await _folderPrintRepository.SaveAsync(record, cancellationToken);

                // 2. Synchronize associated order if path is within root folder
                await SyncAssociatedOrderPrintedStateAsync(cleanPath, true, now, cancellationToken);

                // 3. Write hidden marker file on disk (.lalab-printed)
                try
                {
                    string markerPath = Path.Combine(cleanPath, MarkerFileName);
                    WriteMarkerFile(markerPath, now, markedBy);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to write marker file in '{Path}' (non-critical)", cleanPath);
                }

                // 4. Apply Visual Marker (desktop.ini + icon đỏ + SHChangeNotify)
                bool visualApplied = false;
                try
                {
                    visualApplied = _visualMarkerService.ApplyVisualMarker(cleanPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to apply visual marker for '{Path}' (non-critical)", cleanPath);
                }

                result.IsSuccess = true;
                result.VisualIconUpdated = visualApplied;

                // 5. Fire event
                PrintStatusChanged?.Invoke(cleanPath, PrintStatus.Printed);
                _logger?.LogInformation("Folder marked as PRINTED: '{Path}'", cleanPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Critical failure marking folder as PRINTED: '{Path}'", cleanPath);
                result.IsSuccess = false;
                result.ErrorMessage = $"Lỗi lưu trạng thái ĐÃ IN: {ex.Message}";
            }
        }
        else
        {
            // --- TURN OFF: PRINTED -> NOT_PRINTED ---
            try
            {
                // 1. Update Database (mark as NotPrinted)
                var record = new FolderPrintRecord
                {
                    FolderPath = cleanPath,
                    NormalizedPath = norm,
                    Status = PrintStatus.NotPrinted,
                    MarkedAt = DateTimeOffset.UtcNow,
                    MarkedBy = markedBy
                };
                await _folderPrintRepository.SaveAsync(record, cancellationToken);

                // 2. Synchronize associated order
                await SyncAssociatedOrderPrintedStateAsync(cleanPath, false, null, cancellationToken);

                // 3. Remove hidden marker file (.lalab-printed)
                try
                {
                    string markerPath = Path.Combine(cleanPath, MarkerFileName);
                    if (File.Exists(markerPath))
                    {
                        File.SetAttributes(markerPath, FileAttributes.Normal);
                        File.Delete(markerPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to delete marker file in '{Path}' (non-critical)", cleanPath);
                }

                // 4. Remove Visual Marker (delete desktop.ini + clear ReadOnly + SHChangeNotify)
                bool visualRemoved = false;
                try
                {
                    visualRemoved = _visualMarkerService.RemoveVisualMarker(cleanPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to remove visual marker for '{Path}' (non-critical)", cleanPath);
                }

                result.IsSuccess = true;
                result.VisualIconUpdated = visualRemoved;

                // 5. Fire event
                PrintStatusChanged?.Invoke(cleanPath, PrintStatus.NotPrinted);
                _logger?.LogInformation("Folder unmarked from PRINTED: '{Path}'", cleanPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Critical failure unmarking folder as PRINTED: '{Path}'", cleanPath);
                result.IsSuccess = false;
                result.ErrorMessage = $"Lỗi hủy trạng thái ĐÃ IN: {ex.Message}";
            }
        }

        return result;
    }

    public async Task<IReadOnlySet<string>> GetAllPrintedNormalizedPathsAsync(CancellationToken cancellationToken = default)
    {
        return await _folderPrintRepository.GetAllPrintedNormalizedPathsAsync(cancellationToken);
    }

    public async Task ReconcileFolderStatusAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        string norm = PathNormalizer.Normalize(folderPath);
        var dbRecord = await _folderPrintRepository.GetByNormalizedPathAsync(norm, cancellationToken);
        string markerPath = Path.Combine(folderPath, MarkerFileName);
        bool markerExists = File.Exists(markerPath);

        if (dbRecord != null && dbRecord.Status == PrintStatus.Printed && !markerExists)
        {
            // DB says printed, recreate missing marker
            try
            {
                WriteMarkerFile(markerPath, dbRecord.MarkedAt, dbRecord.MarkedBy);
            }
            catch { }
        }
        else if ((dbRecord == null || dbRecord.Status == PrintStatus.NotPrinted) && markerExists)
        {
            // Marker exists but DB says not printed -> recover into DB
            var rec = new FolderPrintRecord
            {
                FolderPath = folderPath,
                NormalizedPath = norm,
                Status = PrintStatus.Printed,
                MarkedAt = DateTimeOffset.UtcNow,
                MarkedBy = "MarkerRecovery"
            };
            await _folderPrintRepository.SaveAsync(rec, cancellationToken);
        }
    }

    private static void WriteMarkerFile(string markerPath, DateTimeOffset markedAt, string markedBy)
    {
        if (File.Exists(markerPath))
        {
            try
            {
                File.SetAttributes(markerPath, FileAttributes.Normal);
            }
            catch { }
        }

        var markerData = new
        {
            status = "PRINTED",
            markedAt = markedAt.ToString("o"),
            markedBy = markedBy,
            version = 1
        };

        string json = JsonSerializer.Serialize(markerData);
        File.WriteAllText(markerPath, json);
        File.SetAttributes(markerPath, FileAttributes.Hidden);
    }

    private async Task SyncAssociatedOrderPrintedStateAsync(string folderPath, bool isPrinted, DateTimeOffset? printedAt, CancellationToken cancellationToken)
    {
        if (_orderRepository == null || _settingsRepository == null) return;

        try
        {
            var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(settings.RootFolder) &&
                folderPath.StartsWith(settings.RootFolder, StringComparison.OrdinalIgnoreCase))
            {
                string rel = Path.GetRelativePath(settings.RootFolder, folderPath).Replace('/', '\\').Trim('\\');
                if (!string.IsNullOrWhiteSpace(rel))
                {
                    await _orderRepository.UpdateOrderPrintedStatusByRelativePathAsync(rel, isPrinted, printedAt, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Order sync for printed state skipped/failed for '{Path}'", folderPath);
        }
    }
}
