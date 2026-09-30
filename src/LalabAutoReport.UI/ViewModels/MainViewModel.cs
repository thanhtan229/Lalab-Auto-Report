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
    [NotifyPropertyChangedFor(nameof(IsDashboardActive))]
    [NotifyPropertyChangedFor(nameof(IsReportsActive))]
    [NotifyPropertyChangedFor(nameof(IsCustomersActive))]
    [NotifyPropertyChangedFor(nameof(IsPriceListActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private string _activeTab = "Dashboard";

    public bool IsDashboardActive
    {
        get => ActiveTab == "Dashboard";
        set
        {
            if (value && ActiveTab != "Dashboard")
            {
                NavigateToDashboard();
            }
        }
    }

    public bool IsReportsActive
    {
        get => ActiveTab == "Reports";
        set
        {
            if (value && ActiveTab != "Reports")
            {
                _ = NavigateToReportsAsync();
            }
        }
    }

    public bool IsCustomersActive
    {
        get => ActiveTab == "Customers";
        set
        {
            if (value && ActiveTab != "Customers")
            {
                _ = NavigateToCustomersAsync();
            }
        }
    }

    public bool IsPriceListActive
    {
        get => ActiveTab == "PriceList";
        set
        {
            if (value && ActiveTab != "PriceList")
            {
                _ = NavigateToPriceListAsync();
            }
        }
    }

    public bool IsSettingsActive
    {
        get => ActiveTab == "Settings";
        set
        {
            if (value && ActiveTab != "Settings")
            {
                NavigateToSettings();
            }
        }
    }

    [ObservableProperty]
    private bool _hasRootFolderWarning;

    [ObservableProperty]
    private string _rootFolderText = string.Empty;

    public DashboardViewModel DashboardVM { get; } = null!;
    public ReportsViewModel ReportsVM { get; } = null!;
    public SettingsViewModel SettingsVM { get; } = null!;
    public CustomersViewModel CustomersVM { get; } = null!;
    public PriceListViewModel PriceListVM { get; } = null!;

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

        if (SettingsVM != null)
        {
            SettingsVM.SettingsSaved += OnSettingsSaved;
            SettingsVM.DataResetCompleted += OnDataResetCompleted;
        }
    }

    public async Task InitializeAsync()
    {
        if (SettingsVM != null)
            await SettingsVM.LoadSettingsAsync();
        await CheckRootFolderAsync();
        if (DashboardVM != null)
            await DashboardVM.LoadOrdersForSelectedDateAsync();
    }

    private void OnSettingsSaved()
    {
        _ = CheckRootFolderAsync();
    }

    private async void OnDataResetCompleted()
    {
        await CheckRootFolderAsync();
        if (DashboardVM != null)
        {
            await DashboardVM.LoadOrdersForSelectedDateAsync();
        }
        if (CustomersVM != null)
        {
            await CustomersVM.LoadCustomersAsync();
        }
        if (PriceListVM != null)
        {
            await PriceListVM.LoadSpecificationsAsync();
        }
        if (ReportsVM != null)
        {
            await ReportsVM.LoadReportBillsAsync();
            await ReportsVM.LoadReportAsync();
        }
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
        if (ReportsVM != null)
        {
            await ReportsVM.LoadReportAsync();
        }
    }

    [RelayCommand]
    private async Task NavigateToCustomersAsync()
    {
        CurrentView = CustomersVM;
        ActiveTab = "Customers";
        if (CustomersVM != null)
        {
            await CustomersVM.LoadCustomersAsync();
        }
    }

    [RelayCommand]
    private async Task NavigateToPriceListAsync()
    {
        CurrentView = PriceListVM;
        ActiveTab = "PriceList";
        if (PriceListVM != null)
        {
            await PriceListVM.LoadSpecificationsAsync();
        }
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentView = SettingsVM;
        ActiveTab = "Settings";
        if (SettingsVM != null)
        {
            _ = SettingsVM.LoadSettingsAsync();
        }
    }
}
