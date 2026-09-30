using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.FileSystem;
using Xunit;

namespace LalabAutoReport.Tests;

public class PrintFolderResolverTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();
    private readonly HashSet<string> _supportedExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp", ".heic"
    };

    [Fact]
    public void Scenario1_DirectImagesInProductFolder_ShouldResolve_ProductFolderItself()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\img2.jpg");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts);

        result.Status.Should().Be(PrintFolderResolutionStatus.Resolved);
        result.SelectedPrintFolderFullPath.Should().Be(specDir);
        result.PrintCount.Should().Be(2);
    }

    [Fact]
    public void Scenario1b_NoImagesAnywhere_ShouldReturn_NoPrintFolder()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\notes.txt");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts);

        result.Status.Should().Be(PrintFolderResolutionStatus.NoPrintFolder);
        result.SelectedPrintFolderFullPath.Should().BeNull();
        result.PrintCount.Should().BeNull();
    }

    [Fact]
    public void Scenario2_OneRetouchLevel_ShouldResolve_SingleLeaf()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\source1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\source2.jpg");

        string retouchDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\retouch");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\print1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch\print2.jpg");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts);

        result.Status.Should().Be(PrintFolderResolutionStatus.Resolved);
        result.SelectedPrintFolderFullPath.Should().Be(retouchDir);
        result.PrintCount.Should().Be(2);
    }

    [Fact]
    public void Scenario3_MultipleLinearRetouchLevels_ShouldResolve_DeepestLeaf()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\source1.jpg");

        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\sua-lan-1\edit1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\sua-lan-1\sua-lan-2\edit2.jpg");
        string finalDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\sua-lan-1\sua-lan-2\final-in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\sua-lan-1\sua-lan-2\final-in\f1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\sua-lan-1\sua-lan-2\final-in\f2.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\sua-lan-1\sua-lan-2\final-in\f3.jpg");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts);

        result.Status.Should().Be(PrintFolderResolutionStatus.Resolved);
        result.SelectedPrintFolderFullPath.Should().Be(finalDir);
        result.PrintCount.Should().Be(3);
    }

    [Fact]
    public void Scenario4_AmbiguousCompetingLeafFolders_ShouldReturn_AmbiguousPrintFolder()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\source1.jpg");

        string branchA = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\retouch-a");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch-a\imgA1.jpg");

        string branchB = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\retouch-b");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch-b\imgB1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch-b\imgB2.jpg");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts);

        result.Status.Should().Be(PrintFolderResolutionStatus.AmbiguousPrintFolder);
        result.SelectedPrintFolderFullPath.Should().BeNull();
        result.PrintCount.Should().BeNull();
        result.CandidatePrintFolderFullPaths.Should().HaveCount(2);
        result.CandidatePrintFolderFullPaths.Should().Contain(branchA);
        result.CandidatePrintFolderFullPaths.Should().Contain(branchB);
    }

    [Fact]
    public void Scenario5_SupportedAndUnsupportedMix_ShouldOnlyCountSupported()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\img1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\img2.PNG");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\img3.TIF");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\notes.txt");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\work.psd");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\Thumbs.db");

        string printDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\print");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\print\final1.webp");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\print\final2.HEIC");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\print\archive.zip");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts);

        result.Status.Should().Be(PrintFolderResolutionStatus.Resolved);
        result.SelectedPrintFolderFullPath.Should().Be(printDir);
        result.PrintCount.Should().Be(2); // only webp and heic
    }

    [Fact]
    public void Scenario6_EmptyRetouchSubfolder_ShouldNotBeConsideredImageBearing()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\source1.jpg");

        fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\empty-folder");
        string activeDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\active-print");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\active-print\img1.jpg");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts);

        result.Status.Should().Be(PrintFolderResolutionStatus.Resolved);
        result.SelectedPrintFolderFullPath.Should().Be(activeDir);
        result.PrintCount.Should().Be(1);
    }

    [Fact]
    public void Scenario7_PreviouslySelectedPrintFolder_ShouldBeReusedIfExisting()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\source1.jpg");

        string branchA = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\branchA");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\branchA\img1.jpg");

        fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\branchB");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\branchB\img2.jpg");

        string relativeBranchA = Path.GetRelativePath(fixture.RootPath, branchA);

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts, relativeBranchA);

        result.Status.Should().Be(PrintFolderResolutionStatus.Resolved);
        result.SelectedPrintFolderFullPath.Should().Be(branchA);
        result.PrintCount.Should().Be(1);
    }

    [Fact]
    public void Scenario8_EmptySourceFolder_WithPrintImages_ShouldReportZeroSourceAndValidPrint()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        // No source images in specDir

        string printDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\print");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\print\img1.jpg");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\print\img2.jpg");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts);

        result.Status.Should().Be(PrintFolderResolutionStatus.Resolved);
        result.SelectedPrintFolderFullPath.Should().Be(printDir);
        result.PrintCount.Should().Be(2);
    }

    [Fact]
    public void Scenario9_EmptyFinalFolder_ShouldNotBeConsideredImageBearing()
    {
        using var fixture = new TestFileSystemFixture();
        string specDir = fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in");
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\source1.jpg");

        // Intermediate has images, but leaf is empty
        fixture.CreateFile(@"2026-09-28\Van An\13x18 in\retouch1\img1.jpg");
        fixture.CreateDirectory(@"2026-09-28\Van An\13x18 in\retouch1\empty_leaf");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(specDir, fixture.RootPath, _supportedExts);

        // Since empty_leaf has no images, retouch1 is the image-bearing leaf!
        result.Status.Should().Be(PrintFolderResolutionStatus.Resolved);
        result.SelectedPrintFolderFullPath.Should().Be(Path.Combine(specDir, "retouch1"));
        result.PrintCount.Should().Be(1);
    }

    [Fact]
    public void Scenario10_SpecFolderDisappears_ShouldReturn_NoPrintFolder_WithError()
    {
        using var fixture = new TestFileSystemFixture();
        string nonexistent = Path.Combine(fixture.RootPath, "nonexistent");

        var resolver = new PrintFolderResolver(_fileSystem);
        var result = resolver.ResolvePrintFolder(nonexistent, fixture.RootPath, _supportedExts);

        result.Status.Should().Be(PrintFolderResolutionStatus.NoPrintFolder);
        result.ErrorMessage.Should().Contain("does not exist");
    }
}
