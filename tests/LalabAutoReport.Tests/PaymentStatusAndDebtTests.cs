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

public class PaymentStatusAndDebtTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteCustomerBillRepository _customerBillRepo;
    private readonly SqliteCustomerRepository _customerRepo;

    public PaymentStatusAndDebtTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lalab_PaymentTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _dbPath = Path.Combine(_tempRoot, "test_payment_debt.db");
        _connectionFactory = new SqliteConnectionFactory(_dbPath);
        _migrator = new DatabaseMigrator(_connectionFactory);
        _migrator.MigrateAsync().GetAwaiter().GetResult();

        _customerBillRepo = new SqliteCustomerBillRepository(_connectionFactory);
        _customerRepo = new SqliteCustomerRepository(_connectionFactory);
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
    public async Task SetPaymentStatusAsync_UpdatesIsPaidAndPaidAt()
    {
        // Arrange
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Ánh Sáng" });
        var bill = new CustomerBill
        {
            BillNumber = "BILL-20261001-0001",
            CustomerId = customer.Id,
            CustomerNameSnapshot = customer.CanonicalName,
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Locked,
            GrandTotal = 500_000,
            IsPaid = false
        };
        await _customerBillRepo.SaveBillAsync(bill);

        var retrieved = await _customerBillRepo.GetByIdAsync(bill.Id);
        retrieved.Should().NotBeNull();
        retrieved!.IsPaid.Should().BeFalse();
        retrieved.PaidAt.Should().BeNull();

        // Act - Mark Paid
        var paidTime = DateTimeOffset.UtcNow;
        await _customerBillRepo.SetPaymentStatusAsync(bill.Id, true, paidTime);

        // Assert
        var paidBill = await _customerBillRepo.GetByIdAsync(bill.Id);
        paidBill.Should().NotBeNull();
        paidBill!.IsPaid.Should().BeTrue();
        paidBill.PaidAt.Should().NotBeNull();
        paidBill.PaidAt!.Value.ToString("yyyy-MM-dd HH:mm").Should().Be(paidTime.ToString("yyyy-MM-dd HH:mm"));

        // Act - Mark Unpaid
        await _customerBillRepo.SetPaymentStatusAsync(bill.Id, false);
        var unpaidBill = await _customerBillRepo.GetByIdAsync(bill.Id);
        unpaidBill.Should().NotBeNull();
        unpaidBill!.IsPaid.Should().BeFalse();
        unpaidBill.PaidAt.Should().BeNull();
    }

    [Fact]
    public async Task GetCustomerTotalDebtAsync_CalculatesOnlyUnpaidLockedBills()
    {
        // Arrange
        var customer1 = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Hoàng Gia" });
        var customer2 = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Nắng Mai" });

        // Bill 1: Customer 1, 300k, Locked, Unpaid -> debt +300k
        await _customerBillRepo.SaveBillAsync(new CustomerBill
        {
            BillNumber = "BILL-20261001-0001",
            CustomerId = customer1.Id,
            CustomerNameSnapshot = customer1.CanonicalName,
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Locked,
            GrandTotal = 300_000,
            IsPaid = false
        });

        // Bill 2: Customer 1, 200k, Exported, Unpaid -> debt +200k
        await _customerBillRepo.SaveBillAsync(new CustomerBill
        {
            BillNumber = "BILL-20261001-0002",
            CustomerId = customer1.Id,
            CustomerNameSnapshot = customer1.CanonicalName,
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Exported,
            GrandTotal = 200_000,
            IsPaid = false
        });

        // Bill 3: Customer 1, 400k, Exported, PAID -> should NOT count as debt
        await _customerBillRepo.SaveBillAsync(new CustomerBill
        {
            BillNumber = "BILL-20261001-0003",
            CustomerId = customer1.Id,
            CustomerNameSnapshot = customer1.CanonicalName,
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Exported,
            GrandTotal = 400_000,
            IsPaid = true,
            PaidAt = DateTimeOffset.UtcNow
        });

        // Bill 4: Customer 1, 150k, Draft (not locked) -> should NOT count as debt
        await _customerBillRepo.SaveBillAsync(new CustomerBill
        {
            BillNumber = "BILL-20261001-0004",
            CustomerId = customer1.Id,
            CustomerNameSnapshot = customer1.CanonicalName,
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Draft,
            GrandTotal = 150_000,
            IsPaid = false
        });

        // Bill 5: Customer 2, 700k, Locked, Unpaid -> belongs to customer 2
        await _customerBillRepo.SaveBillAsync(new CustomerBill
        {
            BillNumber = "BILL-20261001-0005",
            CustomerId = customer2.Id,
            CustomerNameSnapshot = customer2.CanonicalName,
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Locked,
            GrandTotal = 700_000,
            IsPaid = false
        });

        // Act
        long debt1 = await _customerBillRepo.GetCustomerTotalDebtAsync(customer1.Id);
        long debt2 = await _customerBillRepo.GetCustomerTotalDebtAsync(customer2.Id);

        // Assert
        debt1.Should().Be(500_000); // 300k + 200k
        debt2.Should().Be(700_000);
    }

    [Fact]
    public async Task GetUnpaidBillsAsync_ReturnsOnlyUnpaidLockedBills()
    {
        // Arrange
        var customer = await _customerRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Minh Quân" });

        var billUnpaid = new CustomerBill
        {
            BillNumber = "BILL-20261001-0010",
            CustomerId = customer.Id,
            CustomerNameSnapshot = customer.CanonicalName,
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Locked,
            GrandTotal = 250_000,
            IsPaid = false
        };
        await _customerBillRepo.SaveBillAsync(billUnpaid);

        var billPaid = new CustomerBill
        {
            BillNumber = "BILL-20261001-0011",
            CustomerId = customer.Id,
            CustomerNameSnapshot = customer.CanonicalName,
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-01",
            Status = CustomerBillStatus.Locked,
            GrandTotal = 350_000,
            IsPaid = true,
            PaidAt = DateTimeOffset.UtcNow
        };
        await _customerBillRepo.SaveBillAsync(billPaid);

        // Act
        var unpaidBills = await _customerBillRepo.GetUnpaidBillsAsync(customer.Id);

        // Assert
        unpaidBills.Should().HaveCount(1);
        unpaidBills[0].BillNumber.Should().Be("BILL-20261001-0010");
        unpaidBills[0].IsPaid.Should().BeFalse();
    }
}
