namespace LalabAutoReport.Core.Domain;

/// <summary>
/// Status of an Order in the workflow
/// </summary>
public enum OrderStatus
{
    Unscanned = 0,
    Scanning = 1,
    Scanned = 2,
    NeedsReview = 3,
    Ready = 4,
    Billed = 5,
    Locked = 6,
    Error = 7
}

/// <summary>
/// Status of resolving the print folder candidate in a print specification folder
/// </summary>
public enum PrintFolderResolutionStatus
{
    /// <summary>
    /// Exactly one deepest valid image-bearing leaf folder was found
    /// </summary>
    Resolved = 0,

    /// <summary>
    /// No descendant image-bearing folder exists
    /// </summary>
    NoPrintFolder = 1,

    /// <summary>
    /// Multiple competing image-bearing leaf folders exist
    /// </summary>
    AmbiguousPrintFolder = 2,

    /// <summary>
    /// Direct source printing candidate if permitted
    /// </summary>
    SourceIsPrintCandidate = 3
}

/// <summary>
/// Resolution mode for Effective Billing Folder (Auto vs Manual)
/// </summary>
public enum BillingFolderResolutionMode
{
    AutoResolved = 0,
    ManuallySelected = 1
}

/// <summary>
/// Status of resolving a customer from folder name
/// </summary>
public enum CustomerResolutionStatus
{
    ExactMatch = 0,
    NormalizedMatch = 1,
    FuzzySuggested = 2,
    AmbiguousCollision = 3,
    Unresolved = 4
}

/// <summary>
/// Status of resolving a print specification from folder name
/// </summary>
public enum PrintSpecificationResolutionStatus
{
    Resolved = 0,
    Unknown = 1,
    AmbiguousCollision = 2
}

/// <summary>
/// Mode used to resolve billed quantity when Source != Print
/// </summary>
public enum QuantityResolutionMode
{
    AutoMatch = 0,
    UsePrint = 1,
    UseSource = 2,
    Custom = 3
}

/// <summary>
/// Scopes supported by manual Smart Scan
/// </summary>
public enum ScanScope
{
    Date = 0,
    DateRange = 1,
    MissingDays = 2,
    Order = 3,
    Specification = 4,
    VerifyBeforeLock = 5
}

/// <summary>
/// General scan status
/// </summary>
public enum ScanStatus
{
    Pending = 0,
    Success = 1,
    Warning = 2,
    Failed = 3
}

/// <summary>
/// Order kind: Implicit (legacy customer->product) or Explicit (customer->order->product)
/// </summary>
public enum OrderKind
{
    Implicit = 0,
    Explicit = 1
}

/// <summary>
/// Product category in V2
/// </summary>
public enum ProductCategory
{
    PhotoPrint = 0,
    Album = 1,
    Frame = 2,
    Canvas = 3,
    Photobook = 4,
    Lamination = 5,
    WoodMount = 6,
    Other = 7
}

/// <summary>
/// Billing strategy method
/// </summary>
public enum BillingMethod
{
    FileCount = 0,
    AlbumBasePlusExtra = 1,
    Manual = 2
}

/// <summary>
/// Scan issue types for V2 validation
/// </summary>
public enum ScanIssueType
{
    None = 0,
    UnresolvedCustomer = 1,
    AmbiguousCustomerAlias = 2,
    UnresolvedStructure = 3,
    UnresolvedProduct = 4,
    AmbiguousProductAlias = 5,
    NoPrintableFiles = 6,
    MultipleFinalPrintFolderCandidates = 7,
    MissingFinalPrintFolder = 8,
    AlbumBelowIncludedSheets = 9,
    FilesystemAccessError = 10
}

/// <summary>
/// Status of a customer bill in the customer billing workflow
/// </summary>
public enum CustomerBillStatus
{
    Draft = 0,
    Locked = 1,
    Exported = 2
}

/// <summary>
/// Type of bill adjustment
/// </summary>
public enum AdjustmentType
{
    Shipping = 0,
    Surcharge = 1,
    Discount = 2,
    Custom = 3
}

/// <summary>
/// Direction of adjustment: Add (+) or Deduct (-)
/// </summary>
public enum AdjustmentDirection
{
    Add = 0,
    Deduct = 1
}

/// <summary>
/// Type of bill: Customer-centric or Guest (Quick Bill)
/// </summary>
public enum BillType
{
    Customer = 0,
    Guest = 1
}

/// <summary>
/// Status of printing in the photo workshop
/// </summary>
public enum PrintStatus
{
    NotPrinted = 0,
    Printed = 1
}

/// <summary>
/// Scope of database reset in danger zone
/// </summary>
public enum ResetDataScope
{
    /// <summary>
    /// Deletes orders, scans, customer bills, adjustments, and print statuses.
    /// Preserves customer master list, customer aliases, and product price configurations.
    /// </summary>
    OperationalOnly = 0,

    /// <summary>
    /// Resets entire database to fresh factory state, re-applying initial migration and seed data.
    /// </summary>
    FactoryReset = 1
}
