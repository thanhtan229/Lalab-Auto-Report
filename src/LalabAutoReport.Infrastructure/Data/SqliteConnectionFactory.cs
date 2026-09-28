using System;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LalabAutoReport.Infrastructure.Data;

public interface ISqliteConnectionFactory
{
    SqliteConnection CreateConnection();
    string DatabasePath { get; }
}

public class SqliteConnectionFactory : ISqliteConnectionFactory
{
    public string DatabasePath { get; }

    public SqliteConnectionFactory(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "LalabAutoReport");
            Directory.CreateDirectory(folder);
            DatabasePath = Path.Combine(folder, "lalab_autoreport.db");
        }
        else
        {
            DatabasePath = databasePath;
            string? dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};");
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            cmd.ExecuteNonQuery();
        }

        return connection;
    }
}
