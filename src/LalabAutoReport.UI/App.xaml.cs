using System;
using System.Windows;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using LalabAutoReport.Infrastructure.Logging;
using LalabAutoReport.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace LalabAutoReport.UI;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Initialize logging
        LoggingSetup.Initialize();
        Log.Information("Lalab Auto Report starting up...");

        // 2. Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled AppDomain exception");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Unhandled Dispatcher exception");
            MessageBox.Show($"Đã xảy ra lỗi: {args.Exception.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // 3. Configure Dependency Injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // 4. Run database migrations safely
        try
        {
            var migrator = _serviceProvider.GetRequiredService<IDatabaseMigrator>();
            await migrator.MigrateAsync();
            Log.Information("Database migrations completed successfully.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Database migration failed during startup.");
            MessageBox.Show($"Không thể khởi tạo cơ sở dữ liệu: {ex.Message}", "Lỗi Cơ Sở Dữ Liệu", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // 5. Show Main Window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core & Infrastructure Services
        services.AddSingleton<IFileSystemAdapter, PhysicalFileSystemAdapter>();
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<IDatabaseBackupService, DatabaseBackupService>();
        services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IOrderRepository, SqliteOrderRepository>();
        services.AddSingleton<ICustomerRepository, SqliteCustomerRepository>();
        services.AddSingleton<IPrintSpecificationRepository, SqlitePrintSpecificationRepository>();
        services.AddSingleton<IFolderStructureParser, FolderStructureParser>();
        services.AddSingleton<IPrintFolderResolver, PrintFolderResolver>();
        services.AddSingleton<ICustomerResolver, CustomerResolver>();
        services.AddSingleton<IPrintSpecificationResolver, PrintSpecificationResolver>();
        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<IBillRepository, SqliteBillRepository>();
        services.AddSingleton<IBillingService, BillingService>();
        services.AddSingleton<ILockingService, LockingService>();
        services.AddSingleton<IReportService, ReportService>();

        // ViewModels
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<CustomersViewModel>();
        services.AddSingleton<PriceListViewModel>();
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Lalab Auto Report shutting down with exit code {Code}.", e.ApplicationExitCode);
        LoggingSetup.CloseAndFlush();
        base.OnExit(e);
    }
}
