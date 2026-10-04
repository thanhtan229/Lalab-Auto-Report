using Dapper;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.Infrastructure.Data;

internal sealed class SqliteRemotePrintLedger(ISqliteConnectionFactory factory)
{
    public async Task<string?> ReviewWarningAsync(string endpoint, string stream)
    {
        using var c = factory.CreateConnection();
        var ids = (await c.QueryAsync<long>(@"SELECT command_id FROM cloud_print_command_ledger
            WHERE endpoint=@endpoint AND stream_id=@stream AND status='FAILED'
            AND error_message LIKE 'Kết quả%' ORDER BY command_id", new { endpoint, stream })).ToList();
        var notices = new List<string>();
        if (ids.Count > 0) notices.Add($"Cần kiểm tra kết quả in các lệnh #{string.Join(", #", ids)} trước khi tạo lệnh mới; không tự in lại.");
        var pending = (await c.QueryAsync<long>(@"SELECT command_id FROM cloud_print_command_ledger
            WHERE endpoint=@endpoint AND stream_id=@stream AND acknowledged=0 AND status<>'STARTED' ORDER BY command_id", new { endpoint, stream })).ToList();
        if (pending.Count > 0) notices.Add($"Lệnh in #{string.Join(", #", pending)}: chờ Cloud xác nhận; không tự in lại.");
        var delivery = (await c.QueryAsync<long>(@"SELECT command_id FROM cloud_print_command_ledger
            WHERE endpoint=@endpoint AND stream_id=@stream AND error_message LIKE 'Tem đã in%' ORDER BY command_id", new { endpoint, stream })).ToList();
        if (delivery.Count > 0) notices.Add($"Tem #{string.Join(", #", delivery)} đã in; cần kiểm tra và cập nhật trạng thái giao hàng thủ công.");
        return notices.Count == 0 ? null : string.Join(" · ", notices);
    }

    internal sealed class Entry
    {
        public long CommandId { get; set; }
        public string ClaimToken { get; set; } = "";
        public string Status { get; set; } = "";
        public string? ErrorMessage { get; set; }
    }

    public async Task<IReadOnlyList<Entry>> PendingAsync(string endpoint, string stream)
    {
        using var c = factory.CreateConnection();
        return (await c.QueryAsync<Entry>(@"SELECT command_id CommandId,claim_token ClaimToken,status Status,error_message ErrorMessage
            FROM cloud_print_command_ledger WHERE endpoint=@endpoint AND stream_id=@stream AND acknowledged=0", new { endpoint, stream })).ToList();
    }

    public async Task<bool> StartAsync(string endpoint, string stream, long id, string token)
    {
        using var c = factory.CreateConnection();
        return await c.ExecuteAsync(@"INSERT OR IGNORE INTO cloud_print_command_ledger(endpoint,stream_id,command_id,claim_token,status)
            VALUES(@endpoint,@stream,@id,@token,'STARTED')", new { endpoint, stream, id, token }) == 1;
    }

    public async Task FinishAsync(string endpoint, string stream, long id, string status, string? error)
    {
        using var c = factory.CreateConnection();
        await c.ExecuteAsync(@"UPDATE cloud_print_command_ledger SET status=@status,error_message=@error
            WHERE endpoint=@endpoint AND stream_id=@stream AND command_id=@id", new { endpoint, stream, id, status, error });
    }

    public async Task AcknowledgeAsync(string endpoint, string stream, long id)
    {
        using var c = factory.CreateConnection();
        await c.ExecuteAsync(@"UPDATE cloud_print_command_ledger SET acknowledged=1
            WHERE endpoint=@endpoint AND stream_id=@stream AND command_id=@id", new { endpoint, stream, id });
    }
}
