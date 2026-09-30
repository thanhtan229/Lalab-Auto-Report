using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.UI.ViewModels;
using Xunit;

namespace LalabAutoReport.Tests;

public class InvoiceHistoryAndReorganizationTests
{
    private class FakeReportService : IReportService
    {
        public Task<DailyReport> GetDailyReportAsync(string date, CancellationToken cancellationToken = default)
            => Task.FromResult(new DailyReport(date, 0, 0, 0, 0, 0, 0, Array.Empty<CustomerDailyAggregation>(), Array.Empty<SpecificationSummary>()));

        public Task<MonthlyReport> GetMonthlyReportAsync(int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult(new MonthlyReport(year, month, 0, 0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<DailySummary>(), Array.Empty<CustomerMonthlySummary>(), Array.Empty<SpecificationSummary>()));

        public Task<DateRangeReport> GetDateRangeReportAsync(string startDate, string endDate, CancellationToken cancellationToken = default)
            => Task.FromResult(new DateRangeReport(startDate, endDate, 0, 0, 0, 0, 0, 0, Array.Empty<DailySummary>(), Array.Empty<CustomerMonthlySummary>(), Array.Empty<SpecificationSummary>()));
    }

    private class FakeScanService : IScanService
    {
        public Task<IReadOnlyList<Order>> ScanDateAsync(string dateString, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Order>>(new List<Order>());

        public Task<Order?> ScanOrderAsync(string orderRelativePath, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult<Order?>(null);

        public Task<OrderItemScan?> ScanSpecificationAsync(string specRelativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<OrderItemScan?>(null);

        public Task<IReadOnlyList<string>> GetMissingScanDaysAsync(int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string>());

        public Task<IReadOnlyList<Order>> ScanDateRangeAsync(string startDateString, string endDateString, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Order>>(new List<Order>());

        public Task<IReadOnlyList<Order>> ScanMissingDaysAsync(int year, int month, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Order>>(new List<Order>());
    }

    private class FakeCustomerBillRepository : ICustomerBillRepository
    {
        public List<CustomerBill> Store { get; set; } = new();

        public Task<CustomerBill?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(Store.FirstOrDefault(b => b.Id == id));

        public Task<CustomerBill?> GetByBillNumberAsync(string billNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(Store.FirstOrDefault(b => b.BillNumber == billNumber));

        public Task<IReadOnlyList<CustomerBill>> GetAllBillsAsync(BillType? typeFilter = null, CancellationToken cancellationToken = default)
        {
            var res = Store.AsEnumerable();
            if (typeFilter.HasValue) res = res.Where(b => b.BillType == typeFilter.Value);
            return Task.FromResult<IReadOnlyList<CustomerBill>>(res.ToList());
        }

        public Task<IReadOnlyList<CustomerBill>> GetBillsByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerBill>>(Store.Where(b => b.CustomerId == customerId).ToList());

        public Task<CustomerBill?> GetLastLockedOrExportedBillByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(Store.Where(b => b.CustomerId == customerId && (b.Status == CustomerBillStatus.Locked || b.Status == CustomerBillStatus.Exported)).OrderByDescending(b => b.Id).FirstOrDefault());

        public Task<CustomerBill?> GetLockedBillByOrderIdAsync(long orderId, CancellationToken cancellationToken = default)
            => Task.FromResult<CustomerBill?>(null);

        public Task<CustomerBill?> GetActiveDraftByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(Store.FirstOrDefault(b => b.CustomerId == customerId && b.Status == CustomerBillStatus.Draft));

        public Task<IReadOnlyList<CustomerBill>> GetBillsBySourceFolderPathAsync(string normalizedFolderPath, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerBill>>(new List<CustomerBill>());

        public Task<IReadOnlyList<GuestBillSourceFolder>> GetSourceFoldersByBillIdAsync(long billId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GuestBillSourceFolder>>(new List<GuestBillSourceFolder>());

        public Task SaveBillAsync(CustomerBill bill, CancellationToken cancellationToken = default)
        {
            var idx = Store.FindIndex(b => b.Id == bill.Id);
            if (idx >= 0) Store[idx] = bill;
            else Store.Add(bill);
            return Task.CompletedTask;
        }

        public Task LockCustomerBillAtomicAsync(CustomerBill bill, IReadOnlyList<long> productJobIds, IReadOnlyList<long> orderIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ReopenCustomerBillAtomicAsync(CustomerBill bill, IReadOnlyList<long> productJobIds, IReadOnlyList<long> orderIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteDraftBillAsync(long billId, CancellationToken cancellationToken = default)
        {
            Store.RemoveAll(b => b.Id == billId);
            return Task.CompletedTask;
        }

        public Task DeleteBillAsync(long billId, CancellationToken cancellationToken = default)
        {
            Store.RemoveAll(b => b.Id == billId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CustomerBill>> GetBillsByDateAsync(string date, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerBill>>(Store.Where(b => (b.Status == CustomerBillStatus.Locked || b.Status == CustomerBillStatus.Exported) && (b.PeriodEnd == date || b.PeriodStart == date)).ToList());

        public Task<IReadOnlyList<CustomerBill>> GetBillsByMonthAsync(string yearMonth, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerBill>>(Store.Where(b => (b.Status == CustomerBillStatus.Locked || b.Status == CustomerBillStatus.Exported) && (b.PeriodEnd.StartsWith(yearMonth) || b.PeriodStart.StartsWith(yearMonth))).ToList());

        public Task<IReadOnlyList<CustomerBill>> GetBillsByDateRangeAsync(string startDate, string endDate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerBill>>(Store.Where(b => (b.Status == CustomerBillStatus.Locked || b.Status == CustomerBillStatus.Exported) && string.Compare(b.PeriodStart, startDate) >= 0 && string.Compare(b.PeriodEnd, endDate) <= 0).ToList());

        public Task<string> GenerateNextBillNumberAsync(string date, CancellationToken cancellationToken = default)
            => Task.FromResult($"BILL-{date}-0001");
    }

    private class FakeCustomerBillingService : ICustomerBillingService
    {
        public Task<CustomerUnbilledSummary> GetCustomerUnbilledSummaryAsync(long customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CustomerUnbilledSummary(customerId, "Test", 0, 0, 0, null, null, false));

        public Task<CustomerBillDraftResult> BuildOrRefreshDraftAsync(long customerId, bool forceRescan = true, CancellationToken cancellationToken = default)
            => Task.FromResult(new CustomerBillDraftResult(new CustomerBill(), new List<string>(), new List<string>()));

        public Task<IReadOnlyList<DuplicateFolderWarning>> CheckDuplicateSourceFoldersAsync(IEnumerable<string> folderPaths, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DuplicateFolderWarning>>(new List<DuplicateFolderWarning>());

        public Task<CustomerBillDraftResult> BuildGuestBillDraftAsync(IReadOnlyList<string> sourceFolderPaths, string? customGuestName = null, long? existingDraftId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new CustomerBillDraftResult(new CustomerBill { BillType = BillType.Guest }, new List<string>(), new List<string>()));

        public Task<Customer> ConvertGuestBillToCustomerAsync(long billId, string customerCanonicalName, CancellationToken cancellationToken = default)
            => Task.FromResult(new Customer { Id = 1, CanonicalName = customerCanonicalName });

        public CustomerBill RecalculateTotals(CustomerBill bill) => bill;

        public Task<CustomerBill> LockBillAsync(CustomerBill draft, CancellationToken cancellationToken = default)
            => Task.FromResult(draft);

        public Task ReopenBillAsync(long billId, string reason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordBillExportedAsync(long billId, string exportFilePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DetachOrderFromCustomerBillAsync(long orderId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SyncBillWithScannedOrderAsync(Order scannedOrder, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private class FakeJpegExporter : IJpegBillExporter
    {
        public Task<string> ExportBillToJpegAsync(CustomerBill bill, string? destinationDirectory = null, CancellationToken cancellationToken = default)
            => Task.FromResult(@"C:\FakePath\Bill.jpg");
    }

    private class FakeCustomerRepo : ICustomerRepository
    {
        public List<Customer> Customers { get; set; } = new();

        public Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Customer>>(Customers);

        public Task<Customer?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(Customers.FirstOrDefault(c => c.Id == id));

        public Task<Customer?> FindExactMatchAsync(string normalizedName, CancellationToken cancellationToken = default)
            => Task.FromResult(Customers.FirstOrDefault(c => c.CanonicalName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)));

        public Task<Customer> CreateCustomerAsync(Customer customer, string? initialAlias = null, CancellationToken cancellationToken = default)
        {
            customer.Id = Customers.Count + 1;
            Customers.Add(customer);
            return Task.FromResult(customer);
        }

        public Task UpdateCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddAliasAsync(long customerId, string aliasText, CancellationToken cancellationToken = default)
        {
            var cust = Customers.First(c => c.Id == customerId);
            cust.Aliases.Add(new CustomerAlias { Id = cust.Aliases.Count + 1, CustomerId = customerId, AliasText = aliasText, NormalizedAlias = aliasText.ToLowerInvariant() });
            return Task.CompletedTask;
        }

        public Task UpdateAliasAsync(long aliasId, string newAliasText, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveAliasAsync(long aliasId, CancellationToken cancellationToken = default)
        {
            foreach (var c in Customers) c.Aliases.RemoveAll(a => a.Id == aliasId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CustomerAlias>> GetAllAliasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerAlias>>(Customers.SelectMany(c => c.Aliases).ToList());

        public Task<CustomerAlias?> FindAliasByTextAsync(string aliasText, CancellationToken cancellationToken = default)
            => Task.FromResult(Customers.SelectMany(c => c.Aliases).FirstOrDefault(a => a.AliasText.Equals(aliasText, StringComparison.OrdinalIgnoreCase)));

        public Task ReassignAliasAsync(long aliasId, long newCustomerId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> HasCustomerHistoryAsync(long customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task DeleteCustomerAsync(long customerId, CancellationToken cancellationToken = default)
        {
            Customers.RemoveAll(c => c.Id == customerId);
            return Task.CompletedTask;
        }
    }

    private class FakeSettingsRepo : ISettingsRepository
    {
        public AppSettings CurrentSettings { get; set; } = new();

        public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CurrentSettings);

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            CurrentSettings = settings;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ReportsViewModel_ExcludesDraftBills_AndShowsOnlyLockedOrExported()
    {
        var billRepo = new FakeCustomerBillRepository
        {
            Store = new List<CustomerBill>
            {
                new CustomerBill { Id = 1, BillNumber = "BILL-001", Status = CustomerBillStatus.Draft, PeriodStart = "2026-09-01", PeriodEnd = "2026-09-30" },
                new CustomerBill { Id = 2, BillNumber = "BILL-002", Status = CustomerBillStatus.Locked, PeriodStart = "2026-09-01", PeriodEnd = "2026-09-30", GrandTotal = 150000 },
                new CustomerBill { Id = 3, BillNumber = "BILL-003", Status = CustomerBillStatus.Exported, PeriodStart = "2026-09-01", PeriodEnd = "2026-09-30", GrandTotal = 300000 }
            }
        };

        var vm = new ReportsViewModel(
            new FakeReportService(),
            new FakeScanService(),
            billRepo,
            new FakeCustomerBillingService(),
            new FakeJpegExporter()
        )
        {
            SelectedYear = 2026,
            SelectedMonth = 9
        };

        await vm.LoadReportBillsAsync();

        // Must NOT contain Draft bill
        vm.FilteredReportBills.Should().HaveCount(2);
        vm.FilteredReportBills.Select(b => b.BillNumber).Should().Contain(new[] { "BILL-002", "BILL-003" });
        vm.FilteredReportBills.Select(b => b.BillNumber).Should().NotContain("BILL-001");
        vm.TotalReportBillsAmount.Should().Be(450000);
    }

    [Fact]
    public async Task ReportsViewModel_ToggleAllTime_SwitchesFromPeriodToAllTime()
    {
        var billRepo = new FakeCustomerBillRepository
        {
            Store = new List<CustomerBill>
            {
                // In September 2026
                new CustomerBill { Id = 1, BillNumber = "BILL-SEP-001", Status = CustomerBillStatus.Locked, PeriodStart = "2026-09-01", PeriodEnd = "2026-09-10" },
                // In August 2026 (outside September)
                new CustomerBill { Id = 2, BillNumber = "BILL-AUG-001", Status = CustomerBillStatus.Exported, PeriodStart = "2026-08-01", PeriodEnd = "2026-08-10" }
            }
        };

        var vm = new ReportsViewModel(
            new FakeReportService(),
            new FakeScanService(),
            billRepo,
            new FakeCustomerBillingService(),
            new FakeJpegExporter()
        )
        {
            SelectedYear = 2026,
            SelectedMonth = 9,
            BillScopeAllTime = false
        };

        // When in period mode (September)
        await vm.LoadReportBillsAsync();
        vm.FilteredReportBills.Should().HaveCount(1);
        vm.FilteredReportBills.First().BillNumber.Should().Be("BILL-SEP-001");

        // When switching to All Time
        vm.BillScopeAllTime = true;
        await vm.LoadReportBillsAsync();
        vm.FilteredReportBills.Should().HaveCount(2);
        vm.FilteredReportBills.Select(b => b.BillNumber).Should().Contain(new[] { "BILL-SEP-001", "BILL-AUG-001" });
    }

    [Fact]
    public async Task ReportsViewModel_FilterByTypeAndSearchText_WorksProperly()
    {
        var billRepo = new FakeCustomerBillRepository
        {
            Store = new List<CustomerBill>
            {
                new CustomerBill { Id = 1, BillNumber = "BILL-001", BillType = BillType.Customer, CustomerNameSnapshot = "Nguyễn Văn An", PhoneSnapshot = "0901234567", Status = CustomerBillStatus.Locked, PeriodStart = "2026-09-01", PeriodEnd = "2026-09-30" },
                new CustomerBill { Id = 2, BillNumber = "BILL-002", BillType = BillType.Guest, CustomerNameSnapshot = "Khách Lẻ - Chị Lan", PhoneSnapshot = "0988776655", Status = CustomerBillStatus.Exported, PeriodStart = "2026-09-01", PeriodEnd = "2026-09-30" }
            }
        };

        var vm = new ReportsViewModel(
            new FakeReportService(),
            new FakeScanService(),
            billRepo,
            new FakeCustomerBillingService(),
            new FakeJpegExporter()
        )
        {
            SelectedYear = 2026,
            SelectedMonth = 9,
            BillScopeAllTime = true
        };

        await vm.LoadReportBillsAsync();
        vm.FilteredReportBills.Should().HaveCount(2);

        // Filter by Customer only
        vm.BillTypeFilter = "Customer";
        vm.FilteredReportBills.Should().HaveCount(1);
        vm.FilteredReportBills.First().BillNumber.Should().Be("BILL-001");

        // Filter by Guest only
        vm.BillTypeFilter = "Guest";
        vm.FilteredReportBills.Should().HaveCount(1);
        vm.FilteredReportBills.First().BillNumber.Should().Be("BILL-002");

        // Reset type, search by customer phone
        vm.BillTypeFilter = "All";
        vm.BillSearchText = "0988";
        vm.FilteredReportBills.Should().HaveCount(1);
        vm.FilteredReportBills.First().CustomerNameSnapshot.Should().Be("Khách Lẻ - Chị Lan");

        // Search by bill code
        vm.BillSearchText = "BILL-001";
        vm.FilteredReportBills.Should().HaveCount(1);
        vm.FilteredReportBills.First().BillNumber.Should().Be("BILL-001");
    }

    [Fact]
    public async Task CustomersViewModel_CustomerSelection_ShowsOnlySelectedCustomerBills()
    {
        var custRepo = new FakeCustomerRepo
        {
            Customers = new List<Customer>
            {
                new Customer { Id = 10, CanonicalName = "Studio Ánh Sáng" },
                new Customer { Id = 20, CanonicalName = "Studio Bình Minh" }
            }
        };

        var billRepo = new FakeCustomerBillRepository
        {
            Store = new List<CustomerBill>
            {
                new CustomerBill { Id = 1, BillNumber = "B-10", CustomerId = 10, BillType = BillType.Customer, Status = CustomerBillStatus.Locked },
                new CustomerBill { Id = 2, BillNumber = "B-20", CustomerId = 20, BillType = BillType.Customer, Status = CustomerBillStatus.Locked },
                new CustomerBill { Id = 3, BillNumber = "B-GUEST", CustomerId = null, BillType = BillType.Guest, Status = CustomerBillStatus.Exported }
            }
        };

        var settingsRepo = new FakeSettingsRepo();
        var vm = new CustomersViewModel(
            custRepo,
            new FakeCustomerBillingService(),
            billRepo,
            new FakeJpegExporter(),
            settingsRepo
        );

        await vm.LoadCustomersAsync();

        // 1. Select Customer 10
        vm.SelectedCustomer = vm.AllCustomers.First(c => c.Id == 10);
        await vm.RefreshBillsAsync();
        vm.CustomerBills.Should().HaveCount(1);
        vm.CustomerBills.First().BillNumber.Should().Be("B-10");

        // 2. Select Customer 20
        vm.SelectedCustomer = vm.AllCustomers.First(c => c.Id == 20);
        await vm.RefreshBillsAsync();
        vm.CustomerBills.Should().HaveCount(1);
        vm.CustomerBills.First().BillNumber.Should().Be("B-20");

        // 3. Select Guest Customer Item (Id = -1)
        vm.SelectedCustomer = vm.FilteredCustomers.First(c => c.Id == -1);
        vm.IsGuestSelected.Should().BeTrue();
        await vm.RefreshBillsAsync();
        vm.CustomerBills.Should().HaveCount(1);
        vm.CustomerBills.First().BillNumber.Should().Be("B-GUEST");
    }

    [Fact]
    public async Task CustomersViewModel_GuestAliases_SavedToSettings()
    {
        var custRepo = new FakeCustomerRepo();
        var settingsRepo = new FakeSettingsRepo
        {
            CurrentSettings = new AppSettings
            {
                GuestAliases = new List<string> { "khach_le", "khách lẻ" }
            }
        };

        var vm = new CustomersViewModel(
            custRepo,
            new FakeCustomerBillingService(),
            new FakeCustomerBillRepository(),
            new FakeJpegExporter(),
            settingsRepo
        );

        await vm.LoadCustomersAsync();

        // Select guest
        vm.SelectedCustomer = vm.FilteredCustomers.First(c => c.Id == -1);
        vm.IsGuestSelected.Should().BeTrue();

        // Add a new guest alias
        vm.NewAliasText = "chup_lay_lien";
        await vm.AddAliasCommand.ExecuteAsync(null);

        // Verify settings were updated
        var updatedSettings = await settingsRepo.GetSettingsAsync();
        updatedSettings.GuestAliases.Should().Contain("chup_lay_lien");

        // Cannot delete guest customer
        bool deleteResult = await vm.DeleteCustomerAsync();
        deleteResult.Should().BeFalse();
        vm.StatusMessage.Should().Contain("cố định của hệ thống");
    }

    [Fact]
    public void Order_IsGuestFolderName_RecognizesCustomGuestAliases()
    {
        var aliases = new List<string> { "khach_le", "khách lẻ", "chup_lay_ngay", "khach_vang_lai" };

        Order.IsGuestFolderName("2026-09-30_chup_lay_ngay_folder", null, aliases).Should().BeTrue();
        Order.IsGuestFolderName("KHACH_VANG_LAI_ANNA", null, aliases).Should().BeTrue();
        Order.IsGuestFolderName("Studio Ánh Sáng", null, aliases).Should().BeFalse();
        Order.IsGuestFolderName("2026-09-30_khách lẻ", null, null).Should().BeTrue(); // default built-in
    }
}
