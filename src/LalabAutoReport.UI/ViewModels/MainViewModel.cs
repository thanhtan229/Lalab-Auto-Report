using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepository;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _activeTab = "Dashboard";

    [ObservableProperty]
    private bool _hasRootFolderWarning;

    [ObservableProperty]
    private string _rootFolderText = string.Empty;

    public DashboardViewModel DashboardVM { get; }
    public ReportsViewModel ReportsVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public CustomersViewModel CustomersVM { get; }
    public PriceListViewModel PriceListVM { get; }

    public MainViewModel(
        DashboardViewModel dashboardVM,
        ReportsViewModel reportsVM,
        SettingsViewModel settingsVM,
        CustomersViewModel customersVM,
        PriceListViewModel priceListVM,
        ISettingsRepository settingsRepository)
    {
        DashboardVM = dashboardVM;
        ReportsVM = reportsVM;
        SettingsVM = settingsVM;
        CustomersVM = customersVM;
        PriceListVM = priceListVM;
        _settingsRepository = settingsRepository;

        _currentView = DashboardVM;
        _activeTab = "Dashboard";

        SettingsVM.SettingsSaved += OnSettingsSaved;
    }

    public async Task InitializeAsync()
    {
        await SettingsVM.LoadSettingsAsync();
        await CheckRootFolderAsync();
        await DashboardVM.LoadOrdersForSelectedDateAsync();
    }

    private void OnSettingsSaved()
    {
        _ = CheckRootFolderAsync();
    }

    private async Task CheckRootFolderAsync()
    {
        var settings = await _settingsRepository.GetSettingsAsync();
        RootFolderText = settings.RootFolder;
        HasRootFolderWarning = string.IsNullOrWhiteSpace(settings.RootFolder) || !Directory.Exists(settings.RootFolder);
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        CurrentView = DashboardVM;
        ActiveTab = "Dashboard";
    }

    [RelayCommand]
    private async Task NavigateToReportsAsync()
    {
        CurrentView = ReportsVM;
        ActiveTab = "Reports";
        await ReportsVM.LoadReportAsync();
    }

    [RelayCommand]
    private async Task NavigateToCustomersAsync()
    {
        CurrentView = CustomersVM;
        ActiveTab = "Customers";
        await CustomersVM.LoadCustomersAsync();
    }

    [RelayCommand]
    private async Task NavigateToPriceListAsync()
    {
        CurrentView = PriceListVM;
        ActiveTab = "PriceList";
        await PriceListVM.LoadSpecificationsAsync();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentView = SettingsVM;
        ActiveTab = "Settings";
        _ = SettingsVM.LoadSettingsAsync();
    }
}
