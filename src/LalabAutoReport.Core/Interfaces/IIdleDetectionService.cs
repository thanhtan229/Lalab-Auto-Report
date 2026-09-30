using System;

namespace LalabAutoReport.Core.Interfaces;

/// <summary>
/// Service to measure user idle time (duration since last keyboard, mouse, or touch input).
/// </summary>
public interface IIdleDetectionService
{
    TimeSpan GetIdleTime();
}
