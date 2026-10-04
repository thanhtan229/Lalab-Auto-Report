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

public class ManageFamilyAliasesViewModelTests
{
    private async Task<(SqliteProductRepository repo, string dbPath)> CreateTestRepoAsync()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"test_family_aliases_{Guid.NewGuid():N}.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();
        var repo = new SqliteProductRepository(connFactory);
        return (repo, dbPath);
    }

    [Fact]
    public async Task LoadFamiliesAsync_LoadsAllFamiliesWithIconsAndDescriptions()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            vm.FamilyCards.Should().NotBeEmpty();
            var albumCard = vm.FamilyCards.FirstOrDefault(c => c.Category == ProductCategory.Album || c.Name.Contains("Album"));
            albumCard.Should().NotBeNull();
            albumCard!.DisplayIcon.Should().Be("📖");
            albumCard.Aliases.Should().Contain(a => a.AliasText.ToLower().Contains("ab") || a.AliasText.ToLower().Contains("album"));

            var photoCard = vm.FamilyCards.FirstOrDefault(c => c.Category == ProductCategory.PhotoPrint && !c.Name.Contains("Mica"));
            photoCard.Should().NotBeNull();
            photoCard!.DisplayIcon.Should().Be("🖼️");
            photoCard.Aliases.Should().Contain(a => a.AliasText.ToLower().Contains("in") || a.AliasText.ToLower().Contains("anh"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task AddAliasAsync_ValidInput_AddsSuccessfully()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            var albumCard = vm.FamilyCards.First(c => c.Category == ProductCategory.Album);
            albumCard.NewAliasInput = "album_dep";

            await vm.AddAliasAsync(albumCard);

            vm.IsSuccessStatus.Should().BeTrue();
            vm.StatusMessage.Should().Contain("album_dep");

            // Reload and verify persisted
            var reloadedCard = vm.FamilyCards.First(c => c.FamilyId == albumCard.FamilyId);
            reloadedCard.Aliases.Should().Contain(a => a.AliasText == "album_dep");
            albumCard.NewAliasInput.Should().BeEmpty();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task AddAliasAsync_EmptyInput_ShowsError()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            var albumCard = vm.FamilyCards.First();
            albumCard.NewAliasInput = "   ";

            await vm.AddAliasAsync(albumCard);

            vm.IsSuccessStatus.Should().BeFalse();
            vm.StatusMessage.Should().Contain("Vui lòng nhập");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task AddAliasAsync_DuplicateInSameFamily_ShowsError()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            var albumCard = vm.FamilyCards.First(c => c.Category == ProductCategory.Album);
            albumCard.NewAliasInput = "ab"; // already seeded

            await vm.AddAliasAsync(albumCard);

            vm.IsSuccessStatus.Should().BeFalse();
            vm.StatusMessage.Should().Contain("đã có trong dòng");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task AddAliasAsync_DuplicateAcrossFamilies_ShowsCollisionError()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            var albumCard = vm.FamilyCards.First(c => c.Category == ProductCategory.Album);
            albumCard.NewAliasInput = "in"; // 'in' belongs to PhotoPrint family

            await vm.AddAliasAsync(albumCard);

            vm.IsSuccessStatus.Should().BeFalse();
            vm.StatusMessage.Should().Contain("đang được dùng cho dòng");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task RemoveAliasAsync_ValidAlias_RemovesSuccessfully()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            var albumCard = vm.FamilyCards.First(c => c.Category == ProductCategory.Album);
            var aliasToRemove = albumCard.Aliases.First();
            string removedText = aliasToRemove.AliasText;

            await vm.RemoveAliasAsync(aliasToRemove);

            vm.IsSuccessStatus.Should().BeTrue();
            vm.StatusMessage.Should().Contain("Đã xóa alias");

            var reloadedCard = vm.FamilyCards.First(c => c.FamilyId == albumCard.FamilyId);
            reloadedCard.Aliases.Should().NotContain(a => a.Id == aliasToRemove.Id);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task UpdateAliasAsync_ValidInput_UpdatesSuccessfully()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            var albumCard = vm.FamilyCards.First(c => c.Category == ProductCategory.Album);
            var aliasToEdit = albumCard.Aliases.First();

            bool result = await vm.UpdateAliasAsync(aliasToEdit, "cuon_album");

            result.Should().BeTrue();
            vm.IsSuccessStatus.Should().BeTrue();
            vm.StatusMessage.Should().Contain("cuon_album");

            var reloadedCard = vm.FamilyCards.First(c => c.FamilyId == albumCard.FamilyId);
            reloadedCard.Aliases.Should().Contain(a => a.AliasText == "cuon_album");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task UpdateAliasAsync_CollisionWithOtherFamily_ShowsError()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            var albumCard = vm.FamilyCards.First(c => c.Category == ProductCategory.Album);
            var aliasToEdit = albumCard.Aliases.First();

            // Try to rename it to 'in' which belongs to PhotoPrint
            bool result = await vm.UpdateAliasAsync(aliasToEdit, "in");

            result.Should().BeFalse();
            vm.IsSuccessStatus.Should().BeFalse();
            vm.StatusMessage.Should().Contain("đã tồn tại trong dòng");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task CreateFamilyAsync_Valid_CreatesNewFamilyWithAliases()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            bool result = await vm.CreateFamilyAsync(
                "Ảnh in ép plastic",
                ProductCategory.PhotoPrint,
                BillingMethod.FileCount,
                new[] { "ep", "plastic", "epplastic" }
            );

            result.Should().BeTrue();
            vm.IsSuccessStatus.Should().BeTrue();

            var plasticCard = vm.FamilyCards.FirstOrDefault(c => c.Name == "Ảnh in ép plastic");
            plasticCard.Should().NotBeNull();
            plasticCard!.DisplayIcon.Should().Be("📑");
            plasticCard.Aliases.Should().HaveCount(4);
            plasticCard.Aliases.Should().Contain(a => a.AliasText == "ep");
            plasticCard.Aliases.Should().Contain(a => a.AliasText == "Ảnh in ép plastic");
            plasticCard.CanDelete.Should().BeTrue();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task RenameFamilyAsync_Valid_RenamesFamily()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            await vm.CreateFamilyAsync("Bao da", ProductCategory.Other, BillingMethod.FileCount, new[] { "baoda" });
            var card = vm.FamilyCards.First(c => c.Name == "Bao da");

            bool result = await vm.RenameFamilyAsync(card, "Bao da cao cấp");

            result.Should().BeTrue();
            vm.IsSuccessStatus.Should().BeTrue();
            vm.FamilyCards.Should().Contain(c => c.Name == "Bao da cao cấp");
            vm.FamilyCards.Should().NotContain(c => c.Name == "Bao da");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task DeleteFamilyAsync_CustomFamilyWithoutVariants_DeletesSuccessfully()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            await vm.CreateFamilyAsync("Khung ảnh", ProductCategory.PhotoPrint, BillingMethod.FileCount, new[] { "khung" });
            var card = vm.FamilyCards.First(c => c.Name == "Khung ảnh");

            bool result = await vm.DeleteFamilyAsync(card);

            result.Should().BeTrue();
            vm.IsSuccessStatus.Should().BeTrue();
            vm.FamilyCards.Should().NotContain(c => c.Name == "Khung ảnh");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task DeleteFamilyAsync_CoreSystemFamily_PreventsDeletion()
    {
        var (repo, dbPath) = await CreateTestRepoAsync();
        try
        {
            var vm = new ManageFamilyAliasesViewModel(repo);
            await vm.InitializeAsync();

            var albumCard = vm.FamilyCards.First(c => c.Name == "Album");
            albumCard.CanDelete.Should().BeFalse();

            bool result = await vm.DeleteFamilyAsync(albumCard);

            result.Should().BeFalse();
            vm.IsSuccessStatus.Should().BeFalse();
            vm.StatusMessage.Should().Contain("mặc định");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }
}
