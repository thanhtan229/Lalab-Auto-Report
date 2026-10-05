using System;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.UI.ViewModels;
using Xunit;

namespace LalabAutoReport.Tests;

public class OrderItemDisplayModelTests
{
    [Fact]
    public void FormattedUnitPrice_AlbumStandard_ReturnsConciseFormattedString()
    {
        var spec = new PrintSpecification
        {
            Id = 1,
            CanonicalName = "Album 20x20",
            Category = ProductCategory.Album,
            BillingMethod = BillingMethod.AlbumBasePlusExtra,
            BasePrice = 400000,
            IncludedSheets = 10,
            ExtraSheetPrice = 20000
        };

        var scan = new OrderItemScan
        {
            PrintSpecification = spec,
            PrintCount = 10
        };

        var order = new Order
        {
            Id = 1,
            OriginalFolderName = "TestOrder",
            RelativePath = "2026-09-29/TestOrder",
            WorkDate = "2026-09-29"
        };
        order.Items.Add(scan);
        var parentVm = new OrderDisplayModel(order);
        var itemVm = parentVm.Items[0];

        itemVm.FormattedUnitPrice.Should().Be("400,000 đ(10 tờ)(+20k/tờ)");
    }

    [Fact]
    public void FormattedUnitPrice_AlbumDifferentPrices_FormatsCorrectly()
    {
        var spec1 = new PrintSpecification
        {
            Id = 1,
            CanonicalName = "Album 25x25",
            Category = ProductCategory.Album,
            BillingMethod = BillingMethod.AlbumBasePlusExtra,
            BasePrice = 550000,
            IncludedSheets = 15,
            ExtraSheetPrice = 25000
        };

        var scan1 = new OrderItemScan { PrintSpecification = spec1 };
        var order1 = new Order { Id = 1, OriginalFolderName = "Test", RelativePath = "2026-09-29/Test", WorkDate = "2026-09-29" };
        order1.Items.Add(scan1);
        var parentVm1 = new OrderDisplayModel(order1);
        var itemVm1 = parentVm1.Items[0];

        itemVm1.FormattedUnitPrice.Should().Be("550,000 đ(15 tờ)(+25k/tờ)");

        // Non-round thousands e.g. 22500 -> 22.5k
        var spec2 = new PrintSpecification
        {
            Id = 2,
            CanonicalName = "Album 30x30",
            Category = ProductCategory.Album,
            BillingMethod = BillingMethod.AlbumBasePlusExtra,
            BasePrice = 700000,
            IncludedSheets = 20,
            ExtraSheetPrice = 22500
        };
        var scan2 = new OrderItemScan { PrintSpecification = spec2 };
        var order2 = new Order { Id = 2, OriginalFolderName = "Test2", RelativePath = "2026-09-29/Test2", WorkDate = "2026-09-29" };
        order2.Items.Add(scan2);
        var parentVm2 = new OrderDisplayModel(order2);
        var itemVm2 = parentVm2.Items[0];

        itemVm2.FormattedUnitPrice.Should().Be("700,000 đ(20 tờ)(+22.5k/tờ)");
    }

    [Fact]
    public void FormattedUnitPrice_PhotoPrint_PreservesPerPhotoFormat()
    {
        var spec = new PrintSpecification
        {
            Id = 3,
            CanonicalName = "CP3 13x18",
            Category = ProductCategory.PhotoPrint,
            BillingMethod = BillingMethod.FileCount,
            UnitPrice = 4500
        };

        var scan = new OrderItemScan { PrintSpecification = spec };
        var order = new Order { Id = 1, OriginalFolderName = "Test", RelativePath = "2026-09-29/Test", WorkDate = "2026-09-29" };
        order.Items.Add(scan);
        var parentVm = new OrderDisplayModel(order);
        var itemVm = parentVm.Items[0];

        itemVm.FormattedUnitPrice.Should().Be("4,500 đ/ảnh");
    }

    [Fact]
    public void FormattedUnitPrice_NullSpec_ReturnsDash()
    {
        var scan = new OrderItemScan { PrintSpecification = null };
        var order = new Order { Id = 1, OriginalFolderName = "Test", RelativePath = "2026-09-29/Test", WorkDate = "2026-09-29" };
        order.Items.Add(scan);
        var parentVm = new OrderDisplayModel(order);
        var itemVm = parentVm.Items[0];

        itemVm.FormattedUnitPrice.Should().Be("--");
    }

    [Fact]
    public void OrderDisplayModel_StatusText_WhenLockedOrBilled_ReturnsDaTinhBill()
    {
        var orderLocked = new Order { Id = 1, Status = OrderStatus.Locked };
        var vmLocked = new OrderDisplayModel(orderLocked);
        vmLocked.StatusText.Should().Be("Đã tính Bill");
        vmLocked.IsBilled.Should().BeTrue();

        var orderBilled = new Order { Id = 2, Status = OrderStatus.Billed };
        var vmBilled = new OrderDisplayModel(orderBilled);
        vmBilled.StatusText.Should().Be("Đã tính Bill");
        vmBilled.IsBilled.Should().BeTrue();
    }

    [Fact]
    public void OrderDisplayModel_StatusText_WhenFilesystemChanged_ReturnsWarning()
    {
        var order = new Order
        {
            Id = 1,
            Status = OrderStatus.Locked,
            FilesystemChangedAfterLock = true
        };
        var vm = new OrderDisplayModel(order);
        vm.StatusText.Should().Be("⚠️ Đã tính Bill (File thay đổi!)");
        vm.HasIssues.Should().BeTrue();
    }

    [Fact]
    public void OrderDisplayModel_StatusChange_NotifiesIsBilledAndUpdatesValue()
    {
        var order = new Order
        {
            Id = 1,
            OriginalFolderName = "TestOrder",
            Status = OrderStatus.Ready
        };
        var vm = new OrderDisplayModel(order);
        vm.IsBilled.Should().BeFalse();

        var notifiedProperties = new List<string>();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) notifiedProperties.Add(e.PropertyName);
        };

        vm.Status = OrderStatus.Locked;
        vm.IsBilled.Should().BeTrue();
        notifiedProperties.Should().Contain(nameof(vm.IsBilled));

        notifiedProperties.Clear();
        vm.Status = OrderStatus.Ready;
        vm.IsBilled.Should().BeFalse();
        notifiedProperties.Should().Contain(nameof(vm.IsBilled));
    }

    [Fact]
    public void OrderDisplayModel_CustomerId_ReflectsUnderlyingOrderCustomerId()
    {
        var order = new Order
        {
            Id = 1,
            CustomerId = 42,
            Customer = new Customer { Id = 42, CanonicalName = "Nguyen Van A" }
        };
        var vm = new OrderDisplayModel(order);
        vm.CustomerId.Should().Be(42);
        vm.CanonicalCustomerName.Should().Be("Nguyen Van A");
    }

    [Fact]
    public void OrderDisplayModel_DeliveredStatusAndTooltip_ToggleCorrectly()
    {
        var order = new Order
        {
            Id = 1,
            OriginalFolderName = "TestOrder",
            IsDelivered = false
        };
        var vm = new OrderDisplayModel(order);

        // When not delivered
        vm.IsDelivered.Should().BeFalse();
        vm.DeliveredStatusText.Should().Be("Chưa giao");
        vm.DeliveredButtonTooltip.Should().Contain("Chưa giao hàng");
        vm.DeliveredButtonTooltip.Should().Contain("ĐÃ GIAO");

        var notifiedProperties = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) notifiedProperties.Add(e.PropertyName);
        };

        // When delivered
        var deliveredTime = new DateTimeOffset(2026, 10, 2, 10, 30, 0, TimeSpan.FromHours(7));
        vm.DeliveredAt = deliveredTime;
        vm.DeliveredBy = "Chủ tiệm";
        vm.IsDelivered = true;

        vm.IsDelivered.Should().BeTrue();
        vm.DeliveredStatusText.Should().Be("🚚 ĐÃ GIAO");
        vm.DeliveredButtonTooltip.Should().Contain("Đã giao lúc");
        vm.DeliveredButtonTooltip.Should().NotContain("Chủ tiệm");
        vm.DeliveredButtonTooltip.Should().NotContain("Nhân viên");
        vm.DeliveredButtonTooltip.Should().Contain("hoàn tác");
        vm.DeliveredDetailText.Should().Be("Đã giao lúc 02/10/2026 10:30");

        notifiedProperties.Should().Contain(nameof(vm.DeliveredButtonTooltip));
        notifiedProperties.Should().Contain(nameof(vm.DeliveredDetailText));
        notifiedProperties.Should().Contain(nameof(vm.DeliveredStatusText));
    }

    [Fact]
    public void OrderDisplayModel_WhenNoCustomer_DefaultsToGuestBadge_AndNeverShowsUnassigned()
    {
        var order = new Order
        {
            Id = 1,
            OriginalFolderName = "Chi Lan Q7",
            CustomerId = null,
            Customer = null
        };
        var vm = new OrderDisplayModel(order);

        vm.HasCustomer.Should().BeFalse();
        vm.IsGuest.Should().BeTrue();
        vm.CustomerBadgeText.Should().Be("⚡ Khách lẻ: Chi Lan Q7");
        vm.CustomerBadgeText.Should().NotContain("Chưa gán");
        vm.CustomerBadgeBg.Should().Be("#FEF3C7");
        vm.CustomerBadgeFg.Should().Be("#B45309");
        vm.CustomerMappingTooltip.Should().Contain("Khách lẻ");
    }

    [Fact]
    public void OrderDisplayModel_WhenHasCustomer_ShowsRegularCustomerBadge()
    {
        var customer = new Customer
        {
            Id = 10,
            CanonicalName = "Studio Paris"
        };
        var order = new Order
        {
            Id = 2,
            OriginalFolderName = "Paris Q1",
            CustomerId = customer.Id,
            Customer = customer
        };
        var vm = new OrderDisplayModel(order);

        vm.HasCustomer.Should().BeTrue();
        vm.IsGuest.Should().BeFalse();
        vm.CustomerBadgeText.Should().Be("👤 Studio Paris");
        vm.CustomerBadgeBg.Should().Be("#E0F2FE");
        vm.CustomerBadgeFg.Should().Be("#0369A1");
        vm.CustomerMappingTooltip.Should().Contain("Studio Paris");
    }
}
