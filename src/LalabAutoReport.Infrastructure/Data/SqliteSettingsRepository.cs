using System;
using System.Collections.Generic;
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

            INSERT INTO app_settings (key, value) VALUES ('SupportedExtensions', @SupportedExtensions)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('AutoScanStartup', @AutoScanStartup)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;

            INSERT INTO app_settings (key, value) VALUES ('StartupIncludeYesterday', @StartupIncludeYesterday)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        ", new
        {
            RootFolder = settings.RootFolder,
            SupportedExtensions = extString,
            AutoScanStartup = settings.AutoScanStartup.ToString(),
            StartupIncludeYesterday = settings.StartupIncludeYesterday.ToString()
        }, transaction: transaction);

        transaction.Commit();
    }
}
