using System;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;

namespace LalabAutoReport.Core.Interfaces;

public interface IUpdateService
{
    Task<UpdateInfo> CheckForUpdateAsync(CancellationToken cancellationToken = default);
    Task DownloadAndApplyUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
