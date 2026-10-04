using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using LalabAutoReport.Infrastructure.Windows;
using Xunit;

namespace LalabAutoReport.Tests;

public class PrintProgressWorkflowTests
{
    private readonly PhysicalFileSystemAdapter _fileSystem = new();

    private class TestSettingsRepo : ISettingsRepository
    {
        private AppSettings _settings;
        public TestSettingsRepo(AppSettings settings) => _settings = settings;

        public Task<AppSettings> GetSettingsAsync(System.Threading.CancellationToken ct = default) =>
            Task.FromResult(_settings);

        public Task SaveSettingsAsync(AppSettings settings, System.Threading.CancellationToken ct = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private async Task<(SqliteFolderPrintRepository printRepo,
                        SqliteOrderRepository orderRepo,
                        SqliteCustomerRepository custRepo,
                        SqliteProductRepository productRepo,
                        FolderVisualMarkerService visualService,
                        PrintStatusService printStatusService,
                        ScanService scanner,
                        TestSettingsRepo settingsRepo)>
        CreateTestContextAsync(TestFileSystemFixture fixture)
    {
        string dbPath = Path.Combine(fixture.RootPath, $"test_{Guid.NewGuid():N}.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        var migrator = new DatabaseMigrator(connFactory);
        await migrator.MigrateAsync();

        var printRepo = new SqliteFolderPrintRepository(connFactory);
        var orderRepo = new SqliteOrderRepository(connFactory);
        var billRepo = new SqliteBillRepository(connFactory);
        var custRepo = new SqliteCustomerRepository(connFactory);
        var productRepo = new SqliteProductRepository(connFactory);

        var parser = new FolderStructureParser(_fileSystem, productRepo);
        var printResolver = new PrintFolderResolver(_fileSystem);
        var custResolver = new CustomerResolver(custRepo);
        var specResolver = new PrintSpecificationResolver(productRepo);
        var fingerprintService = new FolderFingerprintService(_fileSystem);

        var settings = new AppSettings
        {
            RootFolder = fixture.RootPath,
            SupportedExtensions = new() { ".jpg", ".jpeg", ".png" },
            EnableIdleScan = false,
            AutoScanStartup = false
        };
        var settingsRepo = new TestSettingsRepo(settings);

        var visualService = new FolderVisualMarkerService();
        var printStatusService = new PrintStatusService(
            printRepo, visualService, orderRepo, settingsRepo);

        var billing = new BillingService(orderRepo, billRepo, productRepo, custRepo);
        var scanner = new ScanService(
            _fileSystem, parser, printResolver, settingsRepo,
            orderRepo, custResolver, specResolver, billRepo, billing,
            null, fingerprintService, printStatusService);

        return (printRepo, orderRepo, custRepo, productRepo, visualService, printStatusService, scanner, settingsRepo);
    }

    [Fact]
    public async Task SubfolderToggle_PartialProgress_TransitionsYellowToBlueToRed_AndReverts()
    {
        using var fixture = new TestFileSystemFixture();
        var (printRepo, orderRepo, custRepo, _, visualService, printService, scanner, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Kim Studio" });
        string today = "2026-10-02";
        string orderRel = $@"{today}\Kim Studio";
        string orderFull = Path.Combine(fixture.RootPath, orderRel);

        // 3 album folders in Kim Studio order
        string sub1 = Path.Combine(orderRel, @"Album Cuoi 25x35\img1.jpg");
        string sub2 = Path.Combine(orderRel, @"Album Prewedding 30x30\img2.jpg");
        string sub3 = Path.Combine(orderRel, @"Album Gia Dinh 20x30\img3.jpg");

        fixture.CreateFile(sub1);
        fixture.CreateFile(sub2);
        fixture.CreateFile(sub3);

        // Initial scan
        var order = await scanner.ScanOrderAsync(orderRel);
        order.Should().NotBeNull();
        order!.Items.Should().HaveCount(3);
        order.PrintProgress.Should().Be(PrintStatus.NotPrinted);
        order.IsPrinted.Should().BeFalse();

        string fullSub1 = Path.Combine(fixture.RootPath, orderRel, "Album Cuoi 25x35");
        string fullSub2 = Path.Combine(fixture.RootPath, orderRel, "Album Prewedding 30x30");
        string fullSub3 = Path.Combine(fixture.RootPath, orderRel, "Album Gia Dinh 20x30");

        // 1. Toggle 1st subfolder: 1/3 -> Parent should become Partial (Blue)
        var res1 = await printService.ToggleStatusAsync(fullSub1);
        res1.IsPrinted.Should().BeTrue();
        res1.NewStatus.Should().Be(PrintStatus.Printed);

        // Verify parent progress
        var pRecord1 = await printRepo.GetByFolderPathAsync(orderFull);
        pRecord1.Should().NotBeNull();
        pRecord1!.Status.Should().Be(PrintStatus.Partial);
        pRecord1.PrintedSubCount.Should().Be(1);
        pRecord1.TotalSubCount.Should().Be(3);

        var updatedOrder1 = await orderRepo.GetOrderByRelativePathAsync(orderRel);
        updatedOrder1!.PrintProgress.Should().Be(PrintStatus.Partial);
        updatedOrder1.IsPrinted.Should().BeFalse();

        // Check desktop.ini of parent folder has blue icon
        string parentDesktopIni = Path.Combine(orderFull, "desktop.ini");
        File.Exists(parentDesktopIni).Should().BeTrue();
        string iniText1 = File.ReadAllText(parentDesktopIni);
        iniText1.Should().Contain("folder_blue.ico");

        // 2. Toggle 2nd subfolder: 2/3 -> Parent remains Partial (Blue)
        var res2 = await printService.ToggleStatusAsync(fullSub2);
        res2.IsPrinted.Should().BeTrue();

        var pRecord2 = await printRepo.GetByFolderPathAsync(orderFull);
        pRecord2!.Status.Should().Be(PrintStatus.Partial);
        pRecord2.PrintedSubCount.Should().Be(2);

        // 3. Toggle 3rd subfolder: 3/3 -> Parent becomes Printed (Red)
        var res3 = await printService.ToggleStatusAsync(fullSub3);
        res3.IsPrinted.Should().BeTrue();

        var pRecord3 = await printRepo.GetByFolderPathAsync(orderFull);
        pRecord3!.Status.Should().Be(PrintStatus.Printed);
        pRecord3.PrintedSubCount.Should().Be(3);

        var updatedOrder3 = await orderRepo.GetOrderByRelativePathAsync(orderRel);
        updatedOrder3!.PrintProgress.Should().Be(PrintStatus.Printed);
        updatedOrder3.IsPrinted.Should().BeTrue();

        string iniText3 = File.ReadAllText(parentDesktopIni);
        iniText3.Should().Contain("folder_printed.ico");

        // 4. Reverse: Unmark 3rd subfolder -> Parent degrades back to Partial (Blue) (2/3)
        var resUnmark3 = await printService.ToggleStatusAsync(fullSub3);
        resUnmark3.IsPrinted.Should().BeFalse();

        var pRecordDegraded = await printRepo.GetByFolderPathAsync(orderFull);
        pRecordDegraded!.Status.Should().Be(PrintStatus.Partial);
        pRecordDegraded.PrintedSubCount.Should().Be(2);

        var updatedOrderDegraded = await orderRepo.GetOrderByRelativePathAsync(orderRel);
        updatedOrderDegraded!.PrintProgress.Should().Be(PrintStatus.Partial);
        updatedOrderDegraded.IsPrinted.Should().BeFalse();

        string iniTextDegraded = File.ReadAllText(parentDesktopIni);
        iniTextDegraded.Should().Contain("folder_blue.ico");

        // 5. Unmark 1st and 2nd subfolders -> Parent reverts to NotPrinted (Yellow) (0/3)
        await printService.ToggleStatusAsync(fullSub1);
        await printService.ToggleStatusAsync(fullSub2);

        var pRecordReverted = await printRepo.GetByFolderPathAsync(orderFull);
        pRecordReverted!.Status.Should().Be(PrintStatus.NotPrinted);
        pRecordReverted.PrintedSubCount.Should().Be(0);

        var updatedOrderReverted = await orderRepo.GetOrderByRelativePathAsync(orderRel);
        updatedOrderReverted!.PrintProgress.Should().Be(PrintStatus.NotPrinted);
        updatedOrderReverted.IsPrinted.Should().BeFalse();

        // desktop.ini should no longer specify an icon or be removed
        if (File.Exists(parentDesktopIni))
        {
            string cleanIni = File.ReadAllText(parentDesktopIni);
            cleanIni.Should().NotContain("folder_blue.ico");
            cleanIni.Should().NotContain("folder_printed.ico");
        }
    }

    [Fact]
    public async Task ParentOrderToggle_CascadesToAllSubfolders_AndReverts()
    {
        using var fixture = new TestFileSystemFixture();
        var (printRepo, orderRepo, custRepo, _, _, printService, scanner, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Studio Mai" });
        string today = "2026-10-02";
        string orderRel = $@"{today}\Studio Mai";
        string orderFull = Path.Combine(fixture.RootPath, orderRel);

        fixture.CreateFile(Path.Combine(orderRel, @"Album A\img1.jpg"));
        fixture.CreateFile(Path.Combine(orderRel, @"Album B\img2.jpg"));

        await scanner.ScanOrderAsync(orderRel);

        string subA = Path.Combine(orderFull, "Album A");
        string subB = Path.Combine(orderFull, "Album B");

        // 1. Toggle parent folder to Printed -> Cascades to all children
        var cascadeRes = await printService.ToggleStatusAsync(orderFull);
        cascadeRes.IsPrinted.Should().BeTrue();
        cascadeRes.NewStatus.Should().Be(PrintStatus.Printed);
        cascadeRes.PrintedSubCount.Should().Be(2);
        cascadeRes.TotalSubCount.Should().Be(2);

        // Check both subfolders are printed
        (await printService.IsFolderPrintedAsync(subA)).Should().BeTrue();
        (await printService.IsFolderPrintedAsync(subB)).Should().BeTrue();

        var orderAfterCascade = await orderRepo.GetOrderByRelativePathAsync(orderRel);
        orderAfterCascade!.IsPrinted.Should().BeTrue();
        orderAfterCascade.PrintProgress.Should().Be(PrintStatus.Printed);
        orderAfterCascade.Items.All(i => i.IsPrinted).Should().BeTrue();

        // 2. Toggle parent folder back to Unprinted -> Cascades unmark to all children
        var uncascadeRes = await printService.ToggleStatusAsync(orderFull);
        uncascadeRes.IsPrinted.Should().BeFalse();
        uncascadeRes.NewStatus.Should().Be(PrintStatus.NotPrinted);
        uncascadeRes.PrintedSubCount.Should().Be(0);

        (await printService.IsFolderPrintedAsync(subA)).Should().BeFalse();
        (await printService.IsFolderPrintedAsync(subB)).Should().BeFalse();

        var orderAfterUncascade = await orderRepo.GetOrderByRelativePathAsync(orderRel);
        orderAfterUncascade!.IsPrinted.Should().BeFalse();
        orderAfterUncascade.PrintProgress.Should().Be(PrintStatus.NotPrinted);
        orderAfterUncascade.Items.All(i => !i.IsPrinted).Should().BeTrue();
    }

    [Fact]
    public async Task SingleItemOrder_DirectTransition_YellowToRed()
    {
        using var fixture = new TestFileSystemFixture();
        var (printRepo, orderRepo, custRepo, _, _, printService, scanner, _) = await CreateTestContextAsync(fixture);

        await custRepo.CreateCustomerAsync(new Customer { CanonicalName = "Anh Nam" });
        string today = "2026-10-02";
        string orderRel = $@"{today}\Anh Nam";
        string orderFull = Path.Combine(fixture.RootPath, orderRel);

        fixture.CreateFile(Path.Combine(orderRel, @"13x18 in\img1.jpg"));

        await scanner.ScanOrderAsync(orderRel);

        string subFolder = Path.Combine(orderFull, "13x18 in");

        // Toggle single item
        var res = await printService.ToggleStatusAsync(subFolder);
        res.IsPrinted.Should().BeTrue();
        res.NewStatus.Should().Be(PrintStatus.Printed);
        res.PrintedSubCount.Should().Be(1);
        res.TotalSubCount.Should().Be(1);

        var order = await orderRepo.GetOrderByRelativePathAsync(orderRel);
        order!.IsPrinted.Should().BeTrue();
        order.PrintProgress.Should().Be(PrintStatus.Printed);

        string parentDesktopIni = Path.Combine(orderFull, "desktop.ini");
        File.Exists(parentDesktopIni).Should().BeTrue();
        File.ReadAllText(parentDesktopIni).Should().Contain("folder_printed.ico");
    }

    [Fact]
    public void FolderVisualMarkerService_BlueIconGeneration_CreatesValidIcoFile()
    {
        var visualService = new FolderVisualMarkerService();
        string blueIconPath = visualService.GetOrExtractPartialFolderIconPath();

        File.Exists(blueIconPath).Should().BeTrue();
        new FileInfo(blueIconPath).Length.Should().BeGreaterThan(50);
        Path.GetExtension(blueIconPath).Should().Be(".ico");
    }

    [Fact]
    public void FolderVisualMarkerService_ApplyVisualMarker_HandlesAllColorsCorrectly()
    {
        using var fixture = new TestFileSystemFixture();
        var visualService = new FolderVisualMarkerService();

        string folder = Path.Combine(fixture.RootPath, "TestFolder");
        Directory.CreateDirectory(folder);

        // 1. Blue Partial
        visualService.ApplyVisualMarker(folder, VisualFolderColor.BluePartial);
        string iniPath = Path.Combine(folder, "desktop.ini");
        File.Exists(iniPath).Should().BeTrue();
        File.ReadAllText(iniPath).Should().Contain("folder_blue.ico");

        // 2. Red Printed
        visualService.ApplyVisualMarker(folder, VisualFolderColor.RedPrinted);
        File.ReadAllText(iniPath).Should().Contain("folder_printed.ico");

        // 3. Default Yellow
        visualService.ApplyVisualMarker(folder, VisualFolderColor.DefaultYellow);
        if (File.Exists(iniPath))
        {
            string ini = File.ReadAllText(iniPath);
            ini.Should().NotContain("IconResource=");
        }
    }
}
