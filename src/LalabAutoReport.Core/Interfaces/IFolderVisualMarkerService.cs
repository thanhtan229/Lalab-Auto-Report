using System;
using System.Threading.Tasks;

namespace LalabAutoReport.Core.Interfaces;

public enum VisualFolderColor
{
    DefaultYellow = 0,
    BluePartial = 1,
    RedPrinted = 2
}

/// <summary>
/// Service managing visual appearance of folders in Windows File Explorer (desktop.ini, folder attributes, and icon refresh)
/// </summary>
public interface IFolderVisualMarkerService
{
    /// <summary>
    /// Checks whether the folder currently has any visual marker applied
    /// </summary>
    bool HasVisualMarker(string folderPath);

    /// <summary>
    /// Gets the current visual folder color applied to the folder
    /// </summary>
    VisualFolderColor GetVisualMarkerColor(string folderPath);

    /// <summary>
    /// Applies the custom folder icon marker (Red Printed or Blue Partial) to the specified folder
    /// </summary>
    bool ApplyVisualMarker(string folderPath, VisualFolderColor color = VisualFolderColor.RedPrinted);

    /// <summary>
    /// Removes the custom folder icon marker from the specified folder and restores standard appearance
    /// </summary>
    bool RemoveVisualMarker(string folderPath);

    /// <summary>
    /// Requests Windows Explorer to refresh its icon view for the specified folder
    /// </summary>
    void RefreshExplorer(string folderPath);

    /// <summary>
    /// Gets the absolute path to the shared printed folder icon (Red - .ico)
    /// </summary>
    string GetOrExtractPrintedFolderIconPath();

    /// <summary>
    /// Gets the absolute path to the shared partial printed folder icon (Blue - .ico)
    /// </summary>
    string GetOrExtractPartialFolderIconPath();
}
