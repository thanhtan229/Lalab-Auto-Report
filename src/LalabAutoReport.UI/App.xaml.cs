using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using LalabAutoReport.Infrastructure.Data;
using LalabAutoReport.Infrastructure.FileSystem;
using LalabAutoReport.Infrastructure.Logging;
using LalabAutoReport.Infrastructure.Windows;
using LalabAutoReport.UI.Services;
using LalabAutoReport.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace LalabAutoReport.UI;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private static Mutex? _singleInstanceMutex;
    private const string MutexName = "Local\\LalabAutoReport_SingleInstance_Mutex_v1";
    private const string PipeName = "LalabAutoReport_IpcPipe_v1";
    private CancellationTokenSource? _pipeCts;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Parse command-line arguments
        string? quickBillFolder = null;
        string? togglePrintedFolder = null;
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (string.Equals(e.Args[i], "--quick-bill", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                quickBillFolder = e.Args[i + 1];
            }
            else if (string.Equals(e.Args[i], "--toggle-printed", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                togglePrintedFolder = e.Args[i + 1];
            }
        }

        // 2. Single-Instance Check
        bool isFirstInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out isFirstInstance);
        }
        catch (AbandonedMutexException)
        {
            isFirstInstance = true;
        }
        catch
        {
            isFirstInstance = false;
        }

        if (!isFirstInstance)
        {
            // Another instance is already running. Send request to existing instance and exit.
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1500);
                using var writer = new StreamWriter(client, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(quickBillFolder))
                {
                    writer.WriteLine($"QUICK_BILL|{quickBillFolder}");
                }
                else if (!string.IsNullOrWhiteSpace(togglePrintedFolder))
                {
                    writer.WriteLine($"TOGGLE_PRINTED|{togglePrintedFolder}");
                }
                else
                {
                    writer.WriteLine("ACTIVATE|");
                }
                writer.Flush();
            }
            catch
            {
                // Silently fallback if running instance did not respond in time
            }
            finally
            {
                Environment.Exit(0);
            }
            return;
        }

        // 2.1 Fast Headless Mode: If launched with ONLY --toggle-printed and app was not previously running
        if (!string.IsNullOrWhiteSpace(togglePrintedFolder) && string.IsNullOrWhiteSpace(quickBillFolder))
        {
            try
            {
                LoggingSetup.Initialize();
                var connFactory = new SqliteConnectionFactory();
                var migrator = new DatabaseMigrator(connFactory);
                await migrator.MigrateAsync();

                var folderPrintRepo = new SqliteFolderPrintRepository(connFactory);
                var visualMarkerService = new FolderVisualMarkerService();
                var orderRepo = new SqliteOrderRepository(connFactory);
                var settingsRepo = new SqliteSettingsRepository(connFactory);
                var printStatusService = new PrintStatusService(folderPrintRepo, visualMarkerService, orderRepo, settingsRepo);

                var result = await printStatusService.ToggleStatusAsync(togglePrintedFolder);
                Log.Information("Headless TOGGLE_PRINTED processed for '{Folder}': New status {Status}, visual icon updated: {Visual}",
                    togglePrintedFolder, result.NewStatus, result.VisualIconUpdated);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed in headless TOGGLE_PRINTED for '{Folder}'", togglePrintedFolder);
            }
            finally
            {
                LoggingSetup.CloseAndFlush();
                if (_singleInstanceMutex != null)
                {
                    try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                    _singleInstanceMutex.Dispose();
                    _singleInstanceMutex = null;
                }
                Environment.Exit(0);
            }
            return;
        }

        // 3. Initialize logging
        LoggingSetup.Initialize();
        Log.Information("Lalab Auto Report starting up...");

        // 4. Start background IPC pipe server for subsequent instances
        StartIpcServer();

        // 5. Global exception handlers
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

        // Prevent WPF from shutting down before MainWindow is opened
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 6. Configure Dependency Injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // 7. Run database migrations safely
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

        // 7.1 Auto-register Windows Explorer context menu integration (Default always enabled)
        try
        {
            var settingsRepo = _serviceProvider.GetRequiredService<ISettingsRepository>();
            var settings = await settingsRepo.GetSettingsAsync();
            if (settings.AutoRegisterContextMenu)
            {
                var contextMenuService = _serviceProvider.GetRequiredService<IContextMenuIntegrationService>();
                contextMenuService.EnsureContextMenuRegistered();
                Log.Information("Windows Explorer right-click context menu integration auto-registered successfully.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to auto-register Windows Explorer context menu on startup.");
        }

        // 8. Show Main Window
        try
        {
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Log.Information("Main window displayed successfully.");

            // 8.1 Start AutoScanCoordinator and run startup scan if enabled
            try
            {
                var autoScanner = _serviceProvider.GetRequiredService<IAutoScanCoordinator>();
                autoScanner.Start();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await autoScanner.PerformStartupScanAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed during background startup auto-scan.");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize AutoScanCoordinator.");
            }

            // 9. If started with --quick-bill, trigger quick bill review immediately
            if (!string.IsNullOrWhiteSpace(quickBillFolder))
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var launcher = _serviceProvider.GetRequiredService<QuickBillLauncher>();
                        await launcher.LaunchQuickBillForFolderAsync(quickBillFolder);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to launch initial quick bill for folder: {Folder}", quickBillFolder);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to display main window.");
            MessageBox.Show($"Lỗi hiển thị giao diện: {ex.Message}", "Lỗi Khởi Động", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void StartIpcServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? message = await reader.ReadLineAsync(token);

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        await Dispatcher.InvokeAsync(() => HandleIpcMessage(message));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "IPC Pipe server loop error.");
                    try { await Task.Delay(300, token); } catch { break; }
                }
            }
        }, token);
    }

    private async void HandleIpcMessage(string message)
    {
        try
        {
            var parts = message.Split('|', 2);
            string cmd = parts[0];

            if (cmd == "TOGGLE_PRINTED" && parts.Length > 1)
            {
                string folder = parts[1];
                var printStatusService = _serviceProvider?.GetRequiredService<IPrintStatusService>();
                if (printStatusService != null)
                {
                    var result = await printStatusService.ToggleStatusAsync(folder);
                    Log.Information("IPC TOGGLE_PRINTED processed for '{Folder}': New status {Status}, visual icon updated: {Visual}",
                        folder, result.NewStatus, result.VisualIconUpdated);
                }
                return;
            }

            if (MainWindow != null)
            {
                if (MainWindow.WindowState == WindowState.Minimized)
                {
                    MainWindow.WindowState = WindowState.Normal;
                }
                MainWindow.Show();
                MainWindow.Activate();
                MainWindow.Topmost = true;
                MainWindow.Topmost = false;
                MainWindow.Focus();
            }

            if (cmd == "QUICK_BILL" && parts.Length > 1)
            {
                string folder = parts[1];
                var launcher = _serviceProvider?.GetRequiredService<QuickBillLauncher>();
                if (launcher != null)
                {
                    await launcher.LaunchQuickBillForFolderAsync(folder);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Lỗi xử lý IPC message: {Message}", message);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core & Infrastructure Services
        services.AddSingleton<IFileSystemAdapter, PhysicalFileSystemAdapter>();
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<IDatabaseBackupService, DatabaseBackupService>();
        services.AddSingleton<IDatabaseMigrator, DatabaseMigrator>();
        services.AddSingleton<IDatabaseResetService, DatabaseResetService>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IOrderRepository, SqliteOrderRepository>();
        services.AddSingleton<ICustomerRepository, SqliteCustomerRepository>();
        services.AddSingleton<IFolderPrintRepository, SqliteFolderPrintRepository>();
        services.AddSingleton<IFolderVisualMarkerService, FolderVisualMarkerService>();
        services.AddSingleton<IPrintStatusService, PrintStatusService>();
        services.AddSingleton<SqliteProductRepository>();
        services.AddSingleton<IPrintSpecificationRepository>(sp => sp.GetRequiredService<SqliteProductRepository>());
        services.AddSingleton<IProductRepository>(sp => sp.GetRequiredService<SqliteProductRepository>());
        services.AddSingleton<IFolderStructureParser, FolderStructureParser>();
        services.AddSingleton<IPrintFolderResolver, PrintFolderResolver>();
        services.AddSingleton<ICustomerResolver, CustomerResolver>();
        services.AddSingleton<PrintSpecificationResolver>();
        services.AddSingleton<IPrintSpecificationResolver>(sp => sp.GetRequiredService<PrintSpecificationResolver>());
        services.AddSingleton<IProductResolver>(sp => sp.GetRequiredService<PrintSpecificationResolver>());
        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<IBillRepository, SqliteBillRepository>();
        services.AddSingleton<IBillingService, BillingService>();
        services.AddSingleton<ILockingService, LockingService>();
        services.AddSingleton<ICustomerBillRepository, SqliteCustomerBillRepository>();
        services.AddSingleton<ICustomerBillingService, CustomerBillingService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IJpegBillExporter, LalabAutoReport.Infrastructure.Reporting.WpfJpegBillExporter>();
        services.AddSingleton<IExcelBillExporter, LalabAutoReport.Infrastructure.Reporting.ClosedXmlBillExporter>();
        services.AddSingleton<IContextMenuIntegrationService, WindowsContextMenuIntegrationService>();
        services.AddSingleton<QuickBillLauncher>();
        services.AddSingleton<IFolderFingerprintService, FolderFingerprintService>();
        services.AddSingleton<IIdleDetectionService, WindowsIdleDetector>();
        services.AddSingleton<IAutoScanCoordinator, AutoScanCoordinator>();
        services.AddSingleton<IUpdateService, LalabAutoReport.Infrastructure.Services.GitHubUpdateService>();

        // ViewModels
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<CustomersViewModel>();
        services.AddSingleton<PriceListViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<UpdateViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
        services.AddTransient<Views.UpdateDialog>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var autoScanner = _serviceProvider?.GetService<IAutoScanCoordinator>();
            autoScanner?.Stop();
        }
        catch { }

        _pipeCts?.Cancel();
        _pipeCts?.Dispose();

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        Log.Information("Lalab Auto Report shutting down with exit code {Code}.", e.ApplicationExitCode);
        LoggingSetup.CloseAndFlush();
        base.OnExit(e);
    }
}
