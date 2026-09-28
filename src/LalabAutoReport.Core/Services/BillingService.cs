using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class BillingService : IBillingService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IBillRepository _billRepository;
    private readonly IPrintSpecificationRepository _specificationRepository;
    private readonly ICustomerRepository? _customerRepository;
    private readonly ILogger<BillingService>? _logger;

    public BillingService(
        IOrderRepository orderRepository,
        IBillRepository billRepository,
        IPrintSpecificationRepository specificationRepository,
        ICustomerRepository? customerRepository = null,
        ILogger<BillingService>? logger = null)
    {
        _orderRepository = orderRepository;
        _billRepository = billRepository;
        _specificationRepository = specificationRepository;
        _customerRepository = customerRepository;
        _logger = logger;
    }

    public async Task<Bill> CalculateBillForOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        // 1. Fetch order and its latest items
        var order = await _orderRepository.GetOrderByIdAsync(orderId, cancellationToken);

        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy đơn hàng ID {orderId}.");
        }

        if (order.Items.Count == 0)
        {
            throw new InvalidOperationException($"Đơn hàng '{order.OriginalFolderName}' không có quy cách in nào để tính tiền.");
        }

        var lines = new List<BillLine>();
        long subtotal = 0;

        foreach (var item in order.Items)
        {
            // Verify valid print folder
            if (item.PrintFolderStatus != PrintFolderResolutionStatus.Resolved || !item.PrintCount.HasValue)
            {
                throw new InvalidOperationException($"Quy cách '{item.SpecificationFolderName}' chưa có thư mục in hợp lệ.");
            }

            // Auto-resolve bill quantity if counts match
            if (item.SourceCount == item.PrintCount.Value && !item.BillQuantity.HasValue)
            {
                item.BillQuantity = item.PrintCount.Value;
                item.QuantityResolutionMode = QuantityResolutionMode.AutoMatch;
                await _orderRepository.UpdateOrderItemResolutionAsync(item.Id, item.BillQuantity.Value, QuantityResolutionMode.AutoMatch, null, cancellationToken);
            }

            // Mismatch must be resolved
            if (!item.BillQuantity.HasValue)
            {
                throw new InvalidOperationException($"Quy cách '{item.SpecificationFolderName}' bị lệch số lượng (Gốc: {item.SourceCount}, In: {item.PrintCount}) và chưa được chọn số lượng tính tiền.");
            }

            if (item.BillQuantity.Value < 0)
            {
                throw new InvalidOperationException($"Số lượng tính tiền của quy cách '{item.SpecificationFolderName}' không được nhỏ hơn 0.");
            }

            // Resolve spec & price
            if (!item.PrintSpecificationId.HasValue)
            {
                throw new InvalidOperationException($"Quy cách '{item.SpecificationFolderName}' chưa được liên kết với bảng giá.");
            }

            var spec = await _specificationRepository.GetByIdAsync(item.PrintSpecificationId.Value, cancellationToken);
            if (spec == null)
            {
                throw new InvalidOperationException($"Không tìm thấy quy cách ID {item.PrintSpecificationId.Value} trong bảng giá.");
            }

            long unitPrice = spec.UnitPrice;
            long lineTotal = item.BillQuantity.Value * unitPrice;
            subtotal += lineTotal;

            lines.Add(new BillLine
            {
                PrintSpecificationId = spec.Id,
                SourceCount = item.SourceCount,
                PrintCount = item.PrintCount,
                BillQuantity = item.BillQuantity.Value,
                QuantityResolutionMode = item.QuantityResolutionMode ?? QuantityResolutionMode.AutoMatch,
                QuantityResolutionNote = item.QuantityResolutionNote,
                UnitPrice = unitPrice,
                LineTotal = lineTotal,
                SourceScanSnapshotId = item.ScanSnapshotId,
                PrintSpecification = spec
            });
        }

        var bill = new Bill
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId ?? 0,
            Status = OrderStatus.Billed,
            Subtotal = subtotal,
            Lines = lines,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _billRepository.SaveBillAsync(bill, cancellationToken);
        _logger?.LogInformation("Calculated bill for order {OrderId} ({FolderName}): Total {Total} VND, {Lines} lines.", order.Id, order.OriginalFolderName, subtotal, lines.Count);

        return bill;
    }

    public async Task<IReadOnlyList<CustomerDailyAggregation>> GetDailyCustomerAggregationAsync(string date, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetOrdersByDateAsync(date, cancellationToken);
        var bills = await _billRepository.GetBillsByDateAsync(date, cancellationToken);
        var billByOrderId = bills.ToDictionary(b => b.OrderId);

        // Group by CustomerId if present, else by OriginalFolderName
        var groups = orders.GroupBy(o => o.CustomerId.HasValue ? $"CUST_{o.CustomerId.Value}" : $"FOLDER_{o.OriginalFolderName}");

        var aggregations = new List<CustomerDailyAggregation>();

        foreach (var group in groups)
        {
            var groupOrders = group.ToList();
            var groupBills = groupOrders
                .Where(o => billByOrderId.ContainsKey(o.Id))
                .Select(o => billByOrderId[o.Id])
                .ToList();

            long? customerId = groupOrders.FirstOrDefault(o => o.CustomerId.HasValue)?.CustomerId;
            string displayName = groupOrders[0].OriginalFolderName;

            if (customerId.HasValue && _customerRepository != null)
            {
                var cust = await _customerRepository.GetByIdAsync(customerId.Value, cancellationToken);
                if (cust != null)
                {
                    displayName = cust.CanonicalName;
                }
            }

            int totalBillQty = groupBills.Sum(b => b.Lines.Sum(l => l.BillQuantity));
            long totalAmount = groupBills.Sum(b => b.Subtotal);
            bool hasIssues = groupOrders.Any(o => o.Status == OrderStatus.NeedsReview || o.Status == OrderStatus.Error || o.Status == OrderStatus.Unscanned);

            aggregations.Add(new CustomerDailyAggregation(
                CustomerId: customerId,
                DisplayName: displayName,
                WorkDate: date,
                Orders: groupOrders,
                Bills: groupBills,
                TotalBillQuantity: totalBillQty,
                TotalAmount: totalAmount,
                HasUnresolvedIssues: hasIssues
            ));
        }

        return aggregations.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
