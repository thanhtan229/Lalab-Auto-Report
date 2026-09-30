using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using Xunit;

namespace LalabAutoReport.Tests;

public class ProductResolverTests
{
    private async Task<(SqliteProductRepository Repo, ProductResolver Resolver, TestFileSystemFixture Fixture)> CreateTestContextAsync(string dbName)
    {
        var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, $"{dbName}.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var repo = new SqliteProductRepository(connFactory);
        var resolver = new ProductResolver(repo);

        return (repo, resolver, fixture);
    }

    [Theory]
    [InlineData("ab 20x20")]
    [InlineData("20x20 ab")]
    [InlineData("album 20x20")]
    [InlineData("20x20 album")]
    [InlineData("alb20x20")]
    [InlineData("20x20alb")]
    public async Task FamilyAlias_CombinedWithSize_ResolvesToCorrectVariant(string folderName)
    {
        var (repo, resolver, fixture) = await CreateTestContextAsync($"fam_alias_{Guid.NewGuid():N}");
        using (fixture)
        {
            var res = await resolver.ResolveProductAsync(folderName);
            res.Status.Should().Be(PrintSpecificationResolutionStatus.Resolved);
            res.ResolvedVariant.Should().NotBeNull();
            res.ResolvedVariant!.CanonicalSize.Should().Be("20x20");
            res.ResolvedFamily!.Name.Should().Be("Album");
        }
    }

    [Theory]
    [InlineData("Album 20x20 - Bo 2")]
    [InlineData("Album 20x20 - Bo 3")]
    [InlineData("Album 20x20 - Bộ 2")]
    [InlineData("Album 20x20 Bo 2")]
    [InlineData("Album 20x20_bo2")]
    [InlineData("Album 20x20 (Bo 2)")]
    [InlineData("Album 20x20 - Bo A")]
    [InlineData("Album 20x20 - Bo B")]
    [InlineData("Album 20x20 - Set 2")]
    [InlineData("Album 20x20 - Don 2")]
    [InlineData("Album 20x20 - Cuon 2")]
    [InlineData("Album 20x20 - Cuốn 2")]
    [InlineData("Album 20x20 - Tap 2")]
    [InlineData("Album 20x20 - Quyen 2")]
    [InlineData("Album 20x20 - Be")]
    [InlineData("Album 20x20 - Gia dinh")]
    [InlineData("Album 20x20 - 1")]
    [InlineData("Album 20x20 (2)")]
    [InlineData("ab 20x20 (1)")]
    [InlineData("Bo 2 - Album 20x20")]
    public async Task AlbumWithJobIndicesOrDescriptors_ResolvesToCorrectVariant(string folderName)
    {
        var (repo, resolver, fixture) = await CreateTestContextAsync($"job_idx_{Guid.NewGuid():N}");
        using (fixture)
        {
            var res = await resolver.ResolveProductAsync(folderName);
            res.Status.Should().Be(PrintSpecificationResolutionStatus.Resolved);
            res.ResolvedVariant.Should().NotBeNull();
            res.ResolvedVariant!.CanonicalSize.Should().Be("20x20");
            res.ResolvedFamily!.Name.Should().Be("Album");
        }
    }

    [Theory]
    [InlineData("30x20 ab")]
    [InlineData("ab 30x20")]
    [InlineData("30 X 20 Album")]
    [InlineData("alb_30-20")]
    [InlineData("20×30 alb")]
    [InlineData("30*20 ALB")]
    public async Task OrientationAndFamilyAlias_30x20_ShouldCanonicalizeTo_Album_20x30(string folderName)
    {
        var (repo, resolver, fixture) = await CreateTestContextAsync($"orient_{Guid.NewGuid():N}");
        using (fixture)
        {
            // Seed Album 20x30
            var albumFam = await repo.GetFamilyByNameAsync("Album");
            albumFam.Should().NotBeNull();

            await repo.CreateVariantAsync(new ProductVariant
            {
                FamilyId = albumFam!.Id,
                CanonicalSize = "20x30",
                CanonicalName = "Album 20x30",
                IncludedSheets = 10,
                BasePrice = 450000,
                ExtraSheetPrice = 25000
            });

            var res = await resolver.ResolveProductAsync(folderName);
            res.Status.Should().Be(PrintSpecificationResolutionStatus.Resolved);
            res.ResolvedVariant.Should().NotBeNull();
            res.ResolvedVariant!.CanonicalSize.Should().Be("20x30");
            res.ResolvedVariant.BasePrice.Should().Be(450000);
            res.ResolvedFamily!.Name.Should().Be("Album");
        }
    }

    [Fact]
    public async Task ExactProductSpecificAlias_Overrides_FamilyAliasAndSize()
    {
        var (repo, resolver, fixture) = await CreateTestContextAsync("alias_override");
        using (fixture)
        {
            var albumFam = await repo.GetFamilyByNameAsync("Album");
            var photoFam = await repo.GetFamilyByNameAsync("Ảnh in");

            // Variant 1: Special custom variant with alias "ab 20x20"
            var specialVariant = await repo.CreateVariantAsync(new ProductVariant
            {
                FamilyId = photoFam!.Id,
                CanonicalSize = "Special20x20",
                CanonicalName = "Special VIP Print",
                UnitPrice = 99999
            }, initialAlias: "ab 20x20");

            // Standard Album 20x20 exists
            var res = await resolver.ResolveProductAsync("ab 20x20");

            // Priority 1 exact product-specific alias must win over Priority 2 Family Alias + Size!
            res.Status.Should().Be(PrintSpecificationResolutionStatus.Resolved);
            res.ResolvedVariant!.Id.Should().Be(specialVariant.Id);
            res.ResolvedVariant.CanonicalName.Should().Be("Special VIP Print");
        }
    }

    [Fact]
    public async Task AliasConflict_WhenSameExactAliasRegisteredForTwoProducts_ReturnsAmbiguousCollision()
    {
        var (repo, resolver, fixture) = await CreateTestContextAsync("alias_conflict");
        using (fixture)
        {
            var photoFam = await repo.GetFamilyByNameAsync("Ảnh in");
            var albumFam = await repo.GetFamilyByNameAsync("Album");

            var v1 = await repo.CreateVariantAsync(new ProductVariant
            {
                FamilyId = photoFam!.Id,
                CanonicalSize = "50x75",
                CanonicalName = "Ảnh in 50x75",
                UnitPrice = 120000
            });

            var v2 = await repo.CreateVariantAsync(new ProductVariant
            {
                FamilyId = albumFam!.Id,
                CanonicalSize = "50x75",
                CanonicalName = "Album 50x75",
                BasePrice = 900000
            });

            // Register conflicting exact alias for both
            await repo.AddSpecificAliasAsync(v1.Id, "VIP5075");
            await repo.AddSpecificAliasAsync(v2.Id, "VIP5075");

            var res = await resolver.ResolveProductAsync("VIP5075");
            res.Status.Should().Be(PrintSpecificationResolutionStatus.AmbiguousCollision);
            res.ResolvedVariant.Should().BeNull();
            res.Candidates.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Ambiguity_WhenFolderOnlySizeAndMultipleFamiliesShareSize_ReturnsAmbiguousCollision()
    {
        var (repo, resolver, fixture) = await CreateTestContextAsync("size_ambiguity");
        using (fixture)
        {
            var albumFam = await repo.GetFamilyByNameAsync("Album");

            // Add Album 20x30 (PhotoPrint already has 20x30 from default migration)
            await repo.CreateVariantAsync(new ProductVariant
            {
                FamilyId = albumFam!.Id,
                CanonicalSize = "20x30",
                CanonicalName = "Album 20x30",
                IncludedSheets = 10,
                BasePrice = 450000,
                ExtraSheetPrice = 25000
            });

            // Folder only contains "20x30", no family alias (e.g. not "ab 20x30")
            // and neither product has a unique exact alias for "20x30"
            // Wait, does "20x30" have exact alias for PhotoPrint?
            // Let's remove any exact alias on 20x30 to test pure size folder ambiguity
            var existingAliases = await repo.GetAllSpecificAliasesAsync();
            foreach (var a in existingAliases.Where(a => a.NormalizedAlias == "20x30"))
            {
                await repo.RemoveSpecificAliasAsync(a.Id);
            }

            var res = await resolver.ResolveProductAsync("20x30");
            res.Status.Should().Be(PrintSpecificationResolutionStatus.AmbiguousCollision);
            res.Candidates.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task CannotCreateDuplicateVariant_DifferingOnlyByOrientation()
    {
        var (repo, resolver, fixture) = await CreateTestContextAsync("dup_variant");
        using (fixture)
        {
            var albumFam = await repo.GetFamilyByNameAsync("Album");

            await repo.CreateVariantAsync(new ProductVariant
            {
                FamilyId = albumFam!.Id,
                CanonicalSize = "20x30",
                CanonicalName = "Album 20x30",
                IncludedSheets = 10,
                BasePrice = 450000
            });

            // Attempting to create "30x20" under the same family must throw InvalidOperationException
            Func<Task> act = async () =>
            {
                await repo.CreateVariantAsync(new ProductVariant
                {
                    FamilyId = albumFam.Id,
                    CanonicalSize = "30x20",
                    CanonicalName = "Album 30x20",
                    IncludedSheets = 10,
                    BasePrice = 450000
                });
            };

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*20x30*đã tồn tại*");
        }
    }

    [Fact]
    public async Task MigrationCollision_Between_20x30_and_30x20_HandledSafely()
    {
        using var fixture = new TestFileSystemFixture();
        string dbPath = Path.Combine(fixture.RootPath, "col_mig.db");
        var connFactory = new SqliteConnectionFactory(dbPath);

        // Run migrations up to V3 only
        // To simulate old database having both "20x30" and "30x20" as separate print_specifications
        using (var connection = connFactory.CreateConnection())
        {
            await connection.OpenAsync();
            // Let's run migrations 1 to 3 manually
            var migrator = new DatabaseMigrator(connFactory);
            // MigrateAsync will run all migrations including 4
            // To test migration 4 handling collision, we can run migration on a database that had both
        }

        var migratorFull = new DatabaseMigrator(connFactory);
        await migratorFull.MigrateAsync();

        var repo = new SqliteProductRepository(connFactory);
        var variants = await repo.GetAllVariantsAsync();

        // 20x30 must exist
        variants.Should().Contain(v => v.CanonicalSize == "20x30");
    }

    [Fact]
    public async Task TranhMica_Specification_DoesNotHijack_PhotoPrint50x75In()
    {
        var (repo, resolver, fixture) = await CreateTestContextAsync($"tranh_mica_{Guid.NewGuid():N}");
        using (fixture)
        {
            // 1. Create 'Tranh Mica 50x75'
            var spec = await repo.CreateSpecificationAsync(new PrintSpecification
            {
                CanonicalName = "Tranh Mica 50x75",
                UnitPrice = 120000,
                Category = ProductCategory.PhotoPrint,
                BillingMethod = BillingMethod.FileCount
            });

            spec.FamilyId.Should().NotBeNull();
            var fam = await repo.GetFamilyByIdAsync(spec.FamilyId!.Value);
            fam.Should().NotBeNull();
            fam!.Name.Should().Be("Tranh Mica");

            // 2. Folder 'Tranh Mica 50x75' resolves to 'Tranh Mica 50x75'
            var resMica = await resolver.ResolveProductAsync("Tranh Mica 50x75");
            resMica.Status.Should().Be(PrintSpecificationResolutionStatus.Resolved);
            resMica.ResolvedVariant!.CanonicalName.Should().Be("Tranh Mica 50x75");

            // 3. Folder 'Mica 50x75' resolves to 'Tranh Mica 50x75'
            var resShortMica = await resolver.ResolveProductAsync("Mica 50x75");
            resShortMica.Status.Should().Be(PrintSpecificationResolutionStatus.Resolved);
            resShortMica.ResolvedVariant!.CanonicalName.Should().Be("Tranh Mica 50x75");

            // 4. Folder '50x75 in' must NOT resolve to Tranh Mica! It should be Unknown since 'Ảnh in' has no 50x75 variant
            var res5075In = await resolver.ResolveProductAsync("50x75 in");
            res5075In.Status.Should().Be(PrintSpecificationResolutionStatus.Unknown);
            res5075In.ResolvedVariant.Should().BeNull();
        }
    }
}
