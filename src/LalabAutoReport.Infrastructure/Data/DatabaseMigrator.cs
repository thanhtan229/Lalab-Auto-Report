using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LalabAutoReport.Core.Interfaces;
using LalabAutoReport.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LalabAutoReport.Infrastructure.Data;

public class DatabaseMigrator : IDatabaseMigrator
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IDatabaseBackupService? _backupService;
    private readonly ILogger<DatabaseMigrator>? _logger;

    public DatabaseMigrator(
        ISqliteConnectionFactory connectionFactory,
        IDatabaseBackupService? backupService = null,
        ILogger<DatabaseMigrator>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _backupService = backupService;
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
        var pendingMigrations = migrations.Where(m => !appliedMigrations.Contains(m.Version)).ToList();

        if (pendingMigrations.Count > 0 && _backupService != null)
        {
            try
            {
                _logger?.LogInformation("Creating automatic database backup before running {Count} pending migration(s)...", pendingMigrations.Count);
                await _backupService.CreateBackupAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to create pre-migration backup; proceeding with caution");
            }
        }

        foreach (var migration in pendingMigrations)
        {
            _logger?.LogInformation("Applying migration {Version}: {Name}", migration.Version, migration.Name);

            using var transaction = connection.BeginTransaction();
            try
            {
                if (!string.IsNullOrWhiteSpace(migration.Sql))
                {
                    await connection.ExecuteAsync(migration.Sql, transaction: transaction);
                }

                if (migration.PostAction != null)
                {
                    await migration.PostAction(connection, transaction, _logger);
                }

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

    private class MigrationDefinition
    {
        public int Version { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
        public Func<SqliteConnection, SqliteTransaction, ILogger?, Task>? PostAction { get; set; }
    }

    private static IReadOnlyList<MigrationDefinition> GetMigrations()
    {
        return new List<MigrationDefinition>
        {
            new MigrationDefinition
            {
                Version = 1,
                Name = "InitialSchema",
                Sql = @"
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
            "
            },
            new MigrationDefinition
            {
                Version = 2,
                Name = "AddPostLockWarningField",
                Sql = @"
                ALTER TABLE orders ADD COLUMN filesystem_changed_after_lock INTEGER NOT NULL DEFAULT 0;
            "
            },
            new MigrationDefinition
            {
                Version = 3,
                Name = "V2ProductAndOrderHierarchy",
                Sql = @"
                -- Add product category and album pricing fields to print_specifications
                ALTER TABLE print_specifications ADD COLUMN category TEXT NOT NULL DEFAULT 'PhotoPrint';
                ALTER TABLE print_specifications ADD COLUMN billing_method TEXT NOT NULL DEFAULT 'FileCount';
                ALTER TABLE print_specifications ADD COLUMN included_sheets INTEGER;
                ALTER TABLE print_specifications ADD COLUMN base_price INTEGER;
                ALTER TABLE print_specifications ADD COLUMN extra_sheet_price INTEGER;

                -- Add order hierarchy fields to orders
                ALTER TABLE orders ADD COLUMN order_kind TEXT NOT NULL DEFAULT 'Implicit';
                ALTER TABLE orders ADD COLUMN order_name TEXT;

                -- Add V2 fields to order_item_scans
                ALTER TABLE order_item_scans ADD COLUMN printable_file_count INTEGER;
                ALTER TABLE order_item_scans ADD COLUMN billing_metadata_json TEXT;

                -- Add snapshot fields to bill_lines
                ALTER TABLE bill_lines ADD COLUMN product_name_snapshot TEXT;
                ALTER TABLE bill_lines ADD COLUMN billing_method_snapshot TEXT;
                ALTER TABLE bill_lines ADD COLUMN sheet_count INTEGER;
                ALTER TABLE bill_lines ADD COLUMN included_sheets_snapshot INTEGER;
                ALTER TABLE bill_lines ADD COLUMN extra_sheet_count INTEGER;
                ALTER TABLE bill_lines ADD COLUMN base_price_snapshot INTEGER;
                ALTER TABLE bill_lines ADD COLUMN extra_sheet_price_snapshot INTEGER;
                ALTER TABLE bill_lines ADD COLUMN final_print_folder_path TEXT;

                -- Seed default Album 20x20
                INSERT INTO print_specifications (canonical_name, unit_price, category, billing_method, included_sheets, base_price, extra_sheet_price, is_active, created_at, updated_at)
                SELECT 'Album 20x20', 0, 'Album', 'AlbumBasePlusExtra', 10, 400000, 20000, 1, datetime('now'), datetime('now')
                WHERE NOT EXISTS (SELECT 1 FROM print_specifications WHERE canonical_name = 'Album 20x20');

                -- Add aliases for Album 20x20
                INSERT OR IGNORE INTO print_specification_aliases (print_specification_id, alias_text, normalized_alias)
                SELECT id, 'Album 20x20', 'album 20x20'
                FROM print_specifications WHERE canonical_name = 'Album 20x20';

                INSERT OR IGNORE INTO print_specification_aliases (print_specification_id, alias_text, normalized_alias)
                SELECT id, 'Alb 20x20', 'alb 20x20'
                FROM print_specifications WHERE canonical_name = 'Album 20x20';

                INSERT OR IGNORE INTO print_specification_aliases (print_specification_id, alias_text, normalized_alias)
                SELECT id, 'A20x20', 'a20x20'
                FROM print_specifications WHERE canonical_name = 'Album 20x20';

                INSERT OR IGNORE INTO print_specification_aliases (print_specification_id, alias_text, normalized_alias)
                SELECT id, 'Album20x20', 'album20x20'
                FROM print_specifications WHERE canonical_name = 'Album 20x20';
            "
            },
            new MigrationDefinition
            {
                Version = 4,
                Name = "V2ProductFamilyAndVariants",
                Sql = @"
                -- Product Families table
                CREATE TABLE IF NOT EXISTS product_families (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    category TEXT NOT NULL,
                    billing_method TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                -- Product Family Aliases (shared across all sizes)
                CREATE TABLE IF NOT EXISTS product_family_aliases (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    family_id INTEGER NOT NULL,
                    alias_text TEXT NOT NULL,
                    normalized_alias TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(family_id) REFERENCES product_families(id) ON DELETE CASCADE
                );

                -- Product Variants table (enforces unique size per family)
                CREATE TABLE IF NOT EXISTS product_variants (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    family_id INTEGER NOT NULL,
                    canonical_size TEXT NOT NULL,
                    canonical_name TEXT NOT NULL,
                    unit_price INTEGER NOT NULL DEFAULT 0,
                    included_sheets INTEGER,
                    base_price INTEGER,
                    extra_sheet_price INTEGER,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(family_id) REFERENCES product_families(id) ON DELETE CASCADE,
                    UNIQUE(family_id, canonical_size)
                );

                -- Product-Specific Aliases
                CREATE TABLE IF NOT EXISTS product_specific_aliases (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    variant_id INTEGER NOT NULL,
                    alias_text TEXT NOT NULL,
                    normalized_alias TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(variant_id) REFERENCES product_variants(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_family_aliases_normalized ON product_family_aliases(normalized_alias);
                CREATE INDEX IF NOT EXISTS idx_variant_size ON product_variants(canonical_size);
                CREATE INDEX IF NOT EXISTS idx_specific_aliases_normalized ON product_specific_aliases(normalized_alias);

                -- Add family and canonical size columns to print_specifications for backward compatibility
                ALTER TABLE print_specifications ADD COLUMN family_id INTEGER;
                ALTER TABLE print_specifications ADD COLUMN canonical_size TEXT;

                -- Seed default product families
                INSERT OR IGNORE INTO product_families (name, category, billing_method, created_at, updated_at)
                VALUES ('Ảnh in', 'PhotoPrint', 'FileCount', datetime('now'), datetime('now'));

                INSERT OR IGNORE INTO product_families (name, category, billing_method, created_at, updated_at)
                VALUES ('Album', 'Album', 'AlbumBasePlusExtra', datetime('now'), datetime('now'));

                -- Seed family aliases for 'Ảnh in'
                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                SELECT id, 'in', 'in', datetime('now') FROM product_families WHERE name = 'Ảnh in';

                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                SELECT id, 'anh', 'anh', datetime('now') FROM product_families WHERE name = 'Ảnh in';

                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                SELECT id, 'print', 'print', datetime('now') FROM product_families WHERE name = 'Ảnh in';

                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                SELECT id, 'photo', 'photo', datetime('now') FROM product_families WHERE name = 'Ảnh in';

                -- Seed family aliases for 'Album'
                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                SELECT id, 'album', 'album', datetime('now') FROM product_families WHERE name = 'Album';

                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                SELECT id, 'ab', 'ab', datetime('now') FROM product_families WHERE name = 'Album';

                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                SELECT id, 'alb', 'alb', datetime('now') FROM product_families WHERE name = 'Album';
            ",
                PostAction = MigrateProductFamilyAndVariantsAsync
            },
            new MigrationDefinition
            {
                Version = 5,
                Name = "V2EffectiveBillingFolderAndResolutionMode",
                Sql = @"
                ALTER TABLE order_item_scans ADD COLUMN folder_resolution_mode TEXT NOT NULL DEFAULT 'AutoResolved';
                ALTER TABLE bill_lines ADD COLUMN folder_resolution_mode_snapshot TEXT NOT NULL DEFAULT 'AutoResolved';
            "
            },
            new MigrationDefinition
            {
                Version = 6,
                Name = "CustomerBillingAndAdjustments",
                Sql = @"
                CREATE TABLE IF NOT EXISTS customer_bills (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bill_number TEXT NOT NULL UNIQUE,
                    customer_id INTEGER NOT NULL,
                    customer_name_snapshot TEXT NOT NULL,
                    phone_snapshot TEXT,
                    period_start TEXT NOT NULL,
                    period_end TEXT NOT NULL,
                    status TEXT NOT NULL,
                    product_subtotal INTEGER NOT NULL DEFAULT 0,
                    adjustments_total INTEGER NOT NULL DEFAULT 0,
                    grand_total INTEGER NOT NULL DEFAULT 0,
                    note TEXT,
                    export_file_path TEXT,
                    locked_at TEXT,
                    exported_at TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(customer_id) REFERENCES customers(id)
                );

                CREATE TABLE IF NOT EXISTS customer_bill_orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bill_id INTEGER NOT NULL,
                    order_id INTEGER NOT NULL,
                    order_name_snapshot TEXT NOT NULL,
                    order_date_snapshot TEXT NOT NULL,
                    original_folder_name_snapshot TEXT NOT NULL,
                    subtotal INTEGER NOT NULL DEFAULT 0,
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    is_included INTEGER NOT NULL DEFAULT 1,
                    is_from_previous_period INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(bill_id) REFERENCES customer_bills(id) ON DELETE CASCADE,
                    FOREIGN KEY(order_id) REFERENCES orders(id)
                );

                CREATE TABLE IF NOT EXISTS customer_bill_lines (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bill_id INTEGER NOT NULL,
                    order_id INTEGER NOT NULL,
                    product_job_id INTEGER NOT NULL,
                    product_specification_id INTEGER,
                    product_name_snapshot TEXT NOT NULL,
                    variant_snapshot TEXT,
                    billing_method_snapshot TEXT NOT NULL DEFAULT 'FileCount',
                    scanned_quantity INTEGER NOT NULL,
                    billed_quantity INTEGER NOT NULL,
                    quantity_override_reason TEXT,
                    sheet_count INTEGER,
                    included_sheets_snapshot INTEGER,
                    extra_sheet_count INTEGER,
                    base_price_snapshot INTEGER,
                    extra_sheet_price_snapshot INTEGER,
                    configured_unit_price INTEGER NOT NULL DEFAULT 0,
                    billed_unit_price INTEGER NOT NULL DEFAULT 0,
                    price_override_reason TEXT,
                    line_total INTEGER NOT NULL DEFAULT 0,
                    is_included INTEGER NOT NULL DEFAULT 1,
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    final_print_folder_path TEXT,
                    folder_resolution_mode_snapshot TEXT NOT NULL DEFAULT 'AutoResolved',
                    issue_message TEXT,
                    FOREIGN KEY(bill_id) REFERENCES customer_bills(id) ON DELETE CASCADE,
                    FOREIGN KEY(order_id) REFERENCES orders(id),
                    FOREIGN KEY(product_job_id) REFERENCES order_item_scans(id)
                );

                CREATE TABLE IF NOT EXISTS customer_bill_adjustments (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bill_id INTEGER NOT NULL,
                    type TEXT NOT NULL,
                    label TEXT NOT NULL,
                    direction TEXT NOT NULL,
                    amount INTEGER NOT NULL DEFAULT 0,
                    note TEXT,
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(bill_id) REFERENCES customer_bills(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_customer_bills_customer_id ON customer_bills(customer_id);
                CREATE INDEX IF NOT EXISTS idx_customer_bills_status ON customer_bills(status);
                CREATE INDEX IF NOT EXISTS idx_customer_bill_lines_bill_id ON customer_bill_lines(bill_id);
                CREATE INDEX IF NOT EXISTS idx_customer_bill_lines_job_id ON customer_bill_lines(product_job_id);
                CREATE INDEX IF NOT EXISTS idx_customer_bill_orders_bill_id ON customer_bill_orders(bill_id);
                CREATE INDEX IF NOT EXISTS idx_customer_bill_adjustments_bill_id ON customer_bill_adjustments(bill_id);

                ALTER TABLE order_item_scans ADD COLUMN customer_bill_id INTEGER;
                CREATE INDEX IF NOT EXISTS idx_order_item_scans_bill_id ON order_item_scans(customer_bill_id);
            "
            },
            new MigrationDefinition
            {
                Version = 7,
                Name = "GuestBillingAndSourceFolders",
                PostAction = MigrateGuestBillingAndSourceFoldersAsync
            },
            new MigrationDefinition
            {
                Version = 8,
                Name = "AddNoteToCustomerBillLines",
                Sql = @"
                ALTER TABLE customer_bill_lines ADD COLUMN note TEXT;
            "
            },
            new MigrationDefinition
            {
                Version = 9,
                Name = "V2LineSpecificationFolderAndFamilyFix",
                PostAction = MigrateLineSpecificationFolderAndFamilyFixAsync
            },
            new MigrationDefinition
            {
                Version = 10,
                Name = "AddOrderCodeAndBackfill",
                PostAction = MigrateOrderCodeAndBackfillAsync
            },
            new MigrationDefinition
            {
                Version = 11,
                Name = "AddFingerprintToOrders",
                Sql = @"
                ALTER TABLE orders ADD COLUMN fingerprint TEXT;
            "
            },
            new MigrationDefinition
            {
                Version = 12,
                Name = "AddFolderPrintStatusTable",
                Sql = @"
                CREATE TABLE IF NOT EXISTS folder_print_statuses (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    folder_path TEXT NOT NULL,
                    normalized_path TEXT NOT NULL UNIQUE,
                    status INTEGER NOT NULL DEFAULT 1,
                    marked_at TEXT NOT NULL,
                    marked_by TEXT NOT NULL DEFAULT 'ExplorerContextMenu',
                    order_id INTEGER,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(order_id) REFERENCES orders(id) ON DELETE SET NULL
                );

                CREATE INDEX IF NOT EXISTS idx_folder_print_statuses_norm_path 
                ON folder_print_statuses(normalized_path);

                CREATE INDEX IF NOT EXISTS idx_folder_print_statuses_status 
                ON folder_print_statuses(status);

                ALTER TABLE orders ADD COLUMN is_printed INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE orders ADD COLUMN printed_at TEXT;
            "
            }
        };
    }

    private static async Task MigrateProductFamilyAndVariantsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ILogger? logger)
    {
        long photoFamilyId = await connection.QuerySingleAsync<long>(
            "SELECT id FROM product_families WHERE name = 'Ảnh in'", transaction: transaction);
        long albumFamilyId = await connection.QuerySingleAsync<long>(
            "SELECT id FROM product_families WHERE name = 'Album'", transaction: transaction);

        var specs = (await connection.QueryAsync<PrintSpecMigrationDto>(
            "SELECT * FROM print_specifications", transaction: transaction)).ToList();

        var existingAliases = (await connection.QueryAsync<SpecAliasMigrationDto>(
            "SELECT * FROM print_specification_aliases", transaction: transaction)).ToList();
        var aliasLookup = existingAliases.ToLookup(a => a.print_specification_id);

        foreach (var spec in specs)
        {
            long familyId = string.Equals(spec.category, "Album", StringComparison.OrdinalIgnoreCase)
                ? albumFamilyId
                : photoFamilyId;

            string canonicalSize;
            if (SizeNormalizer.TryExtractDimensions(spec.canonical_name, out var extracted, out _, out _))
            {
                canonicalSize = extracted;
            }
            else
            {
                canonicalSize = CustomerNormalizer.Normalize(spec.canonical_name);
            }

            var existingVariant = await connection.QuerySingleOrDefaultAsync<ProductVariantMigrationDto>(
                "SELECT * FROM product_variants WHERE family_id = @FamilyId AND canonical_size = @CanonicalSize LIMIT 1",
                new { FamilyId = familyId, CanonicalSize = canonicalSize },
                transaction: transaction);

            long variantIdToUse;

            if (existingVariant == null)
            {
                variantIdToUse = spec.id;
                await connection.ExecuteAsync(@"
                    INSERT INTO product_variants (
                        id, family_id, canonical_size, canonical_name, unit_price,
                        included_sheets, base_price, extra_sheet_price, is_active, created_at, updated_at
                    ) VALUES (
                        @Id, @FamilyId, @CanonicalSize, @CanonicalName, @UnitPrice,
                        @IncludedSheets, @BasePrice, @ExtraSheetPrice, @IsActive, @CreatedAt, @UpdatedAt
                    );
                ", new
                {
                    Id = spec.id,
                    FamilyId = familyId,
                    CanonicalSize = canonicalSize,
                    CanonicalName = spec.canonical_name,
                    UnitPrice = spec.unit_price,
                    IncludedSheets = spec.included_sheets,
                    BasePrice = spec.base_price,
                    ExtraSheetPrice = spec.extra_sheet_price,
                    IsActive = spec.is_active,
                    CreatedAt = spec.created_at ?? DateTimeOffset.UtcNow.ToString("o"),
                    UpdatedAt = spec.updated_at ?? DateTimeOffset.UtcNow.ToString("o")
                }, transaction: transaction);
            }
            else
            {
                logger?.LogWarning("Migration collision detected for variant size '{Size}' in family {FamilyId}. Existing ID: {ExistingId}, current spec ID: {SpecId}",
                    canonicalSize, familyId, existingVariant.id, spec.id);

                bool priceConfigMatches = (existingVariant.unit_price == spec.unit_price &&
                                           existingVariant.base_price == spec.base_price &&
                                           existingVariant.extra_sheet_price == spec.extra_sheet_price &&
                                           existingVariant.included_sheets == spec.included_sheets);

                if (priceConfigMatches)
                {
                    variantIdToUse = existingVariant.id;
                    string normSpecName = CustomerNormalizer.Normalize(spec.canonical_name);
                    await connection.ExecuteAsync(@"
                        INSERT OR IGNORE INTO product_specific_aliases (variant_id, alias_text, normalized_alias, created_at)
                        VALUES (@VariantId, @AliasText, @NormalizedAlias, @CreatedAt);
                    ", new
                    {
                        VariantId = existingVariant.id,
                        AliasText = spec.canonical_name,
                        NormalizedAlias = normSpecName,
                        CreatedAt = DateTimeOffset.UtcNow.ToString("o")
                    }, transaction: transaction);
                }
                else
                {
                    string conflictSize = $"{canonicalSize}-c{spec.id}";
                    variantIdToUse = spec.id;
                    await connection.ExecuteAsync(@"
                        INSERT INTO product_variants (
                            id, family_id, canonical_size, canonical_name, unit_price,
                            included_sheets, base_price, extra_sheet_price, is_active, created_at, updated_at
                        ) VALUES (
                            @Id, @FamilyId, @CanonicalSize, @CanonicalName, @UnitPrice,
                            @IncludedSheets, @BasePrice, @ExtraSheetPrice, @IsActive, @CreatedAt, @UpdatedAt
                        );
                    ", new
                    {
                        Id = spec.id,
                        FamilyId = familyId,
                        CanonicalSize = conflictSize,
                        CanonicalName = spec.canonical_name,
                        UnitPrice = spec.unit_price,
                        IncludedSheets = spec.included_sheets,
                        BasePrice = spec.base_price,
                        ExtraSheetPrice = spec.extra_sheet_price,
                        IsActive = spec.is_active,
                        CreatedAt = spec.created_at ?? DateTimeOffset.UtcNow.ToString("o"),
                        UpdatedAt = spec.updated_at ?? DateTimeOffset.UtcNow.ToString("o")
                    }, transaction: transaction);
                }
            }

            string normName = CustomerNormalizer.Normalize(spec.canonical_name);
            if (!string.Equals(normName, canonicalSize, StringComparison.OrdinalIgnoreCase))
            {
                await connection.ExecuteAsync(@"
                    INSERT OR IGNORE INTO product_specific_aliases (variant_id, alias_text, normalized_alias, created_at)
                    VALUES (@VariantId, @AliasText, @NormalizedAlias, @CreatedAt);
                ", new
                {
                    VariantId = variantIdToUse,
                    AliasText = spec.canonical_name,
                    NormalizedAlias = normName,
                    CreatedAt = DateTimeOffset.UtcNow.ToString("o")
                }, transaction: transaction);
            }

            await connection.ExecuteAsync(@"
                UPDATE print_specifications
                SET family_id = @FamilyId,
                    canonical_size = @CanonicalSize
                WHERE id = @Id;
            ", new
            {
                Id = spec.id,
                FamilyId = familyId,
                CanonicalSize = canonicalSize
            }, transaction: transaction);

            foreach (var alias in aliasLookup[spec.id])
            {
                await connection.ExecuteAsync(@"
                    INSERT OR IGNORE INTO product_specific_aliases (variant_id, alias_text, normalized_alias, created_at)
                    VALUES (@VariantId, @AliasText, @NormalizedAlias, @CreatedAt);
                ", new
                {
                    VariantId = variantIdToUse,
                    AliasText = alias.alias_text,
                    NormalizedAlias = alias.normalized_alias,
                    CreatedAt = DateTimeOffset.UtcNow.ToString("o")
                }, transaction: transaction);
            }
        }
    }

    private class PrintSpecMigrationDto
    {
        public long id { get; set; }
        public string canonical_name { get; set; } = string.Empty;
        public string category { get; set; } = "PhotoPrint";
        public string billing_method { get; set; } = "FileCount";
        public long unit_price { get; set; }
        public int? included_sheets { get; set; }
        public long? base_price { get; set; }
        public long? extra_sheet_price { get; set; }
        public int is_active { get; set; } = 1;
        public string? created_at { get; set; }
        public string? updated_at { get; set; }
    }

    private class SpecAliasMigrationDto
    {
        public long id { get; set; }
        public long print_specification_id { get; set; }
        public string alias_text { get; set; } = string.Empty;
        public string normalized_alias { get; set; } = string.Empty;
    }

    private class ProductVariantMigrationDto
    {
        public long id { get; set; }
        public long family_id { get; set; }
        public string canonical_size { get; set; } = string.Empty;
        public string canonical_name { get; set; } = string.Empty;
        public long unit_price { get; set; }
        public int? included_sheets { get; set; }
        public long? base_price { get; set; }
        public long? extra_sheet_price { get; set; }
        public int is_active { get; set; } = 1;
    }

    private static async Task MigrateGuestBillingAndSourceFoldersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ILogger? logger)
    {
        // 1. Ensure customer_bills has bill_type column and customer_id is nullable
        var tableInfo = (await connection.QueryAsync<TableColumnDto>(
            "PRAGMA table_info(customer_bills);", transaction: transaction)).ToList();

        bool hasBillType = tableInfo.Any(c => string.Equals(c.name, "bill_type", StringComparison.OrdinalIgnoreCase));
        var custIdCol = tableInfo.FirstOrDefault(c => string.Equals(c.name, "customer_id", StringComparison.OrdinalIgnoreCase));
        bool custIdNotNull = custIdCol != null && custIdCol.notnull == 1;

        if (!hasBillType || custIdNotNull)
        {
            logger?.LogInformation("Recreating customer_bills table to support nullable customer_id and bill_type...");

            await connection.ExecuteAsync(@"
                CREATE TABLE customer_bills_tmp (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bill_number TEXT NOT NULL UNIQUE,
                    bill_type TEXT NOT NULL DEFAULT 'Customer',
                    customer_id INTEGER,
                    customer_name_snapshot TEXT NOT NULL,
                    phone_snapshot TEXT,
                    period_start TEXT NOT NULL,
                    period_end TEXT NOT NULL,
                    status TEXT NOT NULL,
                    product_subtotal INTEGER NOT NULL DEFAULT 0,
                    adjustments_total INTEGER NOT NULL DEFAULT 0,
                    grand_total INTEGER NOT NULL DEFAULT 0,
                    note TEXT,
                    export_file_path TEXT,
                    locked_at TEXT,
                    exported_at TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(customer_id) REFERENCES customers(id)
                );
            ", transaction: transaction);

            string selectBillType = hasBillType ? "bill_type" : "'Customer'";
            await connection.ExecuteAsync($@"
                INSERT INTO customer_bills_tmp (
                    id, bill_number, bill_type, customer_id, customer_name_snapshot,
                    phone_snapshot, period_start, period_end, status, product_subtotal,
                    adjustments_total, grand_total, note, export_file_path, locked_at,
                    exported_at, created_at, updated_at
                )
                SELECT
                    id, bill_number, {selectBillType}, customer_id, customer_name_snapshot,
                    phone_snapshot, period_start, period_end, status, product_subtotal,
                    adjustments_total, grand_total, note, export_file_path, locked_at,
                    exported_at, created_at, updated_at
                FROM customer_bills;
            ", transaction: transaction);

            await connection.ExecuteAsync("DROP TABLE customer_bills;", transaction: transaction);
            await connection.ExecuteAsync("ALTER TABLE customer_bills_tmp RENAME TO customer_bills;", transaction: transaction);

            await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_customer_bills_customer_id ON customer_bills(customer_id);", transaction: transaction);
            await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_customer_bills_status ON customer_bills(status);", transaction: transaction);
            await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_customer_bills_type ON customer_bills(bill_type);", transaction: transaction);
        }

        // 2. Ensure customer_bill_orders has source_folder_path
        var orderCols = (await connection.QueryAsync<TableColumnDto>(
            "PRAGMA table_info(customer_bill_orders);", transaction: transaction)).ToList();
        if (!orderCols.Any(c => string.Equals(c.name, "source_folder_path", StringComparison.OrdinalIgnoreCase)))
        {
            await connection.ExecuteAsync("ALTER TABLE customer_bill_orders ADD COLUMN source_folder_path TEXT;", transaction: transaction);
        }

        // 3. Create guest_bill_source_folders table
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS guest_bill_source_folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                bill_id INTEGER NOT NULL,
                folder_path TEXT NOT NULL,
                normalized_folder_path TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(bill_id) REFERENCES customer_bills(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_guest_source_folders_bill_id ON guest_bill_source_folders(bill_id);
            CREATE INDEX IF NOT EXISTS idx_guest_source_folders_normalized ON guest_bill_source_folders(normalized_folder_path);
        ", transaction: transaction);
    }

    private static async Task MigrateLineSpecificationFolderAndFamilyFixAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ILogger? logger)
    {
        // 1. Ensure customer_bill_lines has specification_folder_name
        var cols = (await connection.QueryAsync<TableColumnDto>(
            "PRAGMA table_info(customer_bill_lines);", transaction: transaction)).ToList();

        if (!cols.Any(c => string.Equals(c.name, "specification_folder_name", StringComparison.OrdinalIgnoreCase)))
        {
            await connection.ExecuteAsync("ALTER TABLE customer_bill_lines ADD COLUMN specification_folder_name TEXT;", transaction: transaction);
        }

        // Backfill existing rows from order_item_scans
        await connection.ExecuteAsync(@"
            UPDATE customer_bill_lines
            SET specification_folder_name = (
                SELECT ois.specification_folder_name
                FROM order_item_scans ois
                WHERE ois.id = customer_bill_lines.product_job_id
            )
            WHERE (specification_folder_name IS NULL OR specification_folder_name = '')
              AND EXISTS (SELECT 1 FROM order_item_scans WHERE id = customer_bill_lines.product_job_id);
        ", transaction: transaction);

        // 2. Fix Product Family for 'Tranh Mica' if it was placed into 'Ảnh in' (family_id = 1)
        bool hasTranhMicaInPhotoFamily = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM product_variants
            WHERE (canonical_name LIKE '%Tranh Mica%' OR canonical_name LIKE '%Mica%')
              AND family_id = (SELECT id FROM product_families WHERE name = 'Ảnh in');
        ", transaction: transaction) > 0;

        if (hasTranhMicaInPhotoFamily)
        {
            logger?.LogInformation("Moving 'Tranh Mica' variants out of 'Ảnh in' family to dedicated 'Tranh Mica' family...");

            // Create 'Tranh Mica' family if missing
            await connection.ExecuteAsync(@"
                INSERT OR IGNORE INTO product_families (name, category, billing_method, created_at, updated_at)
                VALUES ('Tranh Mica', 'PhotoPrint', 'FileCount', datetime('now'), datetime('now'));
            ", transaction: transaction);

            long micaFamId = await connection.QuerySingleAsync<long>(
                "SELECT id FROM product_families WHERE name = 'Tranh Mica';", transaction: transaction);

            // Seed aliases for Tranh Mica
            await connection.ExecuteAsync(@"
                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                VALUES (@FamilyId, 'tranh mica', 'tranh mica', datetime('now'));
                INSERT OR IGNORE INTO product_family_aliases (family_id, alias_text, normalized_alias, created_at)
                VALUES (@FamilyId, 'mica', 'mica', datetime('now'));
            ", new { FamilyId = micaFamId }, transaction: transaction);

            // Update variants
            await connection.ExecuteAsync(@"
                UPDATE product_variants
                SET family_id = @FamilyId
                WHERE (canonical_name LIKE '%Tranh Mica%' OR canonical_name LIKE '%Mica%')
                  AND family_id = (SELECT id FROM product_families WHERE name = 'Ảnh in');
            ", new { FamilyId = micaFamId }, transaction: transaction);

            // Update print_specifications for backward compatibility
            await connection.ExecuteAsync(@"
                UPDATE print_specifications
                SET family_id = @FamilyId
                WHERE (canonical_name LIKE '%Tranh Mica%' OR canonical_name LIKE '%Mica%')
                  AND family_id = (SELECT id FROM product_families WHERE name = 'Ảnh in');
            ", new { FamilyId = micaFamId }, transaction: transaction);
        }
    }

    private static async Task MigrateOrderCodeAndBackfillAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ILogger? logger)
    {
        // 1. Add order_code to orders if missing
        var orderCols = (await connection.QueryAsync<TableColumnDto>(
            "PRAGMA table_info(orders);", transaction: transaction)).ToList();

        if (!orderCols.Any(c => string.Equals(c.name, "order_code", StringComparison.OrdinalIgnoreCase)))
        {
            await connection.ExecuteAsync("ALTER TABLE orders ADD COLUMN order_code TEXT;", transaction: transaction);
        }

        // 2. Add order_code_snapshot to customer_bill_orders if missing
        var billOrderCols = (await connection.QueryAsync<TableColumnDto>(
            "PRAGMA table_info(customer_bill_orders);", transaction: transaction)).ToList();

        if (!billOrderCols.Any(c => string.Equals(c.name, "order_code_snapshot", StringComparison.OrdinalIgnoreCase)))
        {
            await connection.ExecuteAsync("ALTER TABLE customer_bill_orders ADD COLUMN order_code_snapshot TEXT;", transaction: transaction);
        }

        // 3. Backfill order_code for existing orders that have NULL or empty order_code
        var ordersToBackfill = (await connection.QueryAsync<OrderBackfillDto>(
            "SELECT id, work_date, order_code FROM orders ORDER BY work_date, id",
            transaction: transaction)).ToList();

        var dateGroups = ordersToBackfill.GroupBy(o => o.work_date);
        foreach (var group in dateGroups)
        {
            string workDate = group.Key;
            int maxSeq = 0;

            foreach (var item in group)
            {
                if (OrderCodeGenerator.TryParseSequence(item.order_code, workDate, out int existingSeq))
                {
                    if (existingSeq > maxSeq) maxSeq = existingSeq;
                }
            }

            foreach (var item in group)
            {
                if (string.IsNullOrWhiteSpace(item.order_code))
                {
                    maxSeq++;
                    string newCode = OrderCodeGenerator.Generate(workDate, maxSeq);
                    await connection.ExecuteAsync(
                        "UPDATE orders SET order_code = @OrderCode WHERE id = @Id",
                        new { OrderCode = newCode, Id = item.id },
                        transaction: transaction);
                    item.order_code = newCode;
                }
            }
        }

        // 4. Backfill customer_bill_orders.order_code_snapshot from orders.order_code
        await connection.ExecuteAsync(@"
            UPDATE customer_bill_orders
            SET order_code_snapshot = (
                SELECT o.order_code
                FROM orders o
                WHERE o.id = customer_bill_orders.order_id
            )
            WHERE (order_code_snapshot IS NULL OR order_code_snapshot = '')
              AND EXISTS (SELECT 1 FROM orders o WHERE o.id = customer_bill_orders.order_id);
        ", transaction: transaction);

        // 5. Create unique index on orders.order_code and index on customer_bill_orders.order_code_snapshot
        await connection.ExecuteAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS idx_orders_order_code ON orders(order_code);",
            transaction: transaction);

        await connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_customer_bill_orders_order_code ON customer_bill_orders(order_code_snapshot);",
            transaction: transaction);

        logger?.LogInformation("Migration V10 (AddOrderCodeAndBackfill) completed successfully.");
    }

    private class OrderBackfillDto
    {
        public long id { get; set; }
        public string work_date { get; set; } = string.Empty;
        public string? order_code { get; set; }
    }

    private class TableColumnDto
    {
        public int cid { get; set; }
        public string name { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public int notnull { get; set; }
        public object? dflt_value { get; set; }
        public int pk { get; set; }
    }
}

