using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace LalabAutoReport.Infrastructure.Logging;

public static class LoggingSetup
{
    public static string LogDirectory { get; private set; } = string.Empty;

    public static void Initialize(string? customLogDir = null)
    {
        if (string.IsNullOrWhiteSpace(customLogDir))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            LogDirectory = Path.Combine(appData, "LalabAutoReport", "logs");
        }
        else
        {
            LogDirectory = customLogDir;
        }

        Directory.CreateDirectory(LogDirectory);

        string logFilePath = Path.Combine(LogDirectory, "lalab-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        Log.Information("Lalab Auto Report logging initialized. Log directory: {LogDir}", LogDirectory);
    }

    public static void CloseAndFlush()
    {
        Log.CloseAndFlush();
    }
}
