using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.UI.ViewModels;
using Xunit;

namespace LalabAutoReport.Tests;

public class TieredPricingTests
{
    [Fact]
    public void GetEffectiveUnitPrice_ReturnsTierPrice_OrFallsBackToRetail()
    {
        var spec = new PrintSpecification
        {
            Id = 1,
            CanonicalName = "13x18 in",
            UnitPrice = 5000,
            UnitPriceStudio = 4000,
            UnitPriceVip = 3500
        };

        // Explicit tier prices
        Assert.Equal(5000, spec.GetEffectiveUnitPrice(PriceTier.Retail));
        Assert.Equal(4000, spec.GetEffectiveUnitPrice(PriceTier.Studio));
        Assert.Equal(3500, spec.GetEffectiveUnitPrice(PriceTier.Vip));

        // Fallback when unconfigured (0 or null)
        var specFallback = new PrintSpecification
        {
            Id = 2,
            CanonicalName = "20x30 in",
            UnitPrice = 15000,
            UnitPriceStudio = 0,
            UnitPriceVip = null
        };

        Assert.Equal(15000, specFallback.GetEffectiveUnitPrice(PriceTier.Retail));
        Assert.Equal(15000, specFallback.GetEffectiveUnitPrice(PriceTier.Studio));
        Assert.Equal(15000, specFallback.GetEffectiveUnitPrice(PriceTier.Vip));
    }

    [Fact]
    public void GetEffectiveAlbumPrices_ReturnsTierPrices_OrFallsBackToRetail()
    {
        var albumSpec = new PrintSpecification
        {
            Id = 3,
            CanonicalName = "Album 25x25",
            Category = ProductCategory.Album,
            BillingMethod = BillingMethod.AlbumBasePlusExtra,
            IncludedSheets = 10,
            BasePrice = 400000,
            ExtraSheetPrice = 20000,
            BasePriceStudio = 350000,
            ExtraSheetPriceStudio = 18000,
            BasePriceVip = 300000,
            ExtraSheetPriceVip = 15000
        };

        var (retailBase, retailExtra) = albumSpec.GetEffectiveAlbumPrices(PriceTier.Retail);
        Assert.Equal(400000, retailBase);
        Assert.Equal(20000, retailExtra);

        var (studioBase, studioExtra) = albumSpec.GetEffectiveAlbumPrices(PriceTier.Studio);
        Assert.Equal(350000, studioBase);
        Assert.Equal(18000, studioExtra);

        var (vipBase, vipExtra) = albumSpec.GetEffectiveAlbumPrices(PriceTier.Vip);
        Assert.Equal(300000, vipBase);
        Assert.Equal(15000, vipExtra);

        // Fallback when unconfigured
        var fallbackAlbum = new PrintSpecification
        {
            Id = 4,
            CanonicalName = "Album 30x30",
            BasePrice = 500000,
            ExtraSheetPrice = 25000,
            BasePriceStudio = null,
            ExtraSheetPriceStudio = 0,
            BasePriceVip = 0,
            ExtraSheetPriceVip = null
        };

        var (fbStudioBase, fbStudioExtra) = fallbackAlbum.GetEffectiveAlbumPrices(PriceTier.Studio);
        Assert.Equal(500000, fbStudioBase);
        Assert.Equal(25000, fbStudioExtra);

        var (fbVipBase, fbVipExtra) = fallbackAlbum.GetEffectiveAlbumPrices(PriceTier.Vip);
        Assert.Equal(500000, fbVipBase);
        Assert.Equal(25000, fbVipExtra);
    }

    [Fact]
    public async Task CustomerRepository_PersistsAndRetrievesPriceTier()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"lalab_tier_test_{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var migrator = new DatabaseMigrator(factory);
            await migrator.MigrateAsync();

            var custRepo = new SqliteCustomerRepository(factory);

            var customer = new Customer
            {
                CanonicalName = "Studio Ánh Dương",
                Phone = "0901234567",
                PriceTier = PriceTier.Studio
            };

            var created = await custRepo.CreateCustomerAsync(customer);
            Assert.Equal(PriceTier.Studio, created.PriceTier);

            var fetched = await custRepo.GetByIdAsync(created.Id);
            Assert.NotNull(fetched);
            Assert.Equal(PriceTier.Studio, fetched.PriceTier);

            // Update to VIP
            fetched.PriceTier = PriceTier.Vip;
            await custRepo.UpdateCustomerAsync(fetched);

            var updated = await custRepo.GetByIdAsync(created.Id);
            Assert.NotNull(updated);
            Assert.Equal(PriceTier.Vip, updated.PriceTier);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task ProductRepository_PersistsAndRetrievesTieredPrices()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"lalab_prod_tier_test_{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var migrator = new DatabaseMigrator(factory);
            await migrator.MigrateAsync();

            var prodRepo = new SqliteProductRepository(factory);

            var spec = new PrintSpecification
            {
                CanonicalName = "15x21 VIP Test",
                CanonicalSize = "15x21",
                Category = ProductCategory.PhotoPrint,
                BillingMethod = BillingMethod.FileCount,
                UnitPrice = 6000,
                UnitPriceStudio = 5000,
                UnitPriceVip = 4500,
                IsActive = true
            };

            var created = await prodRepo.CreateSpecificationAsync(spec);
            Assert.Equal(6000, created.UnitPrice);
            Assert.Equal(5000, created.UnitPriceStudio);
            Assert.Equal(4500, created.UnitPriceVip);

            var fetched = await prodRepo.GetByIdAsync(created.Id);
            Assert.NotNull(fetched);
            Assert.Equal(6000, fetched.UnitPrice);
            Assert.Equal(5000, fetched.UnitPriceStudio);
            Assert.Equal(4500, fetched.UnitPriceVip);

            // Update
            fetched.UnitPriceStudio = 4800;
            fetched.UnitPriceVip = 4200;
            await prodRepo.UpdateSpecificationAsync(fetched);

            var updated = await prodRepo.GetByIdAsync(created.Id);
            Assert.NotNull(updated);
            Assert.Equal(4800, updated.UnitPriceStudio);
            Assert.Equal(4200, updated.UnitPriceVip);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task BillingService_CalculatesTieredPrices_ForOrders()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"lalab_tier_bill_{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var migrator = new DatabaseMigrator(factory);
            await migrator.MigrateAsync();

            var custRepo = new SqliteCustomerRepository(factory);
            var prodRepo = new SqliteProductRepository(factory);
            var orderRepo = new SqliteOrderRepository(factory);
            var billRepo = new SqliteBillRepository(factory);

            var allSpecs = await prodRepo.GetAllAsync();
            var photoSpec = allSpecs.First(s => s.CanonicalName == "13x18 in");
            photoSpec.UnitPrice = 5000;
            photoSpec.UnitPriceStudio = 4000;
            photoSpec.UnitPriceVip = 3500;
            await prodRepo.UpdateSpecificationAsync(photoSpec);

            var albumSpec = allSpecs.FirstOrDefault(s => s.Category == ProductCategory.Album);
            if (albumSpec != null)
            {
                albumSpec.BasePrice = 400000;
                albumSpec.ExtraSheetPrice = 20000;
                albumSpec.BasePriceStudio = 350000;
                albumSpec.ExtraSheetPriceStudio = 18000;
                albumSpec.BasePriceVip = 300000;
                albumSpec.ExtraSheetPriceVip = 15000;
                await prodRepo.UpdateSpecificationAsync(albumSpec);
            }
            else
            {
                albumSpec = await prodRepo.CreateSpecificationAsync(new PrintSpecification
                {
                    CanonicalName = "Album 20x30 TierTest",
                    Category = ProductCategory.Album,
                    BillingMethod = BillingMethod.AlbumBasePlusExtra,
                    IncludedSheets = 10,
                    BasePrice = 400000,
                    ExtraSheetPrice = 20000,
                    BasePriceStudio = 350000,
                    ExtraSheetPriceStudio = 18000,
                    BasePriceVip = 300000,
                    ExtraSheetPriceVip = 15000
                });
            }

            var retailCust = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Khách Lẻ A", PriceTier = PriceTier.Retail });
            var studioCust = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio B", PriceTier = PriceTier.Studio });
            var vipCust = await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Đại Lý C", PriceTier = PriceTier.Vip });

            var billingService = new BillingService(orderRepo, billRepo, prodRepo, custRepo);

            // 1. Retail order: 10 photo prints -> 50,000
            var order1 = await CreateAndSaveOrderAsync(orderRepo, 1, retailCust.Id, photoSpec.Id, 10);
            var bill1 = await billingService.CalculateBillForOrderAsync(order1.Id);
            Assert.Equal(50000, bill1.Subtotal);
            Assert.Equal(5000, bill1.Lines[0].UnitPrice);

            // 2. Studio order: 10 photo prints -> 40,000
            var order2 = await CreateAndSaveOrderAsync(orderRepo, 2, studioCust.Id, photoSpec.Id, 10);
            var bill2 = await billingService.CalculateBillForOrderAsync(order2.Id);
            Assert.Equal(40000, bill2.Subtotal);
            Assert.Equal(4000, bill2.Lines[0].UnitPrice);

            // 3. VIP order: 10 photo prints -> 35,000
            var order3 = await CreateAndSaveOrderAsync(orderRepo, 3, vipCust.Id, photoSpec.Id, 10);
            var bill3 = await billingService.CalculateBillForOrderAsync(order3.Id);
            Assert.Equal(35000, bill3.Subtotal);
            Assert.Equal(3500, bill3.Lines[0].UnitPrice);

            // 4. VIP Album: 12 sheets -> 300,000 + 2 * 15,000 = 330,000
            var order4 = await CreateAndSaveOrderAsync(orderRepo, 4, vipCust.Id, albumSpec.Id, 12);
            var bill4 = await billingService.CalculateBillForOrderAsync(order4.Id);
            Assert.Equal(330000, bill4.Subtotal);
            Assert.Equal(300000, bill4.Lines[0].BasePriceSnapshot);
            Assert.Equal(15000, bill4.Lines[0].ExtraSheetPriceSnapshot);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task PriceListViewModel_CreateSpecification_WithTieredPrices_SavesCorrectly()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"lalab_vm_tier_test_{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            var migrator = new DatabaseMigrator(factory);
            await migrator.MigrateAsync();

            var prodRepo = new SqliteProductRepository(factory);
            var vm = new PriceListViewModel(prodRepo);
            await vm.LoadSpecificationsAsync();

            // Set inputs with 3 price tiers
            vm.NewCategory = ProductCategory.PhotoPrint;
            vm.NewSpecName = "15x21 In Test";
            vm.NewSpecPrice = 10000;
            vm.NewSpecPriceStudio = 8000;
            vm.NewSpecPriceVip = 7000;

            Assert.True(vm.IsCreatingPhotoPrint);
            Assert.False(vm.IsCreatingAlbum);

            await vm.CreateSpecificationCommand.ExecuteAsync(null);

            // Verify form fields reset
            Assert.Equal(string.Empty, vm.NewSpecName);
            Assert.Equal(5000, vm.NewSpecPrice);
            Assert.Equal(0, vm.NewSpecPriceStudio);
            Assert.Equal(0, vm.NewSpecPriceVip);

            // Verify saved in repository
            var saved = vm.Specifications.FirstOrDefault(s => s.CanonicalName == "15x21 In Test");
            Assert.NotNull(saved);
            Assert.Equal(10000, saved.UnitPrice);
            Assert.Equal(8000, saved.UnitPriceStudio);
            Assert.Equal(7000, saved.UnitPriceVip);
            Assert.Equal(saved.Id, vm.SelectedSpec?.Id);

            // Test fallback behavior when studio/vip are 0
            vm.NewSpecName = "35x50 Retail Only Test";
            vm.NewSpecPrice = 25000;
            vm.NewSpecPriceStudio = 0;
            vm.NewSpecPriceVip = 0;

            await vm.CreateSpecificationCommand.ExecuteAsync(null);

            var saved2 = vm.Specifications.FirstOrDefault(s => s.CanonicalName == "35x50 Retail Only Test");
            Assert.NotNull(saved2);
            Assert.Equal(25000, saved2.UnitPrice);
            Assert.Null(saved2.UnitPriceStudio);
            Assert.Null(saved2.UnitPriceVip);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    private static async Task<Order> CreateAndSaveOrderAsync(IOrderRepository orderRepo, int seq, long customerId, long specId, int printCount)
    {
        var order = new Order
        {
            WorkDate = "2026-10-01",
            CustomerId = customerId,
            OriginalFolderName = $"Order_{seq}",
            RelativePath = $"2026-10-01/Order_{seq}",
            Status = OrderStatus.Ready,
            Items = new List<OrderItemScan>
            {
                new()
                {
                    PrintSpecificationId = specId,
                    SpecificationFolderName = "PrintSpec",
                    PrintFolderStatus = PrintFolderResolutionStatus.Resolved,
                    PrintCount = printCount,
                    SourceCount = printCount,
                    BillQuantity = printCount
                }
            }
        };
        await orderRepo.SaveOrderAsync(order, new ScanSnapshot { CompletedAt = DateTimeOffset.UtcNow });
        return order;
    }
}
