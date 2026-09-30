using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Domain;
using LalabAutoReport.Core.Interfaces;

namespace LalabAutoReport.Infrastructure.Data;

public class SqliteSettingsRepository : ISettingsRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteSettingsRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<(string Key, string Value)>(
            "SELECT key, value FROM app_settings"
        );

        var dict = rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        var settings = new AppSettings();

        if (dict.TryGetValue("RootFolder", out var root))
        {
            settings.RootFolder = root;
        }

        if (dict.TryGetValue("SupportedExtensions", out var exts) && !string.IsNullOrWhiteSpace(exts))
        {
            settings.SupportedExtensions = exts
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.StartsWith('.') ? e : "." + e)
                .ToList();
        }

        if (dict.TryGetValue("AutoScanStartup", out var autoScan) && bool.TryParse(autoScan, out var parsedAuto))
        {
            settings.AutoScanStartup = parsedAuto;
        }

        if (dict.TryGetValue("StartupIncludeYesterday", out var incYest) && bool.TryParse(incYest, out var parsedYest))
        {
            settings.StartupIncludeYesterday = parsedYest;
        }

        if (dict.TryGetValue("AutoRegisterContextMenu", out var autoMenu) && bool.TryParse(autoMenu, out var parsedMenu))
        {
            settings.AutoRegisterContextMenu = parsedMenu;
        }
        else
        {
            settings.AutoRegisterContextMenu = true; // Default always enabled
        }

        if (dict.TryGetValue("EnableIdleScan", out var idleScan) && bool.TryParse(idleScan, out var parsedIdle))
        {
            settings.EnableIdleScan = parsedIdle;
        }

        if (dict.TryGetValue("IdleThresholdMinutes", out var idleThresh) && int.TryParse(idleThresh, out var parsedThresh))
        {
            settings.IdleThresholdMinutes = parsedThresh;
        }

        if (dict.TryGetValue("IdleScanWindowDays", out var idleDays) && int.TryParse(idleDays, out var parsedDays))
        {
            settings.IdleScanWindowDays = parsedDays;
        }

        if (dict.TryGetValue("GuestAliases", out var gAliases) && !string.IsNullOrWhiteSpace(gAliases))
        {
            settings.GuestAliases = gAliases
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (dict.TryGetValue("BillExportFolder", out var exportFolder) && !string.IsNullOrWhiteSpace(exportFolder))
        {
            settings.BillExportFolder = exportFolder;
        }
        else if (!string.IsNullOrWhiteSpace(settings.RootFolder))
        {
            settings.BillExportFolder = Path.Combine(settings.RootFolder, "Bills");
        }
        else
        {
            settings.BillExportFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LalabReports", "Bills");
        }

        return settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        string extString = string.Join(",", settings.SupportedExtensions);

        await connection.ExecuteAsync(@"
            INSERT INTO app_settings (key, value) VALUES ('RootFolder', @RootFolder)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('BillExportFolder', @BillExportFolder)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('SupportedExtensions', @SupportedExtensions)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('AutoScanStartup', @AutoScanStartup)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('StartupIncludeYesterday', @StartupIncludeYesterday)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('AutoRegisterContextMenu', @AutoRegisterContextMenu)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('EnableIdleScan', @EnableIdleScan)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('IdleThresholdMinutes', @IdleThresholdMinutes)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('IdleScanWindowDays', @IdleScanWindowDays)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('GuestAliases', @GuestAliases)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        ", new
        {
            RootFolder = settings.RootFolder,
            BillExportFolder = settings.BillExportFolder,
            SupportedExtensions = extString,
            AutoScanStartup = settings.AutoScanStartup.ToString(),
            StartupIncludeYesterday = settings.StartupIncludeYesterday.ToString(),
            AutoRegisterContextMenu = settings.AutoRegisterContextMenu.ToString(),
            EnableIdleScan = settings.EnableIdleScan.ToString(),
            IdleThresholdMinutes = settings.IdleThresholdMinutes.ToString(),
            IdleScanWindowDays = settings.IdleScanWindowDays.ToString(),
            GuestAliases = string.Join(";", settings.GuestAliases)
        }, transaction: transaction);

        transaction.Commit();
    }
}
