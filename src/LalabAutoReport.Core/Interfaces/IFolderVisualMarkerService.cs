using System;
using System.Threading.Tasks;

namespace LalabAutoReport.Core.Interfaces;

/// <summary>
/// Service managing visual appearance of folders in Windows File Explorer (desktop.ini, folder attributes, and icon refresh)
/// </summary>
public interface IFolderVisualMarkerService
{
    /// <summary>
    /// Checks whether the folder currently has the red printed folder visual marker applied
    /// </summary>
    bool HasVisualMarker(string folderPath);

    /// <summary>
    /// Applies the red folder icon marker to the specified folder
    /// </summary>
    bool ApplyVisualMarker(string folderPath);

    /// <summary>
    /// Removes the red folder icon marker from the specified folder and restores standard appearance
    /// </summary>
    bool RemoveVisualMarker(string folderPath);

    /// <summary>
    /// Requests Windows Explorer to refresh its icon view for the specified folder
    /// </summary>
    void RefreshExplorer(string folderPath);

    /// <summary>
    /// Gets the absolute path to the shared printed folder icon (.ico)
    /// </summary>
    string GetOrExtractPrintedFolderIconPath();
}
