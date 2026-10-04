using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.UI.ViewModels;
using Xunit;

namespace LalabAutoReport.Tests;

public class OrderDeliveryAndNoteTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteOrderRepository _orderRepo;

    public OrderDeliveryAndNoteTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"lalab_order_deliv_test_{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _migrator.MigrateAsync().GetAwaiter().GetResult();
        _orderRepo = new SqliteOrderRepository(_connectionFactory);
    }

    [Fact]
    public async Task SqliteOrderRepository_SaveAndRetrieve_DeliveredAndNote()
    {
        var now = DateTimeOffset.Now;
        var order = new Order
        {
            WorkDate = "2026-10-01",
            OriginalFolderName = "Khach_Test",
            RelativePath = "2026-10-01/Khach_Test",
            Status = OrderStatus.Ready,
            IsDelivered = true,
            DeliveredAt = now,
            DeliveredBy = "Chủ tiệm",
            Note = "Giao gấp trước 17h"
        };

        var snapshot = new ScanSnapshot
        {
            StartedAt = now,
            CompletedAt = now,
            Scope = ScanScope.Order,
            Status = ScanStatus.Success,
            AppVersion = "1.0.0"
        };

        await _orderRepo.SaveOrderAsync(order, snapshot);
        order.Id.Should().BeGreaterThan(0);

        var retrieved = await _orderRepo.GetOrderByIdAsync(order.Id);
        retrieved.Should().NotBeNull();
        retrieved!.IsDelivered.Should().BeTrue();
        retrieved.DeliveredAt.Should().NotBeNull();
        retrieved.DeliveredBy.Should().Be("Chủ tiệm");
        retrieved.Note.Should().Be("Giao gấp trước 17h");
    }

    [Fact]
    public async Task SqliteOrderRepository_UpdateOrderDeliveredStatusAsync_ShouldUpdateColumns()
    {
        var now = DateTimeOffset.Now;
        var order = new Order
        {
            WorkDate = "2026-10-01",
            OriginalFolderName = "Khach_Deliv_Update",
            RelativePath = "2026-10-01/Khach_Deliv_Update",
            Status = OrderStatus.Ready,
            IsDelivered = false
        };

        var snapshot = new ScanSnapshot
        {
            StartedAt = now,
            CompletedAt = now,
            Scope = ScanScope.Order,
            Status = ScanStatus.Success,
            AppVersion = "1.0.0"
        };

        await _orderRepo.SaveOrderAsync(order, snapshot);

        // 1. Mark delivered
        var deliveredAt = DateTimeOffset.UtcNow;
        await _orderRepo.UpdateOrderDeliveredStatusAsync(order.Id, true, deliveredAt, "Nhân viên");

        var retrieved1 = await _orderRepo.GetOrderByIdAsync(order.Id);
        retrieved1.Should().NotBeNull();
        retrieved1!.IsDelivered.Should().BeTrue();
        retrieved1.DeliveredBy.Should().Be("Nhân viên");
        retrieved1.DeliveredAt.Should().NotBeNull();

        // 2. Revert to undelivered
        await _orderRepo.UpdateOrderDeliveredStatusAsync(order.Id, false, null, null);

        var retrieved2 = await _orderRepo.GetOrderByIdAsync(order.Id);
        retrieved2.Should().NotBeNull();
        retrieved2!.IsDelivered.Should().BeFalse();
        retrieved2.DeliveredBy.Should().BeNull();
        retrieved2.DeliveredAt.Should().BeNull();
    }

    [Fact]
    public async Task SqliteOrderRepository_UpdateOrderNoteAsync_ShouldUpdateAndClearNote()
    {
        var now = DateTimeOffset.Now;
        var order = new Order
        {
            WorkDate = "2026-10-01",
            OriginalFolderName = "Khach_Note_Test",
            RelativePath = "2026-10-01/Khach_Note_Test",
            Status = OrderStatus.Ready,
            Note = null
        };

        var snapshot = new ScanSnapshot
        {
            StartedAt = now,
            CompletedAt = now,
            Scope = ScanScope.Order,
            Status = ScanStatus.Success,
            AppVersion = "1.0.0"
        };

        await _orderRepo.SaveOrderAsync(order, snapshot);

        // 1. Add note
        await _orderRepo.UpdateOrderNoteAsync(order.Id, "Thiếu 1 tấm ảnh cổng 60x90");
        var retrieved1 = await _orderRepo.GetOrderByIdAsync(order.Id);
        retrieved1!.Note.Should().Be("Thiếu 1 tấm ảnh cổng 60x90");

        // 2. Update note
        await _orderRepo.UpdateOrderNoteAsync(order.Id, "Đã bù ảnh cổng");
        var retrieved2 = await _orderRepo.GetOrderByIdAsync(order.Id);
        retrieved2!.Note.Should().Be("Đã bù ảnh cổng");

        // 3. Clear note
        await _orderRepo.UpdateOrderNoteAsync(order.Id, null);
        var retrieved3 = await _orderRepo.GetOrderByIdAsync(order.Id);
        retrieved3!.Note.Should().BeNull();
    }

    [Fact]
    public void OrderDisplayModel_Properties_ShouldReflectChanges()
    {
        var order = new Order
        {
            Id = 1,
            WorkDate = "2026-10-01",
            OriginalFolderName = "An_Studio",
            IsDelivered = false,
            Note = null
        };

        var model = new OrderDisplayModel(order);
        model.IsDelivered.Should().BeFalse();
        model.DeliveredStatusText.Should().Be("Chưa giao");
        model.HasNote.Should().BeFalse();

        // Change delivered
        var time = DateTimeOffset.Now;
        model.IsDelivered = true;
        model.DeliveredAt = time;
        model.DeliveredBy = "Nhân viên";

        model.DeliveredStatusText.Should().Be("🚚 ĐÃ GIAO");
        model.DeliveredDetailText.Should().Contain("Đã giao lúc");
        model.DeliveredDetailText.Should().NotContain("Nhân viên");

        // Change note
        model.Note = "Giao buổi chiều";
        model.HasNote.Should().BeTrue();
    }

    [Fact]
    public async Task PrintOrder_WhenAutoMarkDeliveredOnPrint_ShouldUpdateDeliveredStatus()
    {
        var now = DateTimeOffset.Now;
        var order = new Order
        {
            WorkDate = "2026-10-01",
            OriginalFolderName = "Khach_InTem_Test",
            RelativePath = "2026-10-01/Khach_InTem_Test",
            Status = OrderStatus.Ready,
            IsDelivered = false
        };

        var snapshot = new ScanSnapshot
        {
            StartedAt = now,
            CompletedAt = now,
            Scope = ScanScope.Order,
            Status = ScanStatus.Success,
            AppVersion = "1.0.0"
        };

        await _orderRepo.SaveOrderAsync(order, snapshot);
        order.Id.Should().BeGreaterThan(0);

        // Simulate printing triggering auto-delivered
        var deliveredAt = DateTimeOffset.Now;
        string deliveredBy = "In tem PC";
        await _orderRepo.UpdateOrderDeliveredStatusAsync(order.Id, true, deliveredAt, deliveredBy);

        var retrieved = await _orderRepo.GetOrderByIdAsync(order.Id);
        retrieved.Should().NotBeNull();
        retrieved!.IsDelivered.Should().BeTrue();
        retrieved.DeliveredBy.Should().Be("In tem PC");
        retrieved.DeliveredAt.Should().NotBeNull();

        var model = new OrderDisplayModel(retrieved);
        model.IsDelivered.Should().BeTrue();
        model.DeliveredStatusText.Should().Be("🚚 ĐÃ GIAO");
        model.DeliveredDetailText.Should().Contain("Đã giao lúc");
        model.DeliveredDetailText.Should().NotContain("In tem PC");
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }
}
