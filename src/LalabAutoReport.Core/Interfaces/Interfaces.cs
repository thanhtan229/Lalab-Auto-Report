using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.Core.Interfaces;

/// <summary>
/// Abstraction over the physical filesystem to allow deterministic testing with mocks or fixtures
/// </summary>
public interface IFileSystemAdapter
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IEnumerable<string> EnumerateDirectories(string path);
    IEnumerable<string> EnumerateFiles(string path);
    string GetRelativePath(string relativeTo, string path);
    string Combine(params string[] paths);
    string GetFileName(string path);
    string GetDirectoryName(string path);
    string GetExtension(string path);
    void OpenDirectoryInShell(string path);
}

/// <summary>
/// Result of resolving print folder candidate in a print-spec folder
/// </summary>
public record PrintFolderResolutionResult(
    PrintFolderResolutionStatus Status,
    string? SelectedPrintFolderFullPath,
    string? SelectedPrintFolderRelativePath,
    int? PrintCount,
    IReadOnlyList<string> CandidatePrintFolderFullPaths,
    IReadOnlyList<string> CandidatePrintFolderRelativePaths,
    string? ErrorMessage = null
);

/// <summary>
/// Analyzes folder hierarchies under a print specification to find leaf image folders
/// </summary>
public interface IPrintFolderResolver
{
    PrintFolderResolutionResult ResolvePrintFolder(
        string specFolderFullPath,
        string rootFolderFullPath,
        IReadOnlySet<string> supportedExtensions,
        string? previouslySelectedRelativePath = null
    );
}

/// <summary>
/// Parsed representation of an order item (specification) on disk
/// </summary>
public record DiscoveredSpecification(
    string FolderName,
    string FullPath,
    string RelativePath
);

/// <summary>
/// Parsed representation of an order folder on disk
/// </summary>
public record DiscoveredOrder(
    string Date,
    string OriginalCustomerFolderName,
    string FullPath,
    string RelativePath,
    IReadOnlyList<DiscoveredSpecification> Specifications
);

/// <summary>
/// Parses date and customer folder structures
/// </summary>
public interface IFolderStructureParser
{
    IReadOnlyList<string> DiscoverDateFolders(string rootFolder);
    IReadOnlyList<DiscoveredOrder> DiscoverOrdersForDate(string rootFolder, string dateString);
    DiscoveredOrder? DiscoverSingleOrder(string rootFolder, string orderRelativePath);
    DiscoveredSpecification? DiscoverSingleSpecification(string rootFolder, string specRelativePath);
}

/// <summary>
/// Result of customer alias resolution
/// </summary>
public record CustomerResolutionResult(
    CustomerResolutionStatus Status,
    Customer? ResolvedCustomer,
    IReadOnlyList<Customer> SuggestedCustomers,
    string? ErrorMessage = null
);

/// <summary>
/// Customer alias and identity resolution service
/// </summary>
public interface ICustomerResolver
{
    Task<CustomerResolutionResult> ResolveCustomerAsync(string folderName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of print specification resolution
/// </summary>
public record PrintSpecificationResolutionResult(
    PrintSpecificationResolutionStatus Status,
    PrintSpecification? ResolvedSpecification,
    string? ErrorMessage = null
);

/// <summary>
/// Print specification resolution service
/// </summary>
public interface IPrintSpecificationResolver
{
    Task<PrintSpecificationResolutionResult> ResolveSpecificationAsync(string folderName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Progress reporting during scanning
/// </summary>
public record ScanProgress(
    string CurrentStep,
    int CompletedItems,
    int TotalItems,
    string? CurrentItemName = null
);

/// <summary>
/// Core Smart Scan service
/// </summary>
public interface IScanService
{
    Task<IReadOnlyList<Order>> ScanDateAsync(
        string dateString,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task<Order?> ScanOrderAsync(
        string orderRelativePath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task<OrderItemScan?> ScanSpecificationAsync(
        string specRelativePath,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<string>> GetMissingScanDaysAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Settings persistence interface
/// </summary>
public interface ISettingsRepository
{
    Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Order and scan snapshot persistence interface
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetOrderByRelativePathAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersByDateAsync(string date, CancellationToken cancellationToken = default);
    Task SaveOrderAsync(Order order, ScanSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetScannedDatesInMonthAsync(string yearMonth, CancellationToken cancellationToken = default);
    Task UpdateOrderItemResolutionAsync(long orderItemId, int billQuantity, QuantityResolutionMode mode, string? note, CancellationToken cancellationToken = default);
    Task UpdateOrderItemPrintFolderAsync(long orderItemId, string printFolderRelativePath, int printCount, CancellationToken cancellationToken = default);
}

/// <summary>
/// Database migration service
/// </summary>
public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
