using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class LockingService : ILockingService
{
    private readonly IScanService _scanService;
    private readonly IOrderRepository _orderRepository;
    private readonly IBillRepository _billRepository;
    private readonly IBillingService _billingService;
    private readonly ILogger<LockingService>? _logger;

    public LockingService(
        IScanService scanService,
        IOrderRepository orderRepository,
        IBillRepository billRepository,
        IBillingService billingService,
        ILogger<LockingService>? logger = null)
    {
        _scanService = scanService;
        _orderRepository = orderRepository;
        _billRepository = billRepository;
        _billingService = billingService;
        _logger = logger;
    }

    public async Task<VerificationResult> VerifyOrderForLockAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy đơn hàng ID {orderId}.");
        }

        // 1. Authoritative fresh scan before lock
        var freshOrder = await _scanService.ScanOrderAsync(order.RelativePath, null, cancellationToken);
        if (freshOrder == null)
        {
            return new VerificationResult(
                CanLock: false,
                BlockingReasons: new[] { "Không thể quét lại thư mục đơn hàng (thư mục không tồn tại hoặc đã bị xóa)." },
                PreviewBill: null,
                Order: order
            );
        }

        var blockingReasons = new List<string>();

        // 2. Customer resolution verification
        if (!freshOrder.CustomerId.HasValue)
        {
            blockingReasons.Add($"Khách hàng '{freshOrder.OriginalFolderName}' chưa được xác định hoặc gán danh tính.");
        }

        // 3. Specification verification
        if (freshOrder.Items.Count == 0)
        {
            blockingReasons.Add("Đơn hàng không có thư mục quy cách in nào.");
        }

        foreach (var item in freshOrder.Items)
        {
            // Print folder check
            if (item.PrintFolderStatus != PrintFolderResolutionStatus.Resolved || !item.PrintCount.HasValue)
            {
                blockingReasons.Add($"Quy cách '{item.SpecificationFolderName}' chưa có thư mục in hợp lệ ({item.PrintFolderStatus}).");
            }

            // Price list check
            if (!item.PrintSpecificationId.HasValue)
            {
                blockingReasons.Add($"Quy cách '{item.SpecificationFolderName}' chưa được liên kết với bảng giá.");
            }

            // Quantity mismatch check
            if (item.MismatchCount.HasValue && item.MismatchCount.Value != 0 && !item.BillQuantity.HasValue)
            {
                blockingReasons.Add($"Quy cách '{item.SpecificationFolderName}' bị lệch số lượng (Gốc: {item.SourceCount}, In: {item.PrintCount}) và chưa được chọn số lượng tính tiền.");
            }

            // Scanner error check
            if (item.ScanStatus == ScanStatus.Failed)
            {
                blockingReasons.Add($"Quy cách '{item.SpecificationFolderName}' gặp lỗi khi quét: {item.ErrorMessage}");
            }
        }

        if (blockingReasons.Count > 0)
        {
            return new VerificationResult(
                CanLock: false,
                BlockingReasons: blockingReasons,
                PreviewBill: null,
                Order: freshOrder
            );
        }

        // 4. Calculate preview bill
        try
        {
            var previewBill = await _billingService.CalculateBillForOrderAsync(freshOrder.Id, cancellationToken);
            return new VerificationResult(
                CanLock: true,
                BlockingReasons: Array.Empty<string>(),
                PreviewBill: previewBill,
                Order: freshOrder
            );
        }
        catch (Exception ex)
        {
            return new VerificationResult(
                CanLock: false,
                BlockingReasons: new[] { $"Lỗi khi tính bill: {ex.Message}" },
                PreviewBill: null,
                Order: freshOrder
            );
        }
    }

    public async Task<Bill> VerifyAndLockOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var verification = await VerifyOrderForLockAsync(orderId, cancellationToken);
        if (!verification.CanLock || verification.PreviewBill == null)
        {
            string reasons = string.Join("; ", verification.BlockingReasons);
            _logger?.LogWarning("Attempted to lock order {OrderId} but verification failed: {Reasons}", orderId, reasons);
            throw new InvalidOperationException($"Không thể khóa đơn hàng: {reasons}");
        }

        var bill = verification.PreviewBill;
        bill.Status = OrderStatus.Locked;
        bill.LockedAt = DateTimeOffset.UtcNow;
        bill.UpdatedAt = DateTimeOffset.UtcNow;

        // Persist bill snapshot
        await _billRepository.SaveBillAsync(bill, cancellationToken);

        // Update order status in repository
        await _orderRepository.UpdateOrderStatusAsync(orderId, OrderStatus.Locked, cancellationToken);
        await _orderRepository.SetFilesystemChangedAfterLockAsync(orderId, false, cancellationToken);

        _logger?.LogInformation("Order {OrderId} locked successfully. Total: {Total} VND, LockedAt: {LockedAt}",
            orderId, bill.Subtotal, bill.LockedAt);

        return bill;
    }

    public async Task ReopenOrderAsync(long orderId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Lý do mở khóa không được để trống.", nameof(reason));
        }

        var order = await _orderRepository.GetOrderByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy đơn hàng ID {orderId}.");
        }

        if (order.Status != OrderStatus.Locked)
        {
            throw new InvalidOperationException($"Đơn hàng ID {orderId} chưa bị khóa, không cần mở lại.");
        }

        var bill = await _billRepository.GetBillByOrderIdAsync(orderId, cancellationToken);
        if (bill != null)
        {
            bill.Status = OrderStatus.Billed;
            bill.LockedAt = null;
            bill.UpdatedAt = DateTimeOffset.UtcNow;
            await _billRepository.SaveBillAsync(bill, cancellationToken);
        }

        await _orderRepository.UpdateOrderStatusAsync(orderId, OrderStatus.Ready, cancellationToken);
        await _orderRepository.SetFilesystemChangedAfterLockAsync(orderId, false, cancellationToken);

        _logger?.LogWarning("Order {OrderId} ({RelativePath}) REOPENED by user. Reason: {Reason}",
            orderId, order.RelativePath, reason);
    }

    public async Task<bool> CheckFilesystemChangedAfterLockAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderByIdAsync(orderId, cancellationToken);
        var bill = await _billRepository.GetBillByOrderIdAsync(orderId, cancellationToken);

        if (order == null || order.Status != OrderStatus.Locked || bill == null || bill.Status != OrderStatus.Locked)
        {
            return false;
        }

        var freshOrder = await _scanService.ScanOrderAsync(order.RelativePath, null, cancellationToken);
        if (freshOrder == null)
        {
            return true; // Deleted on disk
        }

        if (bill.Lines.Count != freshOrder.Items.Count)
        {
            return true;
        }

        foreach (var line in bill.Lines)
        {
            var match = freshOrder.Items.FirstOrDefault(i => i.PrintSpecificationId == line.PrintSpecificationId);
            if (match == null) return true;
            if (match.SourceCount != line.SourceCount || match.PrintCount != line.PrintCount) return true;
        }

        return false;
    }
}
