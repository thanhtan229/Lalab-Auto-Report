using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.Core.Interfaces;

/// <summary>
/// Core service managing printed workflow status for workshop folders
/// </summary>
public interface IPrintStatusService
{
    /// <summary>
    /// Event fired whenever a folder's print status changes
    /// </summary>
    event Action<string, PrintStatus>? PrintStatusChanged;

    /// <summary>
    /// Gets the current print status for the specified folder (checks DB and marker)
    /// </summary>
    Task<PrintStatus> GetStatusAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the specified folder is currently marked as PRINTED
    /// </summary>
    Task<bool> IsFolderPrintedAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles the printed status of the folder (NotPrinted -> Printed, or Printed -> NotPrinted)
    /// </summary>
    Task<TogglePrintStatusResult> ToggleStatusAsync(string folderPath, string markedBy = "ExplorerContextMenu", CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly sets the printed status of the folder
    /// </summary>
    Task<TogglePrintStatusResult> SetStatusAsync(string folderPath, PrintStatus targetStatus, string markedBy = "ExplorerContextMenu", CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a set of all normalized folder paths currently marked as printed (for efficient batch checks during auto-scan)
    /// </summary>
    Task<IReadOnlySet<string>> GetAllPrintedNormalizedPathsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles state between database and filesystem marker file
    /// </summary>
    Task ReconcileFolderStatusAsync(string folderPath, CancellationToken cancellationToken = default);
}
