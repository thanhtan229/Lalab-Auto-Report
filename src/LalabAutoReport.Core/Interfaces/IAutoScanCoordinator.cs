using System;
using System.Threading;
using System.Threading.Tasks;

namespace LalabAutoReport.Core.Interfaces;

/// <summary>
/// Coordinates background smart scanning based on user idle time, application startup, and contextual triggers.
/// </summary>
public interface IAutoScanCoordinator : IDisposable
{
    /// <summary>
    /// Event raised when an auto-scan status message changes (e.g. idle scan finished, update count).
    /// </summary>
    event Action<string>? StatusChanged;

    /// <summary>
    /// Event raised when an auto-scan successfully updates one or more orders.
    /// </summary>
    event Action<int>? OrdersUpdated;

    /// <summary>
    /// Starts background idle monitoring.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops background idle monitoring.
    /// </summary>
    void Stop();

    /// <summary>
    /// Temporarily pauses or resumes auto-scanning (e.g. while reviewing a bill modal).
    /// </summary>
    void SetPaused(bool isPaused);

    /// <summary>
    /// Performs the startup check (Today + Yesterday if enabled in settings).
    /// </summary>
    Task<int> PerformStartupScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an idle scan check cycle (scans unbilled orders in the active window using fingerprint check).
    /// </summary>
    Task<int> PerformIdleScanAsync(CancellationToken cancellationToken = default);
}
