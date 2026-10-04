using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;

namespace LalabAutoReport.Infrastructure.Services;

public class SqliteGlobalSearchService : IGlobalSearchService
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteGlobalSearchService(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GlobalSearchResult>();
        }

        string trimmed = query.Trim();
        string pattern = $"%{trimmed}%";

        using var connection = _connectionFactory.CreateConnection();

        // 1. Search Customer Bills
        var bills = (await connection.QueryAsync<BillSearchRow>(@"
            SELECT b.id, b.bill_number, b.bill_type, b.customer_name_snapshot, b.phone_snapshot,
                   b.period_start, b.period_end, b.grand_total, b.status, b.is_paid, b.export_file_path
            FROM customer_bills b
            WHERE b.bill_number LIKE @Pattern
               OR b.customer_name_snapshot LIKE @Pattern
               OR b.phone_snapshot LIKE @Pattern
               OR b.note LIKE @Pattern
               OR b.export_file_path LIKE @Pattern
            ORDER BY b.id DESC
            LIMIT @Limit
        ", new { Pattern = pattern, Limit = limit })).ToList();

        // 2. Search Orders
        var orders = (await connection.QueryAsync<OrderSearchRow>(@"
            SELECT o.id, o.order_code, o.original_folder_name, o.order_name, o.work_date,
                   o.status, o.relative_path, c.canonical_name, c.phone
            FROM orders o
            LEFT JOIN customers c ON o.customer_id = c.id
            WHERE o.order_code LIKE @Pattern
               OR o.original_folder_name LIKE @Pattern
               OR o.order_name LIKE @Pattern
               OR o.relative_path LIKE @Pattern
               OR c.canonical_name LIKE @Pattern
               OR c.phone LIKE @Pattern
            ORDER BY o.work_date DESC, o.id DESC
            LIMIT @Limit
        ", new { Pattern = pattern, Limit = limit })).ToList();

        var results = new List<GlobalSearchResult>();

        // Format bill results
        foreach (var b in bills)
        {
            string dateRange = b.period_start == b.period_end || string.IsNullOrEmpty(b.period_end)
                ? b.period_start
                : $"{b.period_start} → {b.period_end}";

            string subtitle = $"{b.customer_name_snapshot}{(string.IsNullOrWhiteSpace(b.phone_snapshot) ? "" : " • " + b.phone_snapshot)}";

            results.Add(new GlobalSearchResult(
                ResultType: GlobalSearchResultType.Bill,
                Id: b.id,
                CodeOrNumber: b.bill_number,
                Title: b.bill_number,
                Subtitle: subtitle,
                Date: dateRange,
                Amount: b.grand_total,
                FolderPath: b.export_file_path,
                IsPaid: b.is_paid == 1,
                Status: b.status
            ));
        }

        // Format order results
        foreach (var o in orders)
        {
            string title = !string.IsNullOrWhiteSpace(o.order_code)
                ? o.order_code
                : o.original_folder_name;

            string subtitle = !string.IsNullOrWhiteSpace(o.canonical_name)
                ? $"{o.canonical_name} ({o.original_folder_name})"
                : o.original_folder_name;

            results.Add(new GlobalSearchResult(
                ResultType: GlobalSearchResultType.Order,
                Id: o.id,
                CodeOrNumber: o.order_code ?? $"#{o.id}",
                Title: title,
                Subtitle: subtitle,
                Date: o.work_date,
                Amount: null,
                FolderPath: o.relative_path,
                IsPaid: false,
                Status: o.status
            ));
        }

        return results.Take(limit).ToList();
    }

    private class BillSearchRow
    {
        public long id { get; set; }
        public string bill_number { get; set; } = string.Empty;
        public string? bill_type { get; set; }
        public string customer_name_snapshot { get; set; } = string.Empty;
        public string? phone_snapshot { get; set; }
        public string period_start { get; set; } = string.Empty;
        public string period_end { get; set; } = string.Empty;
        public long grand_total { get; set; }
        public string status { get; set; } = string.Empty;
        public long is_paid { get; set; }
        public string? export_file_path { get; set; }
    }

    private class OrderSearchRow
    {
        public long id { get; set; }
        public string? order_code { get; set; }
        public string original_folder_name { get; set; } = string.Empty;
        public string? order_name { get; set; }
        public string work_date { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public string relative_path { get; set; } = string.Empty;
        public string? canonical_name { get; set; }
        public string? phone { get; set; }
    }
}
