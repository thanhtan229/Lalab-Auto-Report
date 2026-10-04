using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using Dapper;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.Services;
using Xunit;

namespace LalabAutoReport.Tests;

public sealed class DurableCloudSyncTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LalabDurableSync_" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteCloudSyncStateRepository _state;
    private readonly SqliteSettingsRepository _settings;
    private readonly SqliteOrderRepository _orders;
    private readonly SqliteCustomerBillRepository _bills;
    public DurableCloudSyncTests()
    {
        Directory.CreateDirectory(_directory); _factory = new(Path.Combine(_directory, "test.db"));
        new DatabaseMigrator(_factory).MigrateAsync().GetAwaiter().GetResult();
        _state = new(_factory); _settings = new(_factory); _orders = new(_factory); _bills = new(_factory);
        using var connection = _factory.CreateConnection();
        connection.Execute("INSERT INTO orders(id, work_date, original_folder_name, relative_path, status, created_at, updated_at) VALUES (1, '2026-10-02','C','2026-10-02/C','Ready','2026-10-02T00:00:00Z','2026-10-02T00:00:00Z');");
        connection.Execute("INSERT INTO customer_bills(id,bill_number,bill_type,customer_name_snapshot,period_start,period_end,status,product_subtotal,grand_total,created_at,updated_at) VALUES(1,'B1','Guest','C','2026-10-02','2026-10-02','Locked',75000,75000,'2026-10-02T00:00:00Z','2026-10-02T00:00:00Z');");
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(_directory, true); }
    private static CloudOperationalEvent Note(long id, string text) => new(id, "order_note", 1, JsonSerializer.Serialize(new { note = text }), "2026-10-02T00:00:00Z");
    private async Task EnableAsync()
    {
        var settings = await _settings.GetSettingsAsync(); settings.EnableCloudSync = true; settings.CloudSyncApiUrl = "https://fixture"; settings.CloudSyncSecret = "fixture-secret";
        await _settings.SaveSettingsAsync(settings);
    }
    private CloudSyncService Service(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
    {
        var customers = new SqliteCustomerRepository(_factory); var products = new SqliteProductRepository(_factory); var legacyBills = new SqliteBillRepository(_factory);
        var billing = new BillingService(_orders, legacyBills, products, customers);
        return new(_settings, _orders, _bills, new ReportService(_orders, legacyBills, billing, products, customers), new HttpClient(new Handler(send)), syncState: _state, enablePeriodicSync: false);
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), Encoding.UTF8, "application/json") };
    private static object Page(long cursor, CloudOperationalEvent[] events, bool more = false) => new { success = true, protocolVersion = 2, streamId = "fixture-stream", events, nextCursor = events.LastOrDefault()?.Id ?? cursor, hasMore = more };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }

    [Fact]
    public async Task FailedWrite_RollsBackStateLedgerAndCursor_ThenRestartRetries()
    {
        using (var connection = _factory.CreateConnection()) connection.Execute("CREATE TRIGGER fail_note BEFORE UPDATE OF note ON orders BEGIN SELECT RAISE(ABORT,'injected'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => _state.ApplyEventAsync(Note(1, "new"), true));
        Assert.Equal(0, (await _state.GetCheckpointAsync()).Cursor);
        using (var connection = _factory.CreateConnection()) { Assert.Equal(0, connection.ExecuteScalar<long>("SELECT count(*) FROM cloud_applied_events")); connection.Execute("DROP TRIGGER fail_note"); }
        var restarted = new SqliteCloudSyncStateRepository(_factory);
        Assert.True(await restarted.ApplyEventAsync(Note(1, "new"), true));
        Assert.False(await restarted.ApplyEventAsync(Note(1, "new"), true));
        Assert.Equal("new", (await _orders.GetOrderByIdAsync(1))!.Note);
        Assert.Equal(1, (await restarted.GetCheckpointAsync()).Cursor);
    }
    [Fact]
    public async Task OlderRevisionAndClockSkew_CannotOverwriteConfirmedFieldOrBillSnapshot()
    {
        await _state.ApplyEventAsync(Note(10, "newer"), false); await _state.ApplyEventAsync(Note(2, "older") with { CreatedAt = "2099-01-01" }, true);
        Assert.Equal("newer", (await _orders.GetOrderByIdAsync(1))!.Note);
        await _state.ApplyEventAsync(new(11,"bill_payment",1,"{\"isPaid\":true,\"paidAt\":\"2026-10-02T00:00:00Z\"}","same"), true);
        var bill = (await _bills.GetByIdAsync(1))!; Assert.True(bill.IsPaid); Assert.Equal(75000, bill.GrandTotal); Assert.Equal(LalabAutoReport.Core.Domain.CustomerBillStatus.Locked, bill.Status);
    }
    [Fact]
    public async Task UnknownMalformedOrMissingEntity_KeepCheckpoint()
    {
        foreach (var ev in new[] { Note(1,"a") with { EntityType = "future_type" }, Note(1,"a") with { PayloadJson = "{}" }, Note(1,"a") with { EntityId = 999 } })
            await Assert.ThrowsAnyAsync<Exception>(() => _state.ApplyEventAsync(ev, true));
        Assert.Equal(0, (await _state.GetCheckpointAsync()).Cursor);
    }
    [Fact]
    public async Task Pull501Events_CommitsEveryIdAndDoesNotOverwriteSettings()
    {
        await EnableAsync(); int pages = 0;
        using var service = Service(async request => {
            pages++; long cursor = long.Parse(request.RequestUri!.Query.Split('=')[1]);
            var settings = await _settings.GetSettingsAsync(); settings.WorkshopName = "changed-during-pull"; await _settings.SaveSettingsAsync(settings);
            return Json(Page(cursor, Enumerable.Range((int)cursor + 1, cursor == 0 ? 500 : 1).Select(i => Note(i, i.ToString())).ToArray(), cursor == 0));
        });
        Assert.Equal(501, await service.PullRemoteChangesAsync()); Assert.Equal(2, pages);
        Assert.Equal(501, (await _state.GetCheckpointAsync()).Cursor); Assert.Equal("501", (await _orders.GetOrderByIdAsync(1))!.Note);
        Assert.Equal("changed-during-pull", (await _settings.GetSettingsAsync()).WorkshopName);
    }
    [Fact]
    public async Task FailedPull_BlocksProjectionAndRetainsPendingOperation()
    {
        await EnableAsync(); await _orders.UpdateOrderNoteAsync(1, "offline"); int pushes = 0;
        using var service = Service(request => { if (request.Method == HttpMethod.Post) pushes++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); });
        Assert.False((await service.SyncAllAsync()).Success); Assert.Equal(0, pushes); Assert.Single(await _state.GetPendingAsync());
    }
    [Fact]
    public async Task LocalEditAndOutbox_AreAtomic_AndPendingSurvivesRemoteEvents()
    {
        await EnableAsync();
        using (var connection = _factory.CreateConnection()) connection.Execute("CREATE TRIGGER fail_outbox BEFORE INSERT ON cloud_operational_outbox BEGIN SELECT RAISE(ABORT,'queue failed'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => _orders.UpdateOrderNoteAsync(1,"lost"));
        Assert.Null((await _orders.GetOrderByIdAsync(1))!.Note);
        using (var connection = _factory.CreateConnection()) connection.Execute("DROP TRIGGER fail_outbox");
        await _orders.UpdateOrderNoteAsync(1, "offline"); var operation = Assert.Single(await _state.GetPendingAsync());
        await _state.ApplyEventAsync(Note(2,"mobile"), true); Assert.Equal("offline", (await _orders.GetOrderByIdAsync(1))!.Note);
        await _state.ApplyEventAsync(Note(3,"offline") with { OperationId = operation.OperationId }, false);
        Assert.Empty(await _state.GetPendingAsync()); Assert.Equal(2, (await _state.GetCheckpointAsync()).Cursor);
        await _state.ApplyEventAsync(Note(3,"offline") with { OperationId = operation.OperationId }, true);
        Assert.Equal(3, (await _state.GetCheckpointAsync()).Cursor);
    }
    [Fact]
    public async Task LostAck_RetriesSameOperationIdAfterRestart_AndConfirmsOnlyOnce()
    {
        await EnableAsync(); await _bills.SetPaymentStatusAsync(1, true);
        string operationId = Assert.Single(await _state.GetPendingAsync()).OperationId; int operations = 0; bool loseFirst = true;
        CloudOperationalEvent? accepted = null;
        async Task<HttpResponseMessage> Send(HttpRequestMessage request) {
            if (request.RequestUri!.AbsolutePath.EndsWith("/pull")) { long cursor = long.Parse(request.RequestUri.Query.Split('=')[1]); return Json(Page(cursor, accepted != null && accepted.Id > cursor ? [accepted] : [])); }
            if (request.RequestUri.AbsolutePath.EndsWith("/operations")) {
                operations++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); Assert.Equal(operationId, body.RootElement.GetProperty("operationId").GetString());
                accepted = new(1,"bill_payment",1,"{\"isPaid\":true,\"paidAt\":\"2026-10-02T00:00:00Z\"}","same",operationId);
                if (loseFirst) { loseFirst = false; throw new HttpRequestException("lost response"); }
                return Json(new { success = true, protocolVersion = 2, @event = accepted });
            }
            return Json(new { success = true });
        }
        using (var service = Service(Send)) Assert.False((await service.SyncAllAsync()).Success);
        Assert.Single(await _state.GetPendingAsync());
        using (var service = Service(Send)) { var result = await service.SyncAllAsync(); Assert.True(result.Success, result.Error ?? result.Message); }
        Assert.Equal(2, operations); Assert.Empty(await _state.GetPendingAsync()); Assert.True((await _bills.GetByIdAsync(1))!.IsPaid);
    }
    [Fact]
    public async Task ConcurrentManualPulls_ShareSingleGate()
    {
        await EnableAsync(); int active = 0, maximum = 0;
        using var service = Service(async request => { int current = Interlocked.Increment(ref active); maximum = Math.Max(maximum, current); await Task.Delay(20); Interlocked.Decrement(ref active); return Json(Page(0, [])); });
        await Task.WhenAll(Enumerable.Range(0,8).Select(_ => service.PullRemoteChangesAsync())); Assert.Equal(1, maximum);
    }
    [Fact]
    public async Task MigrationFromLegacyCursor_RequiresReadOnlyReconciliation_RepeatPreservesHistory()
    {
        var factory = new SqliteConnectionFactory(Path.Combine(_directory,"upgrade.db")); var migrator = new DatabaseMigrator(factory);
        await migrator.MigrateToVersionAsync(20);
        using (var connection = factory.CreateConnection()) connection.Execute("INSERT INTO app_settings(key,value) VALUES ('LastCloudPullAt','2026-10-02T00:00:00Z')");
        await migrator.MigrateAsync(); await migrator.MigrateAsync(); var state = new SqliteCloudSyncStateRepository(factory);
        Assert.True((await state.GetCheckpointAsync()).RequiresReconciliation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.BindStreamAsync("stream","endpoint"));
        using var check = factory.CreateConnection(); Assert.Empty(await check.QueryAsync("PRAGMA foreign_key_check")); Assert.Equal("ok", await check.QuerySingleAsync<string>("PRAGMA integrity_check"));
    }

    private async Task<string> ReportAsync() => await _state.CreateReconciliationReportAsync("https://fixture", JsonSerializer.Serialize(new {
        success = true, protocolVersion = 2, streamId = "fixture-stream", cursor = 10,
        fields = new[] { new { entityType = "order_note", entityId = 1, revision = 9, payloadJson = "{\"note\":\"server-note\"}" },
            new { entityType = "bill_payment", entityId = 1, revision = 10, payloadJson = "{\"isPaid\":true,\"paidAt\":\"2026-10-02T00:00:00Z\"}" } }
    }));
    private static string Review(string json, string noteChoice = "server", string paymentChoice = "server") {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var report = JsonSerializer.Deserialize<CloudReconciliationReport>(json, options)!;
        return JsonSerializer.Serialize(report with { Fields = report.Fields.Select(f => f with { Resolution = f.EntityType == "order_note" ? noteChoice : paymentChoice }).ToList() }, options);
    }
    [Fact]
    public async Task Reconciliation_IsReadOnlyUntilEveryFieldReviewed_AndDoesNotModifyLockedMoney()
    {
        await EnableAsync(); await _orders.UpdateOrderNoteAsync(1,"desktop-note");
        string report = await ReportAsync(); Assert.Equal("desktop-note", (await _orders.GetOrderByIdAsync(1))!.Note); Assert.False((await _bills.GetByIdAsync(1))!.IsPaid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _state.CompleteReconciliationAsync(report));
        await _state.CompleteReconciliationAsync(Review(report, "desktop", "server"));
        Assert.Equal("desktop-note", (await _orders.GetOrderByIdAsync(1))!.Note); Assert.True((await _bills.GetByIdAsync(1))!.IsPaid);
        Assert.Equal(75000, (await _bills.GetByIdAsync(1))!.GrandTotal); Assert.Single(await _state.GetPendingAsync()); Assert.Equal(10,(await _state.GetCheckpointAsync()).Cursor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _state.CompleteReconciliationAsync(Review(report)));
    }
    [Fact]
    public async Task Reconciliation_RejectsTamperingAndStaleLocalData()
    {
        await EnableAsync(); string report = await ReportAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _state.CompleteReconciliationAsync(Review(report).Replace("server-note","tampered")));
        await _orders.UpdateOrderNoteAsync(1,"changed-after-export");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _state.CompleteReconciliationAsync(Review(report)));
        Assert.Equal(0,(await _state.GetCheckpointAsync()).Cursor); Assert.Single(await _state.GetPendingAsync());
    }
    [Fact]
    public async Task ReconciliationFailure_RollsBackAllFieldsAndActivation()
    {
        string report = await ReportAsync();
        using (var connection = _factory.CreateConnection()) connection.Execute("CREATE TRIGGER fail_payment BEFORE UPDATE OF is_paid ON customer_bills BEGIN SELECT RAISE(ABORT,'injected'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => _state.CompleteReconciliationAsync(Review(report)));
        Assert.Null((await _orders.GetOrderByIdAsync(1))!.Note); Assert.False((await _bills.GetByIdAsync(1))!.IsPaid); Assert.Equal(0,(await _state.GetCheckpointAsync()).Cursor);
        using (var connection = _factory.CreateConnection()) connection.Execute("DROP TRIGGER fail_payment");
        await _state.CompleteReconciliationAsync(Review(report)); Assert.Equal("server-note",(await _orders.GetOrderByIdAsync(1))!.Note);
    }
    [Fact]
    public async Task MalformedPageOrChangedStream_BlocksPushWithoutCursorAdvance()
    {
        await EnableAsync(); int posts = 0;
        using (var service = Service(request => { if(request.Method == HttpMethod.Post) posts++; return Task.FromResult(Json(new {success=true, protocolVersion=2, streamId="fixture-stream", events=new[]{Note(1,"a")}, nextCursor=500, hasMore=false})); }))
            Assert.False((await service.SyncAllAsync()).Success);
        Assert.Equal(0,posts); Assert.Equal(0,(await _state.GetCheckpointAsync()).Cursor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _state.BindStreamAsync("changed-stream","https://fixture"));
    }
}
