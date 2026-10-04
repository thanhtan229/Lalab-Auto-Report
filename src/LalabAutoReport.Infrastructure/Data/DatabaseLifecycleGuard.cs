using System.Collections.Concurrent;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

namespace LalabAutoReport.Infrastructure.Data;

// A reset/restore cannot overlap an in-flight HTTP sync using the same database.
internal static class DatabaseLifecycleGuard
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<IDisposable> EnterAsync(string path, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Lease(gate);
    }

    public static async Task EnsureLocalOnlyAsync(SqliteConnection connection)
    {
        var tables = (await connection.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type='table'")).ToHashSet();
        bool bound = false;
        if (tables.Contains("app_settings"))
            // EnableCloudSync defaults to true even before credentials are configured.
            // The toggle alone is not evidence of an existing remote identity.
            bound = await connection.ExecuteScalarAsync<int>(@"SELECT count(*) FROM app_settings WHERE
                key IN ('LastCloudSyncAt','LastCloudPullAt','CloudSyncSecret') AND length(trim(value))>0") > 0;
        if (tables.Contains("cloud_sync_state"))
            bound |= await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM cloud_sync_state WHERE stream_id IS NOT NULL OR endpoint IS NOT NULL OR cursor>0 OR requires_reconciliation<>0") > 0;
        foreach (var table in new[] { "cloud_operational_outbox", "cloud_field_revisions", "cloud_applied_events", "cloud_applied_event_archive", "cloud_sync_queue", "cloud_print_command_ledger" })
            if (tables.Contains(table)) bound |= await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}") > 0;
        if (bound)
            throw new InvalidOperationException("Không thể xóa/khôi phục database đã kết nối Cloud hoặc còn thao tác Cloud. Tắt đồng bộ không xóa liên kết cũ. Cần quy trình lưu trữ và đối soát hai phía trước khi thay dữ liệu; dữ liệu hiện tại được giữ nguyên.");
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
