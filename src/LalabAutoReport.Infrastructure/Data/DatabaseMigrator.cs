using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.Data;

public class DatabaseMigrator : IDatabaseMigrator
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseMigrator>? _logger;

    public DatabaseMigrator(ISqliteConnectionFactory connectionFactory, ILogger<DatabaseMigrator>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // 1. Ensure migrations table exists
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
        ");

        var appliedMigrations = (await connection.QueryAsync<int>("SELECT version FROM schema_migrations")).ToHashSet();

        // List of all migrations in order
        var migrations = GetMigrations();

        foreach (var migration in migrations)
        {
            if (!appliedMigrations.Contains(migration.Version))
            {
                _logger?.LogInformation("Applying migration {Version}: {Name}", migration.Version, migration.Name);

                using var transaction = connection.BeginTransaction();
                try
                {
                    await connection.ExecuteAsync(migration.Sql, transaction: transaction);
                    await connection.ExecuteAsync(
                        "INSERT INTO schema_migrations (version, name, applied_at) VALUES (@Version, @Name, @AppliedAt)",
                        new
                        {
                            Version = migration.Version,
                            Name = migration.Name,
                            AppliedAt = DateTimeOffset.UtcNow.ToString("o")
                        },
                        transaction: transaction
                    );
                    transaction.Commit();
                    _logger?.LogInformation("Successfully applied migration {Version}: {Name}", migration.Version, migration.Name);
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger?.LogError(ex, "Failed to apply migration {Version}: {Name}", migration.Version, migration.Name);
                    throw;
                }
            }
        }
    }

    private static IReadOnlyList<(int Version, string Name, string Sql)> GetMigrations()
    {
        return new List<(int, string, string)>
        {
            (1, "InitialSchema", @"
                CREATE TABLE IF NOT EXISTS app_settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS customers (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    canonical_name TEXT NOT NULL,
                    phone TEXT,
                    note TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS customer_aliases (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    customer_id INTEGER NOT NULL,
                    alias_text TEXT NOT NULL,
                    normalized_alias TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(customer_id) REFERENCES customers(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS print_specifications (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    canonical_name TEXT NOT NULL,
                    unit_price INTEGER NOT NULL DEFAULT 0,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS print_specification_aliases (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    print_specification_id INTEGER NOT NULL,
                    alias_text TEXT NOT NULL,
                    normalized_alias TEXT NOT NULL UNIQUE,
                    FOREIGN KEY(print_specification_id) REFERENCES print_specifications(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    work_date TEXT NOT NULL,
                    customer_id INTEGER,
                    original_folder_name TEXT NOT NULL,
                    relative_path TEXT NOT NULL UNIQUE,
                    status TEXT NOT NULL,
                    last_scan_at TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(customer_id) REFERENCES customers(id)
                );

                CREATE TABLE IF NOT EXISTS scan_snapshots (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    order_id INTEGER NOT NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT,
                    scan_scope TEXT NOT NULL,
                    status TEXT NOT NULL,
                    error_message TEXT,
                    app_version TEXT NOT NULL,
                    FOREIGN KEY(order_id) REFERENCES orders(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS order_item_scans (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    scan_snapshot_id INTEGER NOT NULL,
                    order_id INTEGER NOT NULL,
                    print_specification_id INTEGER,
                    specification_folder_name TEXT NOT NULL,
                    specification_relative_path TEXT NOT NULL,
                    source_count INTEGER NOT NULL,
                    print_count INTEGER,
                    selected_print_folder_relative_path TEXT,
                    print_folder_status TEXT NOT NULL,
                    mismatch_count INTEGER,
                    scan_status TEXT NOT NULL,
                    error_message TEXT,
                    candidate_print_folders TEXT,
                    bill_quantity INTEGER,
                    quantity_resolution_mode TEXT,
                    quantity_resolution_note TEXT,
                    FOREIGN KEY(scan_snapshot_id) REFERENCES scan_snapshots(id) ON DELETE CASCADE,
                    FOREIGN KEY(order_id) REFERENCES orders(id) ON DELETE CASCADE,
                    FOREIGN KEY(print_specification_id) REFERENCES print_specifications(id)
                );

                CREATE TABLE IF NOT EXISTS bills (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    order_id INTEGER NOT NULL,
                    customer_id INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    subtotal INTEGER NOT NULL DEFAULT 0,
                    locked_at TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(order_id) REFERENCES orders(id),
                    FOREIGN KEY(customer_id) REFERENCES customers(id)
                );

                CREATE TABLE IF NOT EXISTS bill_lines (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bill_id INTEGER NOT NULL,
                    print_specification_id INTEGER NOT NULL,
                    source_count INTEGER NOT NULL,
                    print_count INTEGER,
                    bill_quantity INTEGER NOT NULL,
                    quantity_resolution_mode TEXT NOT NULL,
                    quantity_resolution_note TEXT,
                    unit_price INTEGER NOT NULL,
                    line_total INTEGER NOT NULL,
                    source_scan_snapshot_id INTEGER NOT NULL,
                    FOREIGN KEY(bill_id) REFERENCES bills(id) ON DELETE CASCADE,
                    FOREIGN KEY(print_specification_id) REFERENCES print_specifications(id),
                    FOREIGN KEY(source_scan_snapshot_id) REFERENCES scan_snapshots(id)
                );

                CREATE INDEX IF NOT EXISTS idx_orders_work_date ON orders(work_date);
                CREATE INDEX IF NOT EXISTS idx_orders_customer_id ON orders(customer_id);
                CREATE INDEX IF NOT EXISTS idx_order_item_scans_order_id ON order_item_scans(order_id);
                CREATE INDEX IF NOT EXISTS idx_customer_aliases_normalized ON customer_aliases(normalized_alias);
                CREATE INDEX IF NOT EXISTS idx_spec_aliases_normalized ON print_specification_aliases(normalized_alias);

                -- Initial Default Specifications
                INSERT INTO print_specifications (canonical_name, unit_price, is_active, created_at, updated_at)
                VALUES ('13x18 in', 5000, 1, datetime('now'), datetime('now'));

                INSERT INTO print_specifications (canonical_name, unit_price, is_active, created_at, updated_at)
                VALUES ('20x30', 15000, 1, datetime('now'), datetime('now'));

                INSERT INTO print_specifications (canonical_name, unit_price, is_active, created_at, updated_at)
                VALUES ('40x60 TG', 80000, 1, datetime('now'), datetime('now'));
            "),
            (2, "AddPostLockWarningField", @"
                ALTER TABLE orders ADD COLUMN filesystem_changed_after_lock INTEGER NOT NULL DEFAULT 0;
            ")
        };
    }
}
