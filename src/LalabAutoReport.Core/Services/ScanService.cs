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
    private readonly ILogger<ScanService>? _logger;

    public ScanService(
        IFileSystemAdapter fileSystem,
        IFolderStructureParser structureParser,
        IPrintFolderResolver printFolderResolver,
        ISettingsRepository settingsRepository,
        IOrderRepository? orderRepository = null,
        ILogger<ScanService>? logger = null)
    {
        _fileSystem = fileSystem;
        _structureParser = structureParser;
        _printFolderResolver = printFolderResolver;
        _settingsRepository = settingsRepository;
        _orderRepository = orderRepository;
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
        return ScanSpecificationInternal(rootFolder, discSpec, supportedExts);
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

        var snapshot = new ScanSnapshot
        {
            OrderId = order.Id,
            StartedAt = DateTimeOffset.UtcNow,
            Scope = scope,
            Status = ScanStatus.Success
        };

        var scannedItems = new List<OrderItemScan>();
        bool hasIssues = false;

        foreach (var discSpec in discOrder.Specifications)
        {
            // Check if there was a previously selected print folder for this spec
            string? previouslySelectedPrintFolder = existingOrder?.Items
                .FirstOrDefault(i => string.Equals(i.SpecificationRelativePath, discSpec.RelativePath, StringComparison.OrdinalIgnoreCase))
                ?.SelectedPrintFolderRelativePath;

            var itemScan = ScanSpecificationInternal(rootFolder, discSpec, supportedExts, previouslySelectedPrintFolder);
            scannedItems.Add(itemScan);

            if (itemScan.PrintFolderStatus != PrintFolderResolutionStatus.Resolved ||
                itemScan.MismatchCount != 0 ||
                itemScan.ScanStatus != ScanStatus.Success)
            {
                hasIssues = true;
            }
        }

        order.Items = scannedItems;
        snapshot.Items = scannedItems;
        snapshot.CompletedAt = DateTimeOffset.UtcNow;

        if (scannedItems.Count == 0)
        {
            order.Status = OrderStatus.NeedsReview;
        }
        else if (hasIssues)
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

    private OrderItemScan ScanSpecificationInternal(
        string rootFolder,
        DiscoveredSpecification discSpec,
        HashSet<string> supportedExts,
        string? previouslySelectedPrintFolder = null)
    {
        var itemScan = new OrderItemScan
        {
            SpecificationFolderName = discSpec.FolderName,
            SpecificationRelativePath = discSpec.RelativePath,
            ScanStatus = ScanStatus.Success
        };

        try
        {
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
            itemScan.ErrorMessage = printResult.ErrorMessage;

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
