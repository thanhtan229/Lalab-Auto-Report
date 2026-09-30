using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Core.Services;

public class ReportService : IReportService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IBillRepository _billRepository;
    private readonly IBillingService _billingService;
    private readonly IPrintSpecificationRepository _specificationRepository;
    private readonly ICustomerRepository? _customerRepository;
    private readonly IScanService? _scanService;
    private readonly ICustomerBillRepository? _customerBillRepository;
    private readonly ILogger<ReportService>? _logger;

    public ReportService(
        IOrderRepository orderRepository,
        IBillRepository billRepository,
        IBillingService billingService,
        IPrintSpecificationRepository specificationRepository,
        ICustomerRepository? customerRepository = null,
        IScanService? scanService = null,
        ICustomerBillRepository? customerBillRepository = null,
        ILogger<ReportService>? logger = null)
    {
        _orderRepository = orderRepository;
        _billRepository = billRepository;
        _billingService = billingService;
        _specificationRepository = specificationRepository;
        _customerRepository = customerRepository;
        _scanService = scanService;
        _customerBillRepository = customerBillRepository;
        _logger = logger;
    }

    public async Task<DailyReport> GetDailyReportAsync(string date, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Generating daily report from SQLite for date '{Date}'", date);

        var orders = await _orderRepository.GetOrdersByDateAsync(date, cancellationToken);
        var legacyBills = await _billRepository.GetBillsByDateAsync(date, cancellationToken);
        var customerBills = _customerBillRepository != null
            ? await _customerBillRepository.GetBillsByDateAsync(date, cancellationToken)
            : Array.Empty<CustomerBill>();

        var (resolvedOrders, matchedCustomerBills) = await ResolveOrderDataAsync(orders, legacyBills, customerBills, cancellationToken);

        int totalOrders = orders.Count;
        int totalBillQuantity = resolvedOrders.Sum(ro => ro.BillQuantity);
        long productTotal = resolvedOrders.Sum(ro => ro.Subtotal);
        long adjustmentsTotal = matchedCustomerBills
            .Where(cb => cb.PeriodEnd == date || (cb.LockedAt.HasValue && cb.LockedAt.Value.ToString("yyyy-MM-dd") == date))
            .Sum(cb => cb.AdjustmentsTotal);
        long totalAmount = Math.Max(0, productTotal + adjustmentsTotal);

        int unresolvedOrdersCount = resolvedOrders.Count(ro => ro.HasIssues);
        int lockedOrdersCount = resolvedOrders.Count(ro => ro.IsLocked);

        var specSummaries = AggregateSpecifications(resolvedOrders);
        var customers = await AggregateDailyCustomersAsync(date, resolvedOrders, matchedCustomerBills, cancellationToken);

        return new DailyReport(
            Date: date,
            TotalOrders: totalOrders,
            TotalCustomers: customers.Count,
            TotalBillQuantity: totalBillQuantity,
            TotalAmount: totalAmount,
            UnresolvedOrdersCount: unresolvedOrdersCount,
            LockedOrdersCount: lockedOrdersCount,
            Customers: customers,
            Specifications: specSummaries
        );
    }

    public async Task<MonthlyReport> GetMonthlyReportAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Generating monthly report from SQLite for {Year:D4}-{Month:D2}", year, month);

        string prefix = $"{year:D4}-{month:D2}";
        string startDate = $"{prefix}-01";
        int daysInMonth = DateTime.DaysInMonth(year, month);
        string endDate = $"{prefix}-{daysInMonth:D2}";

        var orders = await _orderRepository.GetOrdersByDateRangeAsync(startDate, endDate, cancellationToken);
        var legacyBills = await _billRepository.GetBillsByMonthAsync(prefix, cancellationToken);
        var customerBills = _customerBillRepository != null
            ? await _customerBillRepository.GetBillsByMonthAsync(prefix, cancellationToken)
            : Array.Empty<CustomerBill>();

        var missingScanDays = _scanService != null
            ? await _scanService.GetMissingScanDaysAsync(year, month, cancellationToken)
            : Array.Empty<string>();

        var (resolvedOrders, matchedCustomerBills) = await ResolveOrderDataAsync(orders, legacyBills, customerBills, cancellationToken);

        int totalOrders = orders.Count;
        int totalBillQuantity = resolvedOrders.Sum(ro => ro.BillQuantity);
        long productTotal = resolvedOrders.Sum(ro => ro.Subtotal);
        long adjustmentsTotal = matchedCustomerBills.Sum(cb => cb.AdjustmentsTotal);
        long totalAmount = Math.Max(0, productTotal + adjustmentsTotal);

        int unresolvedOrdersCount = resolvedOrders.Count(ro => ro.HasIssues);
        int lockedOrdersCount = resolvedOrders.Count(ro => ro.IsLocked);

        var dailySummaries = AggregateDailySummaries(resolvedOrders, matchedCustomerBills);
        var customerSummaries = await AggregateCustomerSummariesAsync(resolvedOrders, matchedCustomerBills, cancellationToken);
        var specSummaries = AggregateSpecifications(resolvedOrders);

        return new MonthlyReport(
            Year: year,
            Month: month,
            TotalOrders: totalOrders,
            TotalCustomers: customerSummaries.Count,
            TotalBillQuantity: totalBillQuantity,
            TotalAmount: totalAmount,
            UnresolvedOrdersCount: unresolvedOrdersCount,
            LockedOrdersCount: lockedOrdersCount,
            MissingScanDays: missingScanDays,
            DailySummaries: dailySummaries,
            CustomerSummaries: customerSummaries,
            SpecificationSummaries: specSummaries
        );
    }

    public async Task<DateRangeReport> GetDateRangeReportAsync(string startDate, string endDate, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Generating date range report from SQLite for range '{StartDate}' to '{EndDate}'", startDate, endDate);

        var orders = await _orderRepository.GetOrdersByDateRangeAsync(startDate, endDate, cancellationToken);
        var legacyBills = await _billRepository.GetBillsByDateRangeAsync(startDate, endDate, cancellationToken);
        var customerBills = _customerBillRepository != null
            ? await _customerBillRepository.GetBillsByDateRangeAsync(startDate, endDate, cancellationToken)
            : Array.Empty<CustomerBill>();

        var (resolvedOrders, matchedCustomerBills) = await ResolveOrderDataAsync(orders, legacyBills, customerBills, cancellationToken);

        int totalOrders = orders.Count;
        int totalBillQuantity = resolvedOrders.Sum(ro => ro.BillQuantity);
        long productTotal = resolvedOrders.Sum(ro => ro.Subtotal);
        long adjustmentsTotal = matchedCustomerBills.Sum(cb => cb.AdjustmentsTotal);
        long totalAmount = Math.Max(0, productTotal + adjustmentsTotal);

        int unresolvedOrdersCount = resolvedOrders.Count(ro => ro.HasIssues);
        int lockedOrdersCount = resolvedOrders.Count(ro => ro.IsLocked);

        var dailySummaries = AggregateDailySummaries(resolvedOrders, matchedCustomerBills);
        var customerSummaries = await AggregateCustomerSummariesAsync(resolvedOrders, matchedCustomerBills, cancellationToken);
        var specSummaries = AggregateSpecifications(resolvedOrders);

        return new DateRangeReport(
            StartDate: startDate,
            EndDate: endDate,
            TotalOrders: totalOrders,
            TotalCustomers: customerSummaries.Count,
            TotalBillQuantity: totalBillQuantity,
            TotalAmount: totalAmount,
            UnresolvedOrdersCount: unresolvedOrdersCount,
            LockedOrdersCount: lockedOrdersCount,
            DailySummaries: dailySummaries,
            CustomerSummaries: customerSummaries,
            SpecificationSummaries: specSummaries
        );
    }

    private async Task<(List<ResolvedOrderData> ResolvedOrders, List<CustomerBill> MatchedCustomerBills)> ResolveOrderDataAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyList<Bill> legacyBills,
        IReadOnlyList<CustomerBill> customerBills,
        CancellationToken cancellationToken)
    {
        var legacyBillByOrderId = legacyBills.ToDictionary(b => b.OrderId);

        // Map orderId -> CustomerBill
        var orderToCustomerBill = new Dictionary<long, CustomerBill>();
        foreach (var cb in customerBills)
        {
            foreach (var cbo in cb.Orders.Where(o => o.IsIncluded))
            {
                orderToCustomerBill[cbo.OrderId] = cb;
            }
        }

        var resolvedList = new List<ResolvedOrderData>();

        foreach (var order in orders)
        {
            var data = new ResolvedOrderData { Order = order };

            if (orderToCustomerBill.TryGetValue(order.Id, out var cb))
            {
                data.CustomerBill = cb;
                var cbLines = cb.Lines.Where(l => l.OrderId == order.Id && l.IsIncluded).ToList();
                foreach (var cbl in cbLines)
                {
                    data.Lines.Add(new ResolvedLineData
                    {
                        PrintSpecificationId = cbl.ProductSpecificationId ?? 0,
                        SpecificationName = !string.IsNullOrWhiteSpace(cbl.ProductNameSnapshot)
                            ? cbl.ProductNameSnapshot
                            : (cbl.ProductSpecificationId.HasValue ? $"Quy cách #{cbl.ProductSpecificationId}" : "Khác"),
                        Quantity = cbl.BilledQuantity,
                        LineTotal = cbl.LineTotal
                    });
                }
            }
            else if (legacyBillByOrderId.TryGetValue(order.Id, out var lb))
            {
                data.LegacyBill = lb;
                foreach (var bl in lb.Lines)
                {
                    string specName = bl.ProductNameSnapshot ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(specName) && _specificationRepository != null)
                    {
                        var spec = await _specificationRepository.GetByIdAsync(bl.PrintSpecificationId, cancellationToken);
                        specName = spec?.CanonicalName ?? $"Quy cách #{bl.PrintSpecificationId}";
                    }

                    data.Lines.Add(new ResolvedLineData
                    {
                        PrintSpecificationId = bl.PrintSpecificationId,
                        SpecificationName = specName,
                        Quantity = bl.BillQuantity,
                        LineTotal = bl.LineTotal
                    });
                }
            }
            else if (order.Status == OrderStatus.Ready || order.Status == OrderStatus.Billed || order.Status == OrderStatus.Locked)
            {
                // Fallback: Compute directly from order.Items
                foreach (var item in order.Items)
                {
                    if (item.PrintFolderStatus == PrintFolderResolutionStatus.Resolved && item.PrintCount.HasValue)
                    {
                        int qty = item.BillQuantity ?? item.PrintCount.Value;
                        var spec = item.PrintSpecification;
                        long lineTotal = 0;

                        if (spec != null)
                        {
                            if (spec.BillingMethod == BillingMethod.AlbumBasePlusExtra)
                            {
                                int sheets = item.PrintableFileCount > 0 ? item.PrintableFileCount.Value : item.PrintCount.Value;
                                int included = spec.IncludedSheets ?? 10;
                                int extra = Math.Max(0, sheets - included);
                                lineTotal = (spec.BasePrice ?? 0) + (extra * (spec.ExtraSheetPrice ?? 0));
                                qty = 1;
                            }
                            else
                            {
                                lineTotal = (long)qty * spec.UnitPrice;
                            }
                        }

                        data.Lines.Add(new ResolvedLineData
                        {
                            PrintSpecificationId = item.PrintSpecificationId ?? 0,
                            SpecificationName = spec?.CanonicalName ?? item.SpecificationFolderName,
                            Quantity = qty,
                            LineTotal = lineTotal
                        });
                    }
                }
            }

            resolvedList.Add(data);
        }

        return (resolvedList, customerBills.ToList());
    }

    private static IReadOnlyList<DailySummary> AggregateDailySummaries(
        List<ResolvedOrderData> resolvedOrders,
        List<CustomerBill> matchedCustomerBills)
    {
        return resolvedOrders
            .GroupBy(ro => ro.Order.WorkDate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                string dayDate = g.Key;
                int ordersCount = g.Count();
                int qty = g.Sum(ro => ro.BillQuantity);
                long prodTotal = g.Sum(ro => ro.Subtotal);
                long dayAdjustments = matchedCustomerBills
                    .Where(cb => cb.PeriodEnd == dayDate || (cb.LockedAt.HasValue && cb.LockedAt.Value.ToString("yyyy-MM-dd") == dayDate))
                    .Sum(cb => cb.AdjustmentsTotal);
                long total = Math.Max(0, prodTotal + dayAdjustments);
                bool hasIssues = g.Any(ro => ro.HasIssues);

                return new DailySummary(
                    Date: dayDate,
                    TotalOrders: ordersCount,
                    TotalBillQuantity: qty,
                    TotalAmount: total,
                    HasUnresolvedIssues: hasIssues
                );
            })
            .ToList();
    }

    private async Task<IReadOnlyList<CustomerMonthlySummary>> AggregateCustomerSummariesAsync(
        List<ResolvedOrderData> resolvedOrders,
        List<CustomerBill> matchedCustomerBills,
        CancellationToken cancellationToken)
    {
        var groups = resolvedOrders.GroupBy(ro =>
            ro.Order.CustomerId.HasValue
                ? $"CUST_{ro.Order.CustomerId.Value}"
                : (ro.CustomerBill != null && !string.IsNullOrWhiteSpace(ro.CustomerBill.CustomerNameSnapshot)
                    ? $"GUEST_{ro.CustomerBill.CustomerNameSnapshot}"
                    : $"FOLDER_{ro.Order.OriginalFolderName}"));

        var summaries = new List<CustomerMonthlySummary>();

        foreach (var group in groups)
        {
            long? customerId = group.FirstOrDefault(ro => ro.Order.CustomerId.HasValue)?.Order.CustomerId;
            string displayName = group.First().Order.OriginalFolderName;

            if (customerId.HasValue && _customerRepository != null)
            {
                var cust = await _customerRepository.GetByIdAsync(customerId.Value, cancellationToken);
                if (cust != null) displayName = cust.CanonicalName;
            }
            else if (group.Any(ro => ro.CustomerBill != null && !string.IsNullOrWhiteSpace(ro.CustomerBill.CustomerNameSnapshot)))
            {
                displayName = group.First(ro => ro.CustomerBill != null).CustomerBill!.CustomerNameSnapshot;
            }

            int qty = group.Sum(ro => ro.BillQuantity);
            long prodTotal = group.Sum(ro => ro.Subtotal);

            var groupCustBills = matchedCustomerBills.Where(cb =>
                (customerId.HasValue && cb.CustomerId == customerId.Value) ||
                (!customerId.HasValue && cb.CustomerNameSnapshot == displayName)).ToList();

            long groupAdjustments = groupCustBills.Sum(cb => cb.AdjustmentsTotal);
            long amount = Math.Max(0, prodTotal + groupAdjustments);
            bool hasIssues = group.Any(ro => ro.HasIssues);

            summaries.Add(new CustomerMonthlySummary(
                CustomerId: customerId,
                DisplayName: displayName,
                TotalOrders: group.Count(),
                TotalBillQuantity: qty,
                TotalAmount: amount,
                HasUnresolvedIssues: hasIssues
            ));
        }

        return summaries.OrderByDescending(c => c.TotalAmount).ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<IReadOnlyList<CustomerDailyAggregation>> AggregateDailyCustomersAsync(
        string date,
        List<ResolvedOrderData> resolvedOrders,
        List<CustomerBill> matchedCustomerBills,
        CancellationToken cancellationToken)
    {
        var groups = resolvedOrders.GroupBy(ro =>
            ro.Order.CustomerId.HasValue
                ? $"CUST_{ro.Order.CustomerId.Value}"
                : (ro.CustomerBill != null && !string.IsNullOrWhiteSpace(ro.CustomerBill.CustomerNameSnapshot)
                    ? $"GUEST_{ro.CustomerBill.CustomerNameSnapshot}"
                    : $"FOLDER_{ro.Order.OriginalFolderName}"));

        var aggregations = new List<CustomerDailyAggregation>();

        foreach (var group in groups)
        {
            var groupOrders = group.Select(ro => ro.Order).ToList();
            var groupBills = group.Where(ro => ro.LegacyBill != null).Select(ro => ro.LegacyBill!).ToList();

            long? customerId = group.FirstOrDefault(ro => ro.Order.CustomerId.HasValue)?.Order.CustomerId;
            string displayName = group.First().Order.OriginalFolderName;

            if (customerId.HasValue && _customerRepository != null)
            {
                var cust = await _customerRepository.GetByIdAsync(customerId.Value, cancellationToken);
                if (cust != null) displayName = cust.CanonicalName;
            }
            else if (group.Any(ro => ro.CustomerBill != null && !string.IsNullOrWhiteSpace(ro.CustomerBill.CustomerNameSnapshot)))
            {
                displayName = group.First(ro => ro.CustomerBill != null).CustomerBill!.CustomerNameSnapshot;
            }

            int qty = group.Sum(ro => ro.BillQuantity);
            long prodSubtotal = group.Sum(ro => ro.Subtotal);

            var groupCustBills = matchedCustomerBills.Where(cb =>
                (customerId.HasValue && cb.CustomerId == customerId.Value) ||
                (!customerId.HasValue && cb.CustomerNameSnapshot == displayName)).ToList();

            long adjustments = groupCustBills
                .Where(cb => cb.PeriodEnd == date || (cb.LockedAt.HasValue && cb.LockedAt.Value.ToString("yyyy-MM-dd") == date))
                .Sum(cb => cb.AdjustmentsTotal);
            long amount = Math.Max(0, prodSubtotal + adjustments);
            bool hasIssues = group.Any(ro => ro.HasIssues);

            aggregations.Add(new CustomerDailyAggregation(
                CustomerId: customerId,
                DisplayName: displayName,
                WorkDate: date,
                Orders: groupOrders,
                Bills: groupBills,
                TotalBillQuantity: qty,
                TotalAmount: amount,
                HasUnresolvedIssues: hasIssues
            ));
        }

        return aggregations.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<SpecificationSummary> AggregateSpecifications(List<ResolvedOrderData> resolvedOrders)
    {
        var lineGroups = resolvedOrders
            .SelectMany(ro => ro.Lines)
            .GroupBy(l => l.PrintSpecificationId != 0 ? $"ID_{l.PrintSpecificationId}" : $"NAME_{l.SpecificationName}");

        var summaries = new List<SpecificationSummary>();
        foreach (var group in lineGroups)
        {
            var first = group.First();
            int totalQty = group.Sum(l => l.Quantity);
            long totalAmount = group.Sum(l => l.LineTotal);

            summaries.Add(new SpecificationSummary(
                PrintSpecificationId: first.PrintSpecificationId,
                SpecificationName: first.SpecificationName,
                TotalBillQuantity: totalQty,
                TotalAmount: totalAmount
            ));
        }

        return summaries.OrderByDescending(s => s.TotalAmount).ThenBy(s => s.SpecificationName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private class ResolvedOrderData
    {
        public Order Order { get; set; } = null!;
        public CustomerBill? CustomerBill { get; set; }
        public Bill? LegacyBill { get; set; }
        public List<ResolvedLineData> Lines { get; set; } = new();
        public int BillQuantity => Lines.Sum(l => l.Quantity);
        public long Subtotal => Lines.Sum(l => l.LineTotal);
        public bool IsLocked => CustomerBill != null || LegacyBill?.Status == OrderStatus.Locked || Order.Status == OrderStatus.Locked;
        public bool HasIssues => Order.Status == OrderStatus.NeedsReview || Order.Status == OrderStatus.Error || Order.Status == OrderStatus.Unscanned || Order.FilesystemChangedAfterLock;
    }

    private class ResolvedLineData
    {
        public long PrintSpecificationId { get; set; }
        public string SpecificationName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public long LineTotal { get; set; }
    }
}
