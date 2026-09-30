using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.Core.Interfaces;

/// <summary>
/// Repository interface for persisting folder printed status records in SQLite
/// </summary>
public interface IFolderPrintRepository
{
    Task<FolderPrintRecord?> GetByNormalizedPathAsync(string normalizedPath, CancellationToken cancellationToken = default);
    Task<FolderPrintRecord?> GetByFolderPathAsync(string folderPath, CancellationToken cancellationToken = default);
    Task SaveAsync(FolderPrintRecord record, CancellationToken cancellationToken = default);
    Task DeleteAsync(string normalizedPath, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetAllPrintedNormalizedPathsAsync(CancellationToken cancellationToken = default);
    Task<int> GetPrintedCountAsync(CancellationToken cancellationToken = default);
}
