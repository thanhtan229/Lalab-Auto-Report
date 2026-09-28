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

    Task<IReadOnlyList<Order>> ScanDateRangeAsync(
        string startDateString,
        string endDateString,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<Order>> ScanMissingDaysAsync(
        int year,
        int month,
        IProgress<ScanProgress>? progress = null,
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
    Task<Order?> GetOrderByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderByRelativePathAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersByDateAsync(string date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersByDateRangeAsync(string startDate, string endDate, CancellationToken cancellationToken = default);
    Task SaveOrderAsync(Order order, ScanSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetScannedDatesInMonthAsync(string yearMonth, CancellationToken cancellationToken = default);
    Task UpdateOrderItemResolutionAsync(long orderItemId, int billQuantity, QuantityResolutionMode mode, string? note, CancellationToken cancellationToken = default);
    Task UpdateOrderItemPrintFolderAsync(long orderItemId, string printFolderRelativePath, int printCount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScanSnapshot>> GetScanSnapshotsForOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task UpdateOrderStatusAsync(long orderId, OrderStatus status, CancellationToken cancellationToken = default);
    Task SetFilesystemChangedAfterLockAsync(long orderId, bool changed, CancellationToken cancellationToken = default);
}

/// <summary>
/// Database migration service
/// </summary>
public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Customer and alias repository interface
/// </summary>
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Customer?> FindExactMatchAsync(string normalizedName, CancellationToken cancellationToken = default);
    Task<Customer> CreateCustomerAsync(Customer customer, string? initialAlias = null, CancellationToken cancellationToken = default);
    Task UpdateCustomerAsync(Customer customer, CancellationToken cancellationToken = default);
    Task AddAliasAsync(long customerId, string aliasText, CancellationToken cancellationToken = default);
    Task RemoveAliasAsync(long aliasId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerAlias>> GetAllAliasesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Print specification and pricing repository interface
/// </summary>
public interface IPrintSpecificationRepository
{
    Task<PrintSpecification?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrintSpecification>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<PrintSpecification?> FindExactMatchAsync(string normalizedName, CancellationToken cancellationToken = default);
    Task<PrintSpecification> CreateSpecificationAsync(PrintSpecification spec, string? initialAlias = null, CancellationToken cancellationToken = default);
    Task UpdateSpecificationAsync(PrintSpecification spec, CancellationToken cancellationToken = default);
    Task AddAliasAsync(long specId, string aliasText, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrintSpecificationAlias>> GetAllAliasesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Daily customer aggregation grouping multiple physical orders
/// </summary>
public record CustomerDailyAggregation(
    long? CustomerId,
    string DisplayName,
    string WorkDate,
    IReadOnlyList<Order> Orders,
    IReadOnlyList<Bill> Bills,
    int TotalBillQuantity,
    long TotalAmount,
    bool HasUnresolvedIssues
);

/// <summary>
/// Core billing calculation and aggregation service
/// </summary>
public interface IBillingService
{
    Task<Bill> CalculateBillForOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerDailyAggregation>> GetDailyCustomerAggregationAsync(string date, CancellationToken cancellationToken = default);
}

/// <summary>
/// Bill and bill lines repository interface
/// </summary>
public interface IBillRepository
{
    Task<Bill?> GetBillByOrderIdAsync(long orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Bill>> GetBillsByDateAsync(string date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Bill>> GetBillsByMonthAsync(string yearMonth, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Bill>> GetBillsByDateRangeAsync(string startDate, string endDate, CancellationToken cancellationToken = default);
    Task SaveBillAsync(Bill bill, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pre-lock verification result for an order
/// </summary>
public record VerificationResult(
    bool CanLock,
    IReadOnlyList<string> BlockingReasons,
    Bill? PreviewBill,
    Order Order
);

/// <summary>
/// Bill verification, locking, and post-lock change management service
/// </summary>
public interface ILockingService
{
    Task<VerificationResult> VerifyOrderForLockAsync(long orderId, CancellationToken cancellationToken = default);
    Task<Bill> VerifyAndLockOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task ReopenOrderAsync(long orderId, string reason, CancellationToken cancellationToken = default);
    Task<bool> CheckFilesystemChangedAfterLockAsync(long orderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Summary of quantities and revenue for a print specification
/// </summary>
public record SpecificationSummary(
    long PrintSpecificationId,
    string SpecificationName,
    int TotalBillQuantity,
    long TotalAmount
);

/// <summary>
/// Summary of daily operational activity
/// </summary>
public record DailySummary(
    string Date,
    int TotalOrders,
    int TotalBillQuantity,
    long TotalAmount,
    bool HasUnresolvedIssues
);

/// <summary>
/// Summary of customer activity across a month or date range
/// </summary>
public record CustomerMonthlySummary(
    long? CustomerId,
    string DisplayName,
    int TotalOrders,
    int TotalBillQuantity,
    long TotalAmount,
    bool HasUnresolvedIssues
);

/// <summary>
/// Authoritative daily report
/// </summary>
public record DailyReport(
    string Date,
    int TotalOrders,
    int TotalCustomers,
    int TotalBillQuantity,
    long TotalAmount,
    int UnresolvedOrdersCount,
    int LockedOrdersCount,
    IReadOnlyList<CustomerDailyAggregation> Customers,
    IReadOnlyList<SpecificationSummary> Specifications
);

/// <summary>
/// Authoritative monthly report loaded from SQLite
/// </summary>
public record MonthlyReport(
    int Year,
    int Month,
    int TotalOrders,
    int TotalCustomers,
    int TotalBillQuantity,
    long TotalAmount,
    int UnresolvedOrdersCount,
    int LockedOrdersCount,
    IReadOnlyList<string> MissingScanDays,
    IReadOnlyList<DailySummary> DailySummaries,
    IReadOnlyList<CustomerMonthlySummary> CustomerSummaries,
    IReadOnlyList<SpecificationSummary> SpecificationSummaries
);

/// <summary>
/// Authoritative date range report loaded from SQLite
/// </summary>
public record DateRangeReport(
    string StartDate,
    string EndDate,
    int TotalOrders,
    int TotalCustomers,
    int TotalBillQuantity,
    long TotalAmount,
    int UnresolvedOrdersCount,
    int LockedOrdersCount,
    IReadOnlyList<DailySummary> DailySummaries,
    IReadOnlyList<CustomerMonthlySummary> CustomerSummaries,
    IReadOnlyList<SpecificationSummary> SpecificationSummaries
);

/// <summary>
/// Database-only reporting service (never touches raw image trees during report generation)
/// </summary>
public interface IReportService
{
    Task<DailyReport> GetDailyReportAsync(string date, CancellationToken cancellationToken = default);
    Task<MonthlyReport> GetMonthlyReportAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<DateRangeReport> GetDateRangeReportAsync(string startDate, string endDate, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a database backup file
/// </summary>
public record DatabaseBackupInfo(
    string BackupPath,
    string FileName,
    long FileSizeBytes,
    DateTime CreatedAtUtc
);

/// <summary>
/// Database backup and restore service
/// </summary>
public interface IDatabaseBackupService
{
    Task<DatabaseBackupInfo> CreateBackupAsync(string? customDestinationPath = null, CancellationToken cancellationToken = default);
    Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DatabaseBackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default);
    string GetDatabasePath();
}

