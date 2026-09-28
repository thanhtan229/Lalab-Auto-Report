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
