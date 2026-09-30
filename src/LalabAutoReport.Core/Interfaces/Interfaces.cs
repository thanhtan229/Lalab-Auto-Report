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
/// Result of resolving the Effective Billing Folder in a product job folder
/// </summary>
public record PrintFolderResolutionResult(
    PrintFolderResolutionStatus Status,
    string? SelectedPrintFolderFullPath,
    string? SelectedPrintFolderRelativePath,
    int? PrintCount,
    IReadOnlyList<string> CandidatePrintFolderFullPaths,
    IReadOnlyList<string> CandidatePrintFolderRelativePaths,
    string? ErrorMessage = null,
    BillingFolderResolutionMode ResolutionMode = BillingFolderResolutionMode.AutoResolved
)
{
    // Semantic aliases for Effective Billing Folder (Section 1)
    public string? EffectiveBillingFolderFullPath => SelectedPrintFolderFullPath;
    public string? EffectiveBillingFolderRelativePath => SelectedPrintFolderRelativePath;
    public int? EffectiveBillingCount => PrintCount;
}

/// <summary>
/// Analyzes folder hierarchies under a product job to resolve the Effective Billing Folder
/// </summary>
public interface IPrintFolderResolver
{
    PrintFolderResolutionResult ResolvePrintFolder(
        string specFolderFullPath,
        string rootFolderFullPath,
        IReadOnlySet<string> supportedExtensions,
        string? manualSelectedRelativePath = null
    );

    /// <summary>
    /// Semantic alias method for resolving the Effective Billing Folder
    /// </summary>
    PrintFolderResolutionResult ResolveEffectiveBillingFolder(
        string specFolderFullPath,
        string rootFolderFullPath,
        IReadOnlySet<string> supportedExtensions,
        string? manualSelectedRelativePath = null
    ) => ResolvePrintFolder(specFolderFullPath, rootFolderFullPath, supportedExtensions, manualSelectedRelativePath);
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
    IReadOnlyList<DiscoveredSpecification> Specifications,
    OrderKind Kind = OrderKind.Implicit,
    string? OrderName = null
);


/// <summary>
/// Parses date and customer folder structures
/// </summary>
public interface IFolderStructureParser
{
    IReadOnlyList<string> DiscoverDateFolders(string rootFolder);
    IReadOnlyList<DiscoveredOrder> DiscoverOrdersForDate(string rootFolder, string dateString);
    IReadOnlyList<DiscoveredOrder> DiscoverOrdersInFolder(string rootFolder, string folderPath, string? dateString = null);
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
/// Result of product / variant resolution in V2
/// </summary>
public record ProductResolutionResult(
    PrintSpecificationResolutionStatus Status,
    ProductVariant? ResolvedVariant,
    ProductFamily? ResolvedFamily,
    string? CanonicalSize,
    IReadOnlyList<ProductVariant> Candidates,
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
/// Product Family and Variant resolution service in V2
/// </summary>
public interface IProductResolver : IPrintSpecificationResolver
{
    Task<ProductResolutionResult> ResolveProductAsync(string folderName, CancellationToken cancellationToken = default);
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
    Task<Order?> GetOrderByCodeAsync(string orderCode, CancellationToken cancellationToken = default);
    Task<int> GetMaxOrderSequenceForDateAsync(string workDate, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderByRelativePathAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersByDateAsync(string date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersByDateRangeAsync(string startDate, string endDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);
    Task SaveOrderAsync(Order order, ScanSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetScannedDatesInMonthAsync(string yearMonth, CancellationToken cancellationToken = default);
    Task UpdateOrderItemResolutionAsync(long orderItemId, int billQuantity, QuantityResolutionMode mode, string? note, CancellationToken cancellationToken = default);
    Task UpdateOrderItemPrintFolderAsync(long orderItemId, string printFolderRelativePath, int printCount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScanSnapshot>> GetScanSnapshotsForOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task UpdateOrderStatusAsync(long orderId, OrderStatus status, CancellationToken cancellationToken = default);
    Task SetFilesystemChangedAfterLockAsync(long orderId, bool changed, CancellationToken cancellationToken = default);
    Task SetCustomerBillIdForItemsAsync(IEnumerable<long> orderItemIds, long? customerBillId, CancellationToken cancellationToken = default);
    Task UpdateOrderCustomerIdAsync(long orderId, long customerId, CancellationToken cancellationToken = default);
    Task UpdateFolderCustomerIdAsync(string workDate, string originalFolderName, long? customerId, string? orderName = null, CancellationToken cancellationToken = default);
    Task UpdateOrderPrintedStatusAsync(long orderId, bool isPrinted, DateTimeOffset? printedAt, CancellationToken cancellationToken = default);
    Task UpdateOrderPrintedStatusByRelativePathAsync(string relativePath, bool isPrinted, DateTimeOffset? printedAt, CancellationToken cancellationToken = default);
    Task DeleteOrderAsync(long orderId, CancellationToken cancellationToken = default);
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
    Task UpdateAliasAsync(long aliasId, string newAliasText, CancellationToken cancellationToken = default);
    Task RemoveAliasAsync(long aliasId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerAlias>> GetAllAliasesAsync(CancellationToken cancellationToken = default);
    Task<CustomerAlias?> FindAliasByTextAsync(string aliasText, CancellationToken cancellationToken = default);
    Task ReassignAliasAsync(long aliasId, long newCustomerId, CancellationToken cancellationToken = default);
    Task<bool> HasCustomerHistoryAsync(long customerId, CancellationToken cancellationToken = default);
    Task DeleteCustomerAsync(long customerId, CancellationToken cancellationToken = default);
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
    Task DeleteSpecificationAsync(long specId, CancellationToken cancellationToken = default);
    Task AddAliasAsync(long specId, string aliasText, CancellationToken cancellationToken = default);
    Task UpdateAliasAsync(long aliasId, string newAliasText, CancellationToken cancellationToken = default);
    Task RemoveAliasAsync(long aliasId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrintSpecificationAlias>> GetAllAliasesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Product Family and Variant repository interface for V2
/// </summary>
public interface IProductRepository : IPrintSpecificationRepository
{
    // Families
    Task<ProductFamily?> GetFamilyByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<ProductFamily?> GetFamilyByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductFamily>> GetAllFamiliesAsync(CancellationToken cancellationToken = default);
    Task<ProductFamily> CreateFamilyAsync(ProductFamily family, IEnumerable<string>? initialAliases = null, CancellationToken cancellationToken = default);
    Task UpdateFamilyAsync(ProductFamily family, CancellationToken cancellationToken = default);
    Task DeleteFamilyAsync(long familyId, CancellationToken cancellationToken = default);
    Task AddFamilyAliasAsync(long familyId, string aliasText, CancellationToken cancellationToken = default);
    Task RemoveFamilyAliasAsync(long aliasId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductFamilyAlias>> GetAllFamilyAliasesAsync(CancellationToken cancellationToken = default);

    // Variants
    Task<ProductVariant?> GetVariantByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductVariant>> GetAllVariantsAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductVariant>> GetVariantsByFamilyIdAsync(long familyId, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ProductVariant> CreateVariantAsync(ProductVariant variant, string? initialAlias = null, CancellationToken cancellationToken = default);
    Task UpdateVariantAsync(ProductVariant variant, CancellationToken cancellationToken = default);
    Task DeleteVariantAsync(long variantId, CancellationToken cancellationToken = default);

    // Specific Aliases
    Task AddSpecificAliasAsync(long variantId, string aliasText, CancellationToken cancellationToken = default);
    Task UpdateSpecificAliasAsync(long aliasId, string newAliasText, CancellationToken cancellationToken = default);
    Task RemoveSpecificAliasAsync(long aliasId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductSpecificAlias>> GetAllSpecificAliasesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductVariant>> FindVariantsBySpecificAliasAsync(string normalizedAlias, CancellationToken cancellationToken = default);

    // Collision detection
    Task<bool> HasVariantCollisionAsync(long familyId, string canonicalSize, long? excludeVariantId = null, CancellationToken cancellationToken = default);
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

/// <summary>
/// Database reset service for danger zone operations
/// </summary>
public interface IDatabaseResetService
{
    Task<DatabaseBackupInfo> ResetDataAsync(Domain.ResetDataScope scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Unbilled status overview for a customer
/// </summary>
public record CustomerUnbilledSummary(
    long CustomerId,
    string CustomerName,
    int UnbilledOrderCount,
    int UnbilledJobCount,
    long EstimatedTotal,
    string? LastBillDate,
    string? LastBillNumber,
    bool HasOrdersFromPreviousPeriod
);

/// <summary>
/// <summary>
/// Result of generating or refreshing a customer bill draft
/// </summary>
public record CustomerBillDraftResult(
    CustomerBill Draft,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> BlockingIssues,
    Customer? SuggestedCustomer = null
);

/// <summary>
/// Warning for a source folder previously billed in an exported or locked bill
/// </summary>
public record DuplicateFolderWarning(
    string FolderPath,
    string NormalizedFolderPath,
    long PreviousBillId,
    string PreviousBillNumber,
    string PreviousBillDate,
    long PreviousBillGrandTotal,
    CustomerBillStatus PreviousBillStatus,
    string? PreviousBillExportPath
);

/// <summary>
/// Repository for customer-level aggregated bills and guest quick bills
/// </summary>
public interface ICustomerBillRepository
{
    Task<CustomerBill?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<CustomerBill?> GetByBillNumberAsync(string billNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerBill>> GetAllBillsAsync(BillType? typeFilter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerBill>> GetBillsByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);
    Task<CustomerBill?> GetLastLockedOrExportedBillByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);
    Task<CustomerBill?> GetLockedBillByOrderIdAsync(long orderId, CancellationToken cancellationToken = default);
    Task<CustomerBill?> GetActiveDraftByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerBill>> GetBillsBySourceFolderPathAsync(string normalizedFolderPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GuestBillSourceFolder>> GetSourceFoldersByBillIdAsync(long billId, CancellationToken cancellationToken = default);
    Task SaveBillAsync(CustomerBill bill, CancellationToken cancellationToken = default);
    Task LockCustomerBillAtomicAsync(CustomerBill bill, IReadOnlyList<long> productJobIds, IReadOnlyList<long> orderIds, CancellationToken cancellationToken = default);
    Task ReopenCustomerBillAtomicAsync(CustomerBill bill, IReadOnlyList<long> productJobIds, IReadOnlyList<long> orderIds, CancellationToken cancellationToken = default);
    Task DeleteDraftBillAsync(long billId, CancellationToken cancellationToken = default);
    Task DeleteBillAsync(long billId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerBill>> GetBillsByDateAsync(string date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerBill>> GetBillsByMonthAsync(string yearMonth, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerBill>> GetBillsByDateRangeAsync(string startDate, string endDate, CancellationToken cancellationToken = default);
    Task<string> GenerateNextBillNumberAsync(string date, CancellationToken cancellationToken = default);
}

/// <summary>
/// Domain service for customer billing and guest quick billing workflow
/// </summary>
public interface ICustomerBillingService
{
    Task<CustomerUnbilledSummary> GetCustomerUnbilledSummaryAsync(long customerId, CancellationToken cancellationToken = default);
    Task<CustomerBillDraftResult> BuildOrRefreshDraftAsync(long customerId, bool forceRescan = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DuplicateFolderWarning>> CheckDuplicateSourceFoldersAsync(IEnumerable<string> folderPaths, CancellationToken cancellationToken = default);
    Task<CustomerBillDraftResult> BuildGuestBillDraftAsync(IReadOnlyList<string> sourceFolderPaths, string? customGuestName = null, long? existingDraftId = null, CancellationToken cancellationToken = default);
    Task<Customer> ConvertGuestBillToCustomerAsync(long billId, string customerCanonicalName, CancellationToken cancellationToken = default);
    CustomerBill RecalculateTotals(CustomerBill bill);
    Task<CustomerBill> LockBillAsync(CustomerBill draft, CancellationToken cancellationToken = default);
    Task ReopenBillAsync(long billId, string reason, CancellationToken cancellationToken = default);
    Task RecordBillExportedAsync(long billId, string exportFilePath, CancellationToken cancellationToken = default);
    Task DetachOrderFromCustomerBillAsync(long orderId, CancellationToken cancellationToken = default);
    Task SyncBillWithScannedOrderAsync(Order scannedOrder, CancellationToken cancellationToken = default);
}

/// <summary>
/// JPEG Export Service for customer bills
/// </summary>
public interface IJpegBillExporter
{
    Task<string> ExportBillToJpegAsync(CustomerBill bill, string? destinationDirectory = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Excel Export Service for customer bills (.xlsx)
/// </summary>
public interface IExcelBillExporter
{
    Task<string> ExportBillToExcelAsync(CustomerBill bill, string? destinationDirectory = null, CancellationToken cancellationToken = default);
}

