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
    private readonly ILogger<ReportService>? _logger;

    public ReportService(
        IOrderRepository orderRepository,
        IBillRepository billRepository,
        IBillingService billingService,
        IPrintSpecificationRepository specificationRepository,
        ICustomerRepository? customerRepository = null,
        IScanService? scanService = null,
        ILogger<ReportService>? logger = null)
    {
        _orderRepository = orderRepository;
        _billRepository = billRepository;
        _billingService = billingService;
        _specificationRepository = specificationRepository;
        _customerRepository = customerRepository;
        _scanService = scanService;
        _logger = logger;
    }

    public async Task<DailyReport> GetDailyReportAsync(string date, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Generating daily report from SQLite for date '{Date}'", date);

        var customers = await _billingService.GetDailyCustomerAggregationAsync(date, cancellationToken);
        var bills = await _billRepository.GetBillsByDateAsync(date, cancellationToken);
        var orders = await _orderRepository.GetOrdersByDateAsync(date, cancellationToken);

        int totalOrders = orders.Count;
        int totalCustomers = customers.Count;
        int totalBillQuantity = bills.Sum(b => b.Lines.Sum(l => l.BillQuantity));
        long totalAmount = bills.Sum(b => b.Subtotal);
        int unresolvedOrdersCount = orders.Count(o => o.Status == OrderStatus.NeedsReview || o.Status == OrderStatus.Error || o.Status == OrderStatus.Unscanned || o.FilesystemChangedAfterLock);
        int lockedOrdersCount = bills.Count(b => b.Status == OrderStatus.Locked);

        var specSummaries = await AggregateSpecificationsAsync(bills, cancellationToken);

        return new DailyReport(
            Date: date,
            TotalOrders: totalOrders,
            TotalCustomers: totalCustomers,
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

        var bills = await _billRepository.GetBillsByMonthAsync(prefix, cancellationToken);
        var orders = await _orderRepository.GetOrdersByDateRangeAsync(startDate, endDate, cancellationToken);

        var missingScanDays = _scanService != null
            ? await _scanService.GetMissingScanDaysAsync(year, month, cancellationToken)
            : Array.Empty<string>();

        int totalOrders = orders.Count;
        int totalBillQuantity = bills.Sum(b => b.Lines.Sum(l => l.BillQuantity));
        long totalAmount = bills.Sum(b => b.Subtotal);
        int unresolvedOrdersCount = orders.Count(o => o.Status == OrderStatus.NeedsReview || o.Status == OrderStatus.Error || o.Status == OrderStatus.Unscanned || o.FilesystemChangedAfterLock);
        int lockedOrdersCount = bills.Count(b => b.Status == OrderStatus.Locked);

        var dailySummaries = AggregateDailySummaries(orders, bills);
        var customerSummaries = await AggregateCustomerSummariesAsync(orders, bills, cancellationToken);
        var specSummaries = await AggregateSpecificationsAsync(bills, cancellationToken);

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

        var bills = await _billRepository.GetBillsByDateRangeAsync(startDate, endDate, cancellationToken);
        var orders = await _orderRepository.GetOrdersByDateRangeAsync(startDate, endDate, cancellationToken);

        int totalOrders = orders.Count;
        int totalBillQuantity = bills.Sum(b => b.Lines.Sum(l => l.BillQuantity));
        long totalAmount = bills.Sum(b => b.Subtotal);
        int unresolvedOrdersCount = orders.Count(o => o.Status == OrderStatus.NeedsReview || o.Status == OrderStatus.Error || o.Status == OrderStatus.Unscanned || o.FilesystemChangedAfterLock);
        int lockedOrdersCount = bills.Count(b => b.Status == OrderStatus.Locked);

        var dailySummaries = AggregateDailySummaries(orders, bills);
        var customerSummaries = await AggregateCustomerSummariesAsync(orders, bills, cancellationToken);
        var specSummaries = await AggregateSpecificationsAsync(bills, cancellationToken);

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

    private static IReadOnlyList<DailySummary> AggregateDailySummaries(IReadOnlyList<Order> orders, IReadOnlyList<Bill> bills)
    {
        var billByOrderId = bills.ToDictionary(b => b.OrderId);

        return orders
            .GroupBy(o => o.WorkDate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var dayOrders = g.ToList();
                var dayBills = dayOrders
                    .Where(o => billByOrderId.ContainsKey(o.Id))
                    .Select(o => billByOrderId[o.Id])
                    .ToList();

                int qty = dayBills.Sum(b => b.Lines.Sum(l => l.BillQuantity));
                long amount = dayBills.Sum(b => b.Subtotal);
                bool hasIssues = dayOrders.Any(o => o.Status == OrderStatus.NeedsReview || o.Status == OrderStatus.Error || o.Status == OrderStatus.Unscanned || o.FilesystemChangedAfterLock);

                return new DailySummary(
                    Date: g.Key,
                    TotalOrders: dayOrders.Count,
                    TotalBillQuantity: qty,
                    TotalAmount: amount,
                    HasUnresolvedIssues: hasIssues
                );
            })
            .ToList();
    }

    private async Task<IReadOnlyList<CustomerMonthlySummary>> AggregateCustomerSummariesAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyList<Bill> bills,
        CancellationToken cancellationToken)
    {
        var billByOrderId = bills.ToDictionary(b => b.OrderId);

        var groups = orders.GroupBy(o => o.CustomerId.HasValue ? $"CUST_{o.CustomerId.Value}" : $"FOLDER_{o.OriginalFolderName}");
        var summaries = new List<CustomerMonthlySummary>();

        foreach (var group in groups)
        {
            var custOrders = group.ToList();
            var custBills = custOrders
                .Where(o => billByOrderId.ContainsKey(o.Id))
                .Select(o => billByOrderId[o.Id])
                .ToList();

            long? customerId = custOrders.FirstOrDefault(o => o.CustomerId.HasValue)?.CustomerId;
            string displayName = custOrders[0].OriginalFolderName;

            if (customerId.HasValue && _customerRepository != null)
            {
                var cust = await _customerRepository.GetByIdAsync(customerId.Value, cancellationToken);
                if (cust != null)
                {
                    displayName = cust.CanonicalName;
                }
            }

            int qty = custBills.Sum(b => b.Lines.Sum(l => l.BillQuantity));
            long amount = custBills.Sum(b => b.Subtotal);
            bool hasIssues = custOrders.Any(o => o.Status == OrderStatus.NeedsReview || o.Status == OrderStatus.Error || o.Status == OrderStatus.Unscanned || o.FilesystemChangedAfterLock);

            summaries.Add(new CustomerMonthlySummary(
                CustomerId: customerId,
                DisplayName: displayName,
                TotalOrders: custOrders.Count,
                TotalBillQuantity: qty,
                TotalAmount: amount,
                HasUnresolvedIssues: hasIssues
            ));
        }

        return summaries.OrderByDescending(c => c.TotalAmount).ThenBy(c => c.DisplayName).ToList();
    }

    private async Task<IReadOnlyList<SpecificationSummary>> AggregateSpecificationsAsync(
        IReadOnlyList<Bill> bills,
        CancellationToken cancellationToken)
    {
        var lineGroups = bills
            .SelectMany(b => b.Lines)
            .GroupBy(l => l.PrintSpecificationId);

        var summaries = new List<SpecificationSummary>();
        foreach (var group in lineGroups)
        {
            var spec = await _specificationRepository.GetByIdAsync(group.Key, cancellationToken);
            string name = spec?.CanonicalName ?? $"Quy cách #{group.Key}";
            int totalQty = group.Sum(l => l.BillQuantity);
            long totalAmount = group.Sum(l => l.LineTotal);

            summaries.Add(new SpecificationSummary(
                PrintSpecificationId: group.Key,
                SpecificationName: name,
                TotalBillQuantity: totalQty,
                TotalAmount: totalAmount
            ));
        }

        return summaries.OrderByDescending(s => s.TotalAmount).ThenBy(s => s.SpecificationName).ToList();
    }
}
