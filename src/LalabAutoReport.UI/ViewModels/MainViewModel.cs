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
    private readonly IUpdateService? _updateService;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardActive))]
    [NotifyPropertyChangedFor(nameof(IsInvoicesActive))]
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

    public bool IsInvoicesActive
    {
        get => ActiveTab == "Invoices";
        set
        {
            if (value && ActiveTab != "Invoices")
            {
                _ = NavigateToInvoicesAsync();
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
    public InvoicesViewModel InvoicesVM { get; } = null!;
    public ReportsViewModel ReportsVM { get; } = null!;
    public SettingsViewModel SettingsVM { get; } = null!;
    public CustomersViewModel CustomersVM { get; } = null!;
    public PriceListViewModel PriceListVM { get; } = null!;

    public MainViewModel(
        DashboardViewModel dashboardVM,
        InvoicesViewModel invoicesVM,
        ReportsViewModel reportsVM,
        SettingsViewModel settingsVM,
        CustomersViewModel customersVM,
        PriceListViewModel priceListVM,
        ISettingsRepository settingsRepository,
        IUpdateService? updateService = null)
    {
        DashboardVM = dashboardVM;
        InvoicesVM = invoicesVM;
        ReportsVM = reportsVM;
        SettingsVM = settingsVM;
        CustomersVM = customersVM;
        PriceListVM = priceListVM;
        _settingsRepository = settingsRepository;
        _updateService = updateService;

        _currentView = DashboardVM;
        _activeTab = "Dashboard";

        if (SettingsVM != null)
        {
            SettingsVM.SettingsSaved += OnSettingsSaved;
            SettingsVM.DataResetCompleted += OnDataResetCompleted;
        }

        if (CustomersVM != null)
        {
            CustomersVM.ViewCustomerInvoicesRequested += OnViewCustomerInvoicesRequested;
        }
    }

    public async Task InitializeAsync()
    {
        if (SettingsVM != null)
            await SettingsVM.LoadSettingsAsync();
        await CheckRootFolderAsync();
        if (DashboardVM != null)
            await DashboardVM.LoadOrdersForSelectedDateAsync();

        _ = CheckForUpdateOnStartupAsync();
    }

    private async Task CheckForUpdateOnStartupAsync()
    {
        if (_updateService == null) return;
        try
        {
            var updateInfo = await _updateService.CheckForUpdateAsync();
            if (updateInfo.IsUpdateAvailable)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var updateVm = new UpdateViewModel(_updateService);
                    updateVm.Initialize(updateInfo);
                    var dialog = new Views.UpdateDialog(updateVm)
                    {
                        Owner = System.Windows.Application.Current.MainWindow
                    };
                    dialog.ShowDialog();
                });
            }
        }
        catch
        {
            // Silently ignore update check errors on background startup
        }
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
        if (InvoicesVM != null)
        {
            await InvoicesVM.LoadInvoicesAsync();
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
    private async Task NavigateToInvoicesAsync()
    {
        CurrentView = InvoicesVM;
        ActiveTab = "Invoices";
        if (InvoicesVM != null)
        {
            await InvoicesVM.LoadInvoicesAsync();
        }
    }

    private async void OnViewCustomerInvoicesRequested(long customerId)
    {
        try
        {
            CurrentView = InvoicesVM;
            ActiveTab = "Invoices";
            if (InvoicesVM != null)
            {
                await InvoicesVM.LoadInvoicesAsync();
                InvoicesVM.FilterByCustomer(customerId, customerId == -1);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating to customer invoices: {ex.Message}");
        }
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

    [RelayCommand]
    public void OpenGlobalSearch()
    {
        var searchService = (System.Windows.Application.Current as App)?.Services?.GetService(typeof(IGlobalSearchService)) as IGlobalSearchService;
        var billRepo = (System.Windows.Application.Current as App)?.Services?.GetService(typeof(ICustomerBillRepository)) as ICustomerBillRepository;
        var billingService = (System.Windows.Application.Current as App)?.Services?.GetService(typeof(ICustomerBillingService)) as ICustomerBillingService;
        var jpegExporter = (System.Windows.Application.Current as App)?.Services?.GetService(typeof(IJpegBillExporter)) as IJpegBillExporter;
        var excelExporter = (System.Windows.Application.Current as App)?.Services?.GetService(typeof(IExcelBillExporter)) as IExcelBillExporter;
        var settingsRepo = _settingsRepository;

        if (searchService == null || billRepo == null) return;

        var vm = new GlobalSearchViewModel(searchService, billRepo);

        vm.NavigateToOrderRequested += (workDate, orderCode) =>
        {
            if (DateTime.TryParse(workDate, out var date))
            {
                DashboardVM.SelectedDate = date;
            }
            if (!string.IsNullOrWhiteSpace(orderCode))
            {
                DashboardVM.SearchText = orderCode;
            }
            ActiveTab = "Dashboard";
            CurrentView = DashboardVM;
        };

        vm.OpenBillRequested += bill =>
        {
            if (billingService != null && jpegExporter != null)
            {
                var reviewVm = new CustomerBillReviewViewModel(
                    bill,
                    billingService,
                    jpegExporter,
                    excelExporter,
                    null,
                    null,
                    settingsRepo,
                    null,
                    billRepo);

                var reviewWin = new Views.CustomerBillReviewWindow(reviewVm)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                reviewWin.ShowDialog();
            }
        };

        var searchWin = new Views.GlobalSearchWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        searchWin.ShowDialog();
    }
}
