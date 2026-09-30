using System;

namespace LalabAutoReport.Core.Interfaces;

/// <summary>
/// Service to register and unregister Windows Explorer right-click context menu integrations.
/// </summary>
public interface IContextMenuIntegrationService
{
    /// <summary>
    /// Checks whether the QUICK BILL context menu items are currently registered in HKCU registry.
    /// </summary>
    bool IsContextMenuRegistered();

    /// <summary>
    /// Registers the QUICK BILL context menu for Directory and Directory Background in HKCU.
    /// </summary>
    void RegisterContextMenu();

    /// <summary>
    /// Unregisters the QUICK BILL context menu from HKCU.
    /// </summary>
    void UnregisterContextMenu();

    /// <summary>
    /// Ensures that the context menu is registered and pointing to the current executable.
    /// </summary>
    void EnsureContextMenuRegistered();

    /// <summary>
    /// Generates content for a standalone .reg file that can be distributed to other PCs.
    /// </summary>
    string GenerateRegFileContent();
}
