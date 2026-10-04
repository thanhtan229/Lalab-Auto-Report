using System.Threading.Tasks;
using FluentAssertions;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.UI.ViewModels;
using Xunit;

namespace LalabAutoReport.Tests;

public class TabNavigationUiTests
{
    private class MockSettingsRepo : ISettingsRepository
    {
        public AppSettings CurrentSettings { get; set; } = new() { RootFolder = @"C:\FakeRoot" };
        public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentSettings);
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            CurrentSettings = settings;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void MainViewModel_InitialActiveTab_IsDashboard()
    {
        var settingsRepo = new MockSettingsRepo();
        var vm = new MainViewModel(null!, null!, null!, null!, null!, null!, settingsRepo);

        vm.ActiveTab.Should().Be("Dashboard");
        vm.IsDashboardActive.Should().BeTrue();
        vm.IsInvoicesActive.Should().BeFalse();
        vm.IsReportsActive.Should().BeFalse();
        vm.IsCustomersActive.Should().BeFalse();
        vm.IsPriceListActive.Should().BeFalse();
        vm.IsSettingsActive.Should().BeFalse();
    }

    [Fact]
    public void MainViewModel_SettingActiveProperty_SwitchesTabProperly()
    {
        var settingsRepo = new MockSettingsRepo();
        var vm = new MainViewModel(null!, null!, null!, null!, null!, null!, settingsRepo);

        // Switch to Invoices via property
        vm.IsInvoicesActive = true;
        vm.ActiveTab.Should().Be("Invoices");
        vm.IsDashboardActive.Should().BeFalse();
        vm.IsInvoicesActive.Should().BeTrue();
        vm.IsReportsActive.Should().BeFalse();

        // Switch to Reports via property
        vm.IsReportsActive = true;
        vm.ActiveTab.Should().Be("Reports");
        vm.IsDashboardActive.Should().BeFalse();
        vm.IsInvoicesActive.Should().BeFalse();
        vm.IsReportsActive.Should().BeTrue();
        vm.IsCustomersActive.Should().BeFalse();
        vm.IsPriceListActive.Should().BeFalse();
        vm.IsSettingsActive.Should().BeFalse();

        // Switch to Customers via property
        vm.IsCustomersActive = true;
        vm.ActiveTab.Should().Be("Customers");
        vm.IsCustomersActive.Should().BeTrue();
        vm.IsReportsActive.Should().BeFalse();

        // Switch to PriceList via property
        vm.IsPriceListActive = true;
        vm.ActiveTab.Should().Be("PriceList");
        vm.IsPriceListActive.Should().BeTrue();
        vm.IsCustomersActive.Should().BeFalse();

        // Switch to Settings via property
        vm.IsSettingsActive = true;
        vm.ActiveTab.Should().Be("Settings");
        vm.IsSettingsActive.Should().BeTrue();
        vm.IsPriceListActive.Should().BeFalse();

        // Switch back to Dashboard
        vm.IsDashboardActive = true;
        vm.ActiveTab.Should().Be("Dashboard");
        vm.IsDashboardActive.Should().BeTrue();
        vm.IsSettingsActive.Should().BeFalse();
    }

    [Fact]
    public void MainWindowXaml_SidebarTabs_HaveExpectedNamesAndOrder()
    {
        var xamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.AppDomain.CurrentDomain.BaseDirectory,
            "../../../../src/LalabAutoReport.UI/MainWindow.xaml"));

        if (!System.IO.File.Exists(xamlPath))
        {
            // Fallback for different build output directory depths
            xamlPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                System.AppContext.BaseDirectory,
                "../../../../../src/LalabAutoReport.UI/MainWindow.xaml"));
        }

        System.IO.File.Exists(xamlPath).Should().BeTrue($"MainWindow.xaml should exist at {xamlPath}");
        var content = System.IO.File.ReadAllText(xamlPath);

        // Find the MainSidebarTabs radio buttons
        var pattern = @"Content=""([^""]+)""\s+IsChecked=""\{Binding (Is[A-Za-z]+Active)\}""";
        var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern);

        matches.Count.Should().Be(6);
        matches[0].Groups[1].Value.Should().Be("ĐƠN HÀNG");
        matches[0].Groups[2].Value.Should().Be("IsDashboardActive");

        matches[1].Groups[1].Value.Should().Be("HÓA ĐƠN");
        matches[1].Groups[2].Value.Should().Be("IsInvoicesActive");

        matches[2].Groups[1].Value.Should().Be("KHÁCH HÀNG");
        matches[2].Groups[2].Value.Should().Be("IsCustomersActive");

        matches[3].Groups[1].Value.Should().Be("BẢNG GIÁ");
        matches[3].Groups[2].Value.Should().Be("IsPriceListActive");

        matches[4].Groups[1].Value.Should().Be("BÁO CÁO");
        matches[4].Groups[2].Value.Should().Be("IsReportsActive");

        matches[5].Groups[1].Value.Should().Be("CÀI ĐẶT");
        matches[5].Groups[2].Value.Should().Be("IsSettingsActive");
    }
}
