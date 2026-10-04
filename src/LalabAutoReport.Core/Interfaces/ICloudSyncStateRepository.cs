namespace LalabAutoReport.Core.Interfaces;

public sealed record CloudOperationalEvent(long Id, string EntityType, long EntityId, string PayloadJson, string CreatedAt, string? OperationId = null);
public sealed record CloudPendingOperation(long Sequence, string OperationId, string EntityType, long EntityId, string ValueJson, string? DeliveredBy);
public sealed record CloudSyncCheckpoint(long Cursor, bool RequiresReconciliation, string? StreamId, string? Endpoint);

public interface ICloudSyncStateRepository
{
    Task<CloudSyncCheckpoint> GetCheckpointAsync(CancellationToken cancellationToken = default);
    Task BindStreamAsync(string streamId, string endpoint, CancellationToken cancellationToken = default);
    Task<bool> ApplyEventAsync(CloudOperationalEvent ev, bool advanceCursor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudPendingOperation>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<object>> GetReconciliationInventoryAsync(CancellationToken cancellationToken = default);
    Task SetLastSyncAtAsync(string value, CancellationToken cancellationToken = default);
    Task<string> CreateReconciliationReportAsync(string endpoint, string snapshotJson, CancellationToken cancellationToken = default);
    Task CompleteReconciliationAsync(string reviewedReportJson, CancellationToken cancellationToken = default);
}

public sealed record CloudReconciliationField(string EntityType, long EntityId, long Revision, string PayloadJson, string? LocalValueJson, string Resolution);
public sealed record CloudReconciliationReport(string ReportId, string Endpoint, string StreamId, long Cursor, string LocalHash, List<CloudReconciliationField> Fields);
