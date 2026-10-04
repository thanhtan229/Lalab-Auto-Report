using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.Services;
using LalabAutoReport.UI.ViewModels;
using Xunit;

namespace LalabAutoReport.Tests;

public class GlobalSearchTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteOrderRepository _orderRepo;
    private readonly SqliteCustomerBillRepository _customerBillRepo;
    private readonly SqliteCustomerRepository _customerRepo;
    private readonly SqliteGlobalSearchService _searchService;

    public GlobalSearchTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_SearchTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _dbPath = Path.Combine(_tempRoot, "test_global_search.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _orderRepo = new SqliteOrderRepository(_connectionFactory);
        _customerBillRepo = new SqliteCustomerBillRepository(_connectionFactory);
        _customerRepo = new SqliteCustomerRepository(_connectionFactory);
        _searchService = new SqliteGlobalSearchService(_connectionFactory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task SearchAsync_WithBillNumber_FindsMatchingBill()
    {
        // Arrange
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Sài Gòn", Phone = "0901234567" });
        await _customerBillRepo.SaveBillAsync(new CustomerBill
        {
            BillNumber = "BILL-20260928-8888",
            CustomerId = customer.Id,
            CustomerNameSnapshot = customer.CanonicalName,
            PhoneSnapshot = customer.Phone,
            PeriodStart = "2026-09-28",
            PeriodEnd = "2026-09-28",
            Status = CustomerBillStatus.Locked,
            GrandTotal = 1_250_000,
            IsPaid = true
        });

        // Act
        var results = await _searchService.SearchAsync("8888");

        // Assert
        results.Should().NotBeEmpty();
        var billResult = results.FirstOrDefault(r => r.CodeOrNumber == "BILL-20260928-8888");
        billResult.Should().NotBeNull();
        billResult!.ResultType.Should().Be(GlobalSearchResultType.Bill);
        billResult.Amount.Should().Be(1_250_000);
        billResult.IsPaid.Should().BeTrue();
        billResult.Subtitle.Should().Contain("Studio Sài Gòn");
    }

    [Fact]
    public async Task SearchAsync_WithOrderCodeOrFolder_FindsMatchingOrder()
    {
        // Arrange
        var order = new Order
        {
            OrderCode = "ORD-20260928-099",
            OriginalFolderName = "KhachVIP_AlbumCuoi",
            OrderName = "Album Cuoi",
            RelativePath = "2026-09-28/KhachVIP_AlbumCuoi",
            WorkDate = "2026-09-28",
            Status = OrderStatus.Ready
        };
        var snapshot = new ScanSnapshot
        {
            StartedAt = DateTimeOffset.UtcNow,
            Status = ScanStatus.Success
        };
        await _orderRepo.SaveOrderAsync(order, snapshot);

        // Act - Search by order code fragment
        var resultsByCode = await _searchService.SearchAsync("099");
        resultsByCode.Should().ContainSingle(r => r.ResultType == GlobalSearchResultType.Order && r.Title == "ORD-20260928-099");

        // Act - Search by folder fragment
        var resultsByFolder = await _searchService.SearchAsync("AlbumCuoi");
        resultsByFolder.Should().ContainSingle(r => r.ResultType == GlobalSearchResultType.Order);
    }

    [Fact]
    public async Task SearchAsync_WithCustomerName_FindsMatchingBillsAndOrders()
    {
        // Arrange
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Thiên Phúc", Phone = "0988776655" });

        // Order linked to customer
        var order = new Order
        {
            OrderCode = "ORD-20260929-001",
            OriginalFolderName = "ThienPhuc",
            CustomerId = customer.Id,
            Customer = customer,
            RelativePath = "2026-09-29/ThienPhuc",
            WorkDate = "2026-09-29",
            Status = OrderStatus.Ready
        };
        await _orderRepo.SaveOrderAsync(order, new ScanSnapshot { StartedAt = DateTimeOffset.UtcNow, Status = ScanStatus.Success });

        // Bill for customer
        await _customerBillRepo.SaveBillAsync(new CustomerBill
        {
            BillNumber = "BILL-20260929-0001",
            CustomerId = customer.Id,
            CustomerNameSnapshot = customer.CanonicalName,
            PeriodStart = "2026-09-29",
            PeriodEnd = "2026-09-29",
            Status = CustomerBillStatus.Locked,
            GrandTotal = 450_000,
            IsPaid = false
        });

        // Act
        var results = await _searchService.SearchAsync("Thiên Phúc");

        // Assert - Both Order and Bill found
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.ResultType == GlobalSearchResultType.Bill && r.CodeOrNumber == "BILL-20260929-0001");
        results.Should().Contain(r => r.ResultType == GlobalSearchResultType.Order && r.CodeOrNumber == "ORD-20260929-001");
    }

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsEmptyList()
    {
        var results = await _searchService.SearchAsync("");
        results.Should().BeEmpty();

        var resultsWhitespace = await _searchService.SearchAsync("   ");
        resultsWhitespace.Should().BeEmpty();
    }

    [Fact]
    public async Task GlobalSearchViewModel_OpenSelectedResult_NavigatesToOrder()
    {
        // Arrange
        var orderResult = new GlobalSearchResult(
            GlobalSearchResultType.Order,
            1,
            "ORD-20260928-001",
            "ORD-20260928-001",
            "Studio Test",
            "2026-09-28",
            null,
            "2026-09-28/Test",
            false,
            "Ready"
        );

        var vm = new GlobalSearchViewModel(_searchService, _customerBillRepo)
        {
            SelectedResult = orderResult
        };

        string? requestedDate = null;
        string? requestedOrderCode = null;
        bool closeRequested = false;

        vm.NavigateToOrderRequested += (date, code) =>
        {
            requestedDate = date;
            requestedOrderCode = code;
        };
        vm.RequestClose += () => closeRequested = true;

        // Act
        await vm.OpenSelectedResultAsync();

        // Assert
        closeRequested.Should().BeTrue();
        requestedDate.Should().Be("2026-09-28");
        requestedOrderCode.Should().Be("ORD-20260928-001");
    }
}
