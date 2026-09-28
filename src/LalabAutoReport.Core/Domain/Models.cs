using System;
using System.Collections.Generic;

namespace LalabAutoReport.Core.Domain;

public class Customer
{
    public long Id { get; set; }
    public string CanonicalName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CustomerAlias> Aliases { get; set; } = new();
}

public class CustomerAlias
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string AliasText { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PrintSpecification
{
    public long Id { get; set; }
    public string CanonicalName { get; set; } = string.Empty;
    public long UnitPrice { get; set; } // VND as integer
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PrintSpecificationAlias> Aliases { get; set; } = new();
}

public class PrintSpecificationAlias
{
    public long Id { get; set; }
    public long PrintSpecificationId { get; set; }
    public string AliasText { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
}

public class Order
{
    public long Id { get; set; }
    public string WorkDate { get; set; } = string.Empty; // "yyyy-MM-dd"
    public long? CustomerId { get; set; }
    public string OriginalFolderName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty; // e.g. "2026-09-28\Văn An"
    public OrderStatus Status { get; set; } = OrderStatus.Unscanned;
    public bool FilesystemChangedAfterLock { get; set; } = false;
    public DateTimeOffset? LastScanAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Populated during scan or query
    public Customer? Customer { get; set; }
    public List<OrderItemScan> Items { get; set; } = new();
}

public class ScanSnapshot
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public ScanScope Scope { get; set; }
    public ScanStatus Status { get; set; } = ScanStatus.Pending;
    public string? ErrorMessage { get; set; }
    public string AppVersion { get; set; } = "1.0.0";

    public List<OrderItemScan> Items { get; set; } = new();
}

public class OrderItemScan
{
    public long Id { get; set; }
    public long ScanSnapshotId { get; set; }
    public long OrderId { get; set; }
    public long? PrintSpecificationId { get; set; }
    public string SpecificationFolderName { get; set; } = string.Empty;
    public string SpecificationRelativePath { get; set; } = string.Empty;
    public int SourceCount { get; set; }
    public int? PrintCount { get; set; }
    public string? SelectedPrintFolderRelativePath { get; set; }
    public PrintFolderResolutionStatus PrintFolderStatus { get; set; } = PrintFolderResolutionStatus.NoPrintFolder;
    public int? MismatchCount { get; set; }
    public ScanStatus ScanStatus { get; set; } = ScanStatus.Pending;
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Candidate print folders found if ambiguous (joined or parsed)
    /// </summary>
    public List<string> CandidatePrintFolderRelativePaths { get; set; } = new();

    /// <summary>
    /// User resolution if resolved
    /// </summary>
    public int? BillQuantity { get; set; }
    public QuantityResolutionMode? QuantityResolutionMode { get; set; }
    public string? QuantityResolutionNote { get; set; }

    // Navigation / resolved objects
    public PrintSpecification? PrintSpecification { get; set; }
}

public class Bill
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long CustomerId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Billed;
    public long Subtotal { get; set; } // VND integer
    public DateTimeOffset? LockedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<BillLine> Lines { get; set; } = new();
}

public class BillLine
{
    public long Id { get; set; }
    public long BillId { get; set; }
    public long PrintSpecificationId { get; set; }
    public int SourceCount { get; set; }
    public int? PrintCount { get; set; }
    public int BillQuantity { get; set; }
    public QuantityResolutionMode QuantityResolutionMode { get; set; }
    public string? QuantityResolutionNote { get; set; }
    public long UnitPrice { get; set; }
    public long LineTotal { get; set; }
    public long SourceScanSnapshotId { get; set; }

    public PrintSpecification? PrintSpecification { get; set; }
}

public class AppSettings
{
    public string RootFolder { get; set; } = string.Empty;
    public List<string> SupportedExtensions { get; set; } = new()
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp", ".heic"
    };
    public bool AutoScanStartup { get; set; } = false;
    public bool StartupIncludeYesterday { get; set; } = false;
}
