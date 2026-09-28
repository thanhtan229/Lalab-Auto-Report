using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class ScanService : IScanService
{
    private readonly IFileSystemAdapter _fileSystem;
    private readonly IFolderStructureParser _structureParser;
    private readonly IPrintFolderResolver _printFolderResolver;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IOrderRepository? _orderRepository;
    private readonly IBillRepository? _billRepository;
    private readonly ICustomerResolver? _customerResolver;
    private readonly IPrintSpecificationResolver? _specificationResolver;
    private readonly ILogger<ScanService>? _logger;

    public ScanService(
        IFileSystemAdapter fileSystem,
        IFolderStructureParser structureParser,
        IPrintFolderResolver printFolderResolver,
        ISettingsRepository settingsRepository,
        IOrderRepository? orderRepository = null,
        ICustomerResolver? customerResolver = null,
        IPrintSpecificationResolver? specificationResolver = null,
        IBillRepository? billRepository = null,
        ILogger<ScanService>? logger = null)
    {
        _fileSystem = fileSystem;
        _structureParser = structureParser;
        _printFolderResolver = printFolderResolver;
        _settingsRepository = settingsRepository;
        _orderRepository = orderRepository;
        _customerResolver = customerResolver;
        _specificationResolver = specificationResolver;
        _billRepository = billRepository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Order>> ScanDateAsync(
        string dateString,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Starting scan for date '{Date}'", dateString);
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
        string rootFolder = settings.RootFolder;

        if (string.IsNullOrWhiteSpace(rootFolder) || !_fileSystem.DirectoryExists(rootFolder))
        {
            throw new DirectoryNotFoundException($"Root folder '{rootFolder}' does not exist or is not configured.");
        }

        var supportedExts = new HashSet<string>(settings.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        var discoveredOrders = _structureParser.DiscoverOrdersForDate(rootFolder, dateString);

        var resultOrders = new List<Order>();
        int total = discoveredOrders.Count;
        int completed = 0;

        foreach (var discOrder in discoveredOrders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ScanProgress(
                CurrentStep: $"Scanning {discOrder.OriginalCustomerFolderName} ({completed + 1}/{total})",
                CompletedItems: completed,
                TotalItems: total,
                CurrentItemName: discOrder.OriginalCustomerFolderName
            ));

            var order = await ProcessDiscoveredOrderAsync(rootFolder, discOrder, supportedExts, ScanScope.Date, cancellationToken);
            resultOrders.Add(order);

            completed++;
            progress?.Report(new ScanProgress(
                CurrentStep: $"Finished {discOrder.OriginalCustomerFolderName}",
                CompletedItems: completed,
                TotalItems: total,
                CurrentItemName: discOrder.OriginalCustomerFolderName
            ));
        }

        _logger?.LogInformation("Completed scan for date '{Date}': {Count} orders processed.", dateString, resultOrders.Count);
        return resultOrders;
    }

    public async Task<Order?> ScanOrderAsync(
        string orderRelativePath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
        string rootFolder = settings.RootFolder;

        var discOrder = _structureParser.DiscoverSingleOrder(rootFolder, orderRelativePath);
        if (discOrder == null)
        {
            _logger?.LogWarning("Order at '{Path}' not found on disk.", orderRelativePath);
            return null;
        }

        var supportedExts = new HashSet<string>(settings.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        return await ProcessDiscoveredOrderAsync(rootFolder, discOrder, supportedExts, ScanScope.Order, cancellationToken);
    }

    public async Task<OrderItemScan?> ScanSpecificationAsync(
        string specRelativePath,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
        string rootFolder = settings.RootFolder;

        var discSpec = _structureParser.DiscoverSingleSpecification(rootFolder, specRelativePath);
        if (discSpec == null)
        {
            _logger?.LogWarning("Specification at '{Path}' not found on disk.", specRelativePath);
            return null;
        }

        var supportedExts = new HashSet<string>(settings.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        return await ScanSpecificationInternalAsync(rootFolder, discSpec, supportedExts, null, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetMissingScanDaysAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
        string rootFolder = settings.RootFolder;

        if (string.IsNullOrWhiteSpace(rootFolder) || !_fileSystem.DirectoryExists(rootFolder))
        {
            return Array.Empty<string>();
        }

        string prefix = $"{year:D4}-{month:D2}";
        var diskDates = _structureParser.DiscoverDateFolders(rootFolder)
            .Where(d => d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (_orderRepository == null)
        {
            return diskDates;
        }

        var scannedDates = await _orderRepository.GetScannedDatesInMonthAsync(prefix, cancellationToken);
        var scannedSet = new HashSet<string>(scannedDates, StringComparer.OrdinalIgnoreCase);

        var missing = diskDates.Where(d => !scannedSet.Contains(d)).OrderBy(d => d).ToList();
        return missing;
    }

    public async Task<IReadOnlyList<Order>> ScanDateRangeAsync(
        string startDateString,
        string endDateString,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetSettingsAsync(cancellationToken);
        string rootFolder = settings.RootFolder;

        if (string.IsNullOrWhiteSpace(rootFolder) || !_fileSystem.DirectoryExists(rootFolder))
        {
            throw new DirectoryNotFoundException($"Root folder '{rootFolder}' does not exist or is not configured.");
        }

        var allDates = _structureParser.DiscoverDateFolders(rootFolder);
        var inRangeDates = allDates
            .Where(d => string.Compare(d, startDateString, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        string.Compare(d, endDateString, StringComparison.OrdinalIgnoreCase) <= 0)
            .OrderBy(d => d)
            .ToList();

        var orders = new List<Order>();
        int totalDates = inRangeDates.Count;
        int completedDates = 0;

        foreach (var date in inRangeDates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ScanProgress(
                CurrentStep: $"Quét ngày {date} ({completedDates + 1}/{totalDates})",
                CompletedItems: completedDates,
                TotalItems: totalDates,
                CurrentItemName: date
            ));

            var dateOrders = await ScanDateAsync(date, null, cancellationToken);
            orders.AddRange(dateOrders);

            completedDates++;
            progress?.Report(new ScanProgress(
                CurrentStep: $"Hoàn thành ngày {date}",
                CompletedItems: completedDates,
                TotalItems: totalDates,
                CurrentItemName: date
            ));
        }

        return orders;
    }

    public async Task<IReadOnlyList<Order>> ScanMissingDaysAsync(
        int year,
        int month,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var missingDays = await GetMissingScanDaysAsync(year, month, cancellationToken);
        var orders = new List<Order>();
        int total = missingDays.Count;
        int completed = 0;

        foreach (var date in missingDays)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ScanProgress(
                CurrentStep: $"Quét ngày thiếu: {date} ({completed + 1}/{total})",
                CompletedItems: completed,
                TotalItems: total,
                CurrentItemName: date
            ));

            var dateOrders = await ScanDateAsync(date, null, cancellationToken);
            orders.AddRange(dateOrders);

            completed++;
            progress?.Report(new ScanProgress(
                CurrentStep: $"Đã quét ngày thiếu: {date}",
                CompletedItems: completed,
                TotalItems: total,
                CurrentItemName: date
            ));
        }

        return orders;
    }

    private async Task<Order> ProcessDiscoveredOrderAsync(
        string rootFolder,
        DiscoveredOrder discOrder,
        HashSet<string> supportedExts,
        ScanScope scope,
        CancellationToken cancellationToken)
    {
        // Check if order exists in DB to reuse ID and customer mapping
        Order? existingOrder = null;
        if (_orderRepository != null)
        {
            existingOrder = await _orderRepository.GetOrderByRelativePathAsync(discOrder.RelativePath, cancellationToken);
        }

        var order = existingOrder ?? new Order
        {
            WorkDate = discOrder.Date,
            OriginalFolderName = discOrder.OriginalCustomerFolderName,
            RelativePath = discOrder.RelativePath,
            CreatedAt = DateTimeOffset.UtcNow
        };

        order.LastScanAt = DateTimeOffset.UtcNow;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        bool hasIssues = false;

        // Resolve Customer Identity
        if (_customerResolver != null && order.CustomerId == null)
        {
            var custResult = await _customerResolver.ResolveCustomerAsync(discOrder.OriginalCustomerFolderName, cancellationToken);
            if (custResult.Status == CustomerResolutionStatus.ExactMatch || custResult.Status == CustomerResolutionStatus.NormalizedMatch)
            {
                order.CustomerId = custResult.ResolvedCustomer?.Id;
                order.Customer = custResult.ResolvedCustomer;
            }
            else
            {
                hasIssues = true; // Unknown customer or collision
            }
        }
        else if (order.CustomerId == null)
        {
            // No customer resolver configured
        }

        var snapshot = new ScanSnapshot
        {
            OrderId = order.Id,
            StartedAt = DateTimeOffset.UtcNow,
            Scope = scope,
            Status = ScanStatus.Success
        };

        var scannedItems = new List<OrderItemScan>();

        foreach (var discSpec in discOrder.Specifications)
        {
            // Check if there was a previously selected print folder for this spec
            string? previouslySelectedPrintFolder = existingOrder?.Items
                .FirstOrDefault(i => string.Equals(i.SpecificationRelativePath, discSpec.RelativePath, StringComparison.OrdinalIgnoreCase))
                ?.SelectedPrintFolderRelativePath;

            var itemScan = await ScanSpecificationInternalAsync(rootFolder, discSpec, supportedExts, previouslySelectedPrintFolder, cancellationToken);
            scannedItems.Add(itemScan);

            if (itemScan.PrintFolderStatus != PrintFolderResolutionStatus.Resolved ||
                itemScan.MismatchCount != 0 ||
                itemScan.ScanStatus != ScanStatus.Success ||
                (_specificationResolver != null && itemScan.PrintSpecificationId == null))
            {
                hasIssues = true;
            }
        }

        order.Items = scannedItems;
        snapshot.Items = scannedItems;
        snapshot.CompletedAt = DateTimeOffset.UtcNow;

        if (existingOrder != null && existingOrder.Status == OrderStatus.Locked)
        {
            // Locked orders remain Locked!
            order.Status = OrderStatus.Locked;

            // Check if filesystem changed after lock
            if (_billRepository != null)
            {
                var lockedBill = await _billRepository.GetBillByOrderIdAsync(order.Id, cancellationToken);
                if (lockedBill != null && lockedBill.Status == OrderStatus.Locked)
                {
                    bool changed = HasFilesystemChangedFromLockedBill(lockedBill, scannedItems);
                    order.FilesystemChangedAfterLock = changed;
                    if (changed)
                    {
                        _logger?.LogWarning("Filesystem changed after lock detected for order {OrderId} ({Folder})", order.Id, order.OriginalFolderName);
                    }
                }
            }
        }
        else if (scannedItems.Count == 0 || hasIssues)
        {
            order.Status = OrderStatus.NeedsReview;
        }
        else
        {
            order.Status = OrderStatus.Ready;
        }

        if (_orderRepository != null)
        {
            await _orderRepository.SaveOrderAsync(order, snapshot, cancellationToken);
        }

        return order;
    }

    private static bool HasFilesystemChangedFromLockedBill(Bill lockedBill, List<OrderItemScan> currentItems)
    {
        if (lockedBill.Lines.Count != currentItems.Count)
        {
            return true;
        }

        foreach (var line in lockedBill.Lines)
        {
            var matchingItem = currentItems.FirstOrDefault(i => i.PrintSpecificationId == line.PrintSpecificationId);
            if (matchingItem == null)
            {
                return true;
            }

            if (matchingItem.SourceCount != line.SourceCount || matchingItem.PrintCount != line.PrintCount)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<OrderItemScan> ScanSpecificationInternalAsync(
        string rootFolder,
        DiscoveredSpecification discSpec,
        HashSet<string> supportedExts,
        string? previouslySelectedPrintFolder = null,
        CancellationToken cancellationToken = default)
    {
        var itemScan = new OrderItemScan
        {
            SpecificationFolderName = discSpec.FolderName,
            SpecificationRelativePath = discSpec.RelativePath,
            ScanStatus = ScanStatus.Success
        };

        try
        {
            // Resolve Print Specification
            if (_specificationResolver != null)
            {
                var specResult = await _specificationResolver.ResolveSpecificationAsync(discSpec.FolderName, cancellationToken);
                if (specResult.Status == PrintSpecificationResolutionStatus.Resolved)
                {
                    itemScan.PrintSpecificationId = specResult.ResolvedSpecification?.Id;
                    itemScan.PrintSpecification = specResult.ResolvedSpecification;
                }
                else
                {
                    itemScan.PrintSpecificationId = null;
                    itemScan.ScanStatus = ScanStatus.Warning;
                    itemScan.ErrorMessage = specResult.ErrorMessage ?? "Chưa nhận diện quy cách";
                }
            }

            // 1. Source count directly inside the specification folder (non-recursive!)
            int sourceCount = 0;
            foreach (var file in _fileSystem.EnumerateFiles(discSpec.FullPath))
            {
                string ext = _fileSystem.GetExtension(file);
                if (supportedExts.Contains(ext))
                {
                    sourceCount++;
                }
            }
            itemScan.SourceCount = sourceCount;

            // 2. Resolve Print Folder
            var printResult = _printFolderResolver.ResolvePrintFolder(
                discSpec.FullPath,
                rootFolder,
                supportedExts,
                previouslySelectedPrintFolder
            );

            itemScan.PrintFolderStatus = printResult.Status;
            itemScan.SelectedPrintFolderRelativePath = printResult.SelectedPrintFolderRelativePath;
            itemScan.PrintCount = printResult.PrintCount;
            itemScan.CandidatePrintFolderRelativePaths = printResult.CandidatePrintFolderRelativePaths.ToList();

            if (!string.IsNullOrEmpty(printResult.ErrorMessage))
            {
                itemScan.ErrorMessage = printResult.ErrorMessage;
            }

            // 3. Evaluate mismatch
            if (printResult.Status == PrintFolderResolutionStatus.Resolved && printResult.PrintCount.HasValue)
            {
                itemScan.MismatchCount = printResult.PrintCount.Value - sourceCount;

                if (sourceCount == printResult.PrintCount.Value)
                {
                    // Auto match!
                    itemScan.BillQuantity = printResult.PrintCount.Value;
                    itemScan.QuantityResolutionMode = QuantityResolutionMode.AutoMatch;
                }
                else
                {
                    // Source != Print: Mismatch! Requires user decision
                    itemScan.BillQuantity = null;
                    itemScan.QuantityResolutionMode = null;
                }
            }
            else
            {
                // Ambiguous or no print folder
                itemScan.MismatchCount = null;
                itemScan.BillQuantity = null;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed scanning specification '{Spec}'", discSpec.RelativePath);
            itemScan.ScanStatus = ScanStatus.Failed;
            itemScan.ErrorMessage = ex.Message;
        }

        return itemScan;
    }
}
