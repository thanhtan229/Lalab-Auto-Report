using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using Dapper;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.Services;

namespace LalabAutoReport.Tests;

public partial class CloudSyncServiceTests
{
    private const string PrintEndpoint = "https://print.test";
    private async Task PreparePrintAsync()
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        settings.EnableCloudSync = true;
        settings.CloudSyncApiUrl = PrintEndpoint;
        settings.CloudSyncSecret = "synthetic-secret";
        await _settingsRepo.SaveSettingsAsync(settings);
        await new SqliteCloudSyncStateRepository(_connectionFactory).BindStreamAsync("stream", PrintEndpoint);
        using var c = _connectionFactory.CreateConnection();
        await c.ExecuteAsync("INSERT INTO orders(id,work_date,original_folder_name,relative_path,status,created_at,updated_at) VALUES(1,'2026-10-02','Fixture','Fixture','Scanned',datetime('now'),datetime('now'))");
    }

    private sealed class PrintRecoveryHttp(bool lostResponse = false) : HttpMessageHandler
    {
        public int AckAttempts;
        public int AckFailures = 2;
        public int Claims;
        public bool Conflict;
        public string? AckBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/status"))
            {
                AckAttempts++;
                AckBody = await request.Content!.ReadAsStringAsync(ct);
                if (AckFailures-- > 0)
                {
                    if (lostResponse) throw new HttpRequestException("Lost ACK response");
                    return new(HttpStatusCode.InternalServerError);
                }
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/claim"))
            {
                Claims++;
                if (Conflict) return new(HttpStatusCode.Conflict);
            }
            else return new(HttpStatusCode.OK) { Content = new StringContent("{\"commands\":[{\"id\":88,\"orderId\":1,\"commandType\":\"print_order_label\"}]}") };
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true}") };
        }
    }

    private CloudSyncService PrintService(PrintRecoveryHttp handler, TestShippingLabelExporter exporter) =>
        new(_settingsRepo, _orderRepo, _customerBillRepo, _reportService, new HttpClient(handler, false),
            shippingLabelExporter: exporter, enablePeriodicSync: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemotePrint_LostAckOrHttp500_RestartRetriesAckWithoutPrinting(bool lostResponse)
    {
        await PreparePrintAsync();
        var handler = new PrintRecoveryHttp(lostResponse);
        var exporter = new TestShippingLabelExporter();
        using (var first = PrintService(handler, exporter))
        {
            Assert.Equal(1, await first.ProcessPendingPrintCommandsAsync());
            Assert.Contains("chờ Cloud", first.LastSyncStatus);
        }
        // Recreate the service, retaining only the real SQLite ledger.
        using var restarted = PrintService(handler, exporter);
        Assert.Equal(0, await restarted.ProcessPendingPrintCommandsAsync());
        Assert.Equal(0, await restarted.ProcessPendingPrintCommandsAsync());
        Assert.Single(exporter.PrintedOrders);
        Assert.Equal(1, handler.Claims);
        Assert.Equal(3, handler.AckAttempts);
        using var c = _connectionFactory.CreateConnection();
        Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT count(*) FROM cloud_print_command_ledger WHERE status='COMPLETED' AND acknowledged=1"));
    }

    [Fact]
    public async Task RemotePrint_TwoServicesShareDatabase_OnlyOneSpoolsCommand()
    {
        await PreparePrintAsync();
        var handler = new PrintRecoveryHttp { AckFailures = 0 };
        var exporter = new TestShippingLabelExporter();
        using var first = PrintService(handler, exporter);
        using var second = PrintService(handler, exporter);
        await Task.WhenAll(first.ProcessPendingPrintCommandsAsync(), second.ProcessPendingPrintCommandsAsync());
        Assert.Single(exporter.PrintedOrders);
        Assert.Equal(1, handler.Claims);
    }

    [Fact]
    public async Task RemotePrint_CrashWithStartedLedger_ReportsUncertainWithoutSpooling()
    {
        await PreparePrintAsync();
        using (var c = _connectionFactory.CreateConnection())
            await c.ExecuteAsync("INSERT INTO cloud_print_command_ledger(endpoint,stream_id,command_id,claim_token,status) VALUES(@PrintEndpoint,'stream',88,'durable-existing-token','STARTED')", new { PrintEndpoint });
        var handler = new PrintRecoveryHttp { AckFailures = 0 };
        var exporter = new TestShippingLabelExporter();
        using var service = PrintService(handler, exporter);
        Assert.Equal(0, await service.ProcessPendingPrintCommandsAsync());
        Assert.Empty(exporter.PrintedOrders);
        Assert.Equal(0, handler.Claims);
        using var body = JsonDocument.Parse(handler.AckBody!);
        Assert.Equal("FAILED", body.RootElement.GetProperty("status").GetString());
        Assert.Contains("Kiểm tra", body.RootElement.GetProperty("errorMessage").GetString());
        Assert.Contains("#88", service.LastSyncStatus);
        using var restarted = PrintService(handler, exporter);
        await restarted.ProcessPendingPrintCommandsAsync();
        Assert.Contains("#88", restarted.LastSyncStatus);
        Assert.Empty(exporter.PrintedOrders);
    }

    [Fact]
    public async Task RemotePrint_ClaimOwnedElsewhere_DoesNotSpoolOrAcknowledgeOtherOwner()
    {
        await PreparePrintAsync();
        var handler = new PrintRecoveryHttp { Conflict = true };
        var exporter = new TestShippingLabelExporter();
        using var service = PrintService(handler, exporter);
        Assert.Equal(0, await service.ProcessPendingPrintCommandsAsync());
        Assert.Empty(exporter.PrintedOrders);
        Assert.Equal(0, handler.AckAttempts);
    }

    [Fact]
    public async Task RemotePrint_DeliveryWriteFailsAfterSpooling_PersistsCompletionWithoutReprinting()
    {
        await PreparePrintAsync();
        using (var c = _connectionFactory.CreateConnection())
            await c.ExecuteAsync("CREATE TRIGGER fail_delivery BEFORE UPDATE OF is_delivered ON orders BEGIN SELECT RAISE(ABORT,'synthetic delivery write failure'); END");
        var handler = new PrintRecoveryHttp { AckFailures = 0 };
        var exporter = new TestShippingLabelExporter();
        using var service = PrintService(handler, exporter);
        Assert.Equal(1, await service.ProcessPendingPrintCommandsAsync());
        Assert.Contains("giao hàng thủ công", service.LastSyncStatus);
        Assert.Equal(0, await service.ProcessPendingPrintCommandsAsync());
        Assert.Single(exporter.PrintedOrders);
        using var check = _connectionFactory.CreateConnection();
        Assert.Equal(1, await check.ExecuteScalarAsync<int>("SELECT count(*) FROM cloud_print_command_ledger WHERE status='COMPLETED' AND acknowledged=1 AND error_message LIKE 'Tem đã in%'"));
    }

    [Fact]
    public void CloudBillProjection_UsesIncludedSnapshotAndAuthoritativeTotals()
    {
        var bill = new CustomerBill { ProductSubtotal = 75000, AdjustmentsTotal = -5000, GrandTotal = 70000,
            Orders = new() { new() { OrderId = 10 }, new() { OrderId = 20, IsIncluded = false } },
            Lines = new() { new() { Id = 1, OrderId = 10, LineTotal = 75000 }, new() { Id = 2, OrderId = 10, IsIncluded = false, LineTotal = 25000 }, new() { Id = 3, OrderId = 20, LineTotal = 90000 } } };
        var mapper = typeof(CloudSyncService).GetMethod("MapBillToCloudDto", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var projected = JsonDocument.Parse(JsonSerializer.Serialize(mapper.Invoke(null, new object[] { bill })));
        var dto = projected.RootElement;
        Assert.Equal(75000, dto.GetProperty("productSubtotal").GetInt64());
        Assert.Equal(-5000, dto.GetProperty("adjustmentsTotal").GetInt64());
        using var lines = JsonDocument.Parse(dto.GetProperty("linesJson").GetString()!);
        Assert.Equal(1, lines.RootElement.GetArrayLength());
        Assert.Equal(1, lines.RootElement[0].GetProperty("id").GetInt64());
        Assert.Equal(3, bill.Lines.Count);
    }

    private sealed class PausedSyncHttp : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Entered.SetResult();
            await Release.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true}") };
        }
    }

    [Fact]
    public async Task Reset_WaitsForInFlightSyncThenRejectsBoundLifecycle()
    {
        await PreparePrintAsync();
        var handler = new PausedSyncHttp();
        using var sync = new CloudSyncService(_settingsRepo, _orderRepo, _customerBillRepo, _reportService,
            new HttpClient(handler), enablePeriodicSync: false);
        var inFlight = sync.SendHeartbeatAsync();
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var reset = new DatabaseResetService(_connectionFactory, new DatabaseBackupService(_connectionFactory), new DatabaseMigrator(_connectionFactory));
        var pendingReset = reset.ResetDataAsync(ResetDataScope.OperationalOnly);
        try { Assert.False(pendingReset.IsCompleted); }
        finally { handler.Release.TrySetResult(); }
        Assert.True(await inFlight);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pendingReset);
        Assert.NotNull(await _orderRepo.GetOrderByIdAsync(1));
    }
}
