-- 0001_init.sql: Cloud Read Replica Schema for Lalab Auto Report

CREATE TABLE IF NOT EXISTS cloud_orders (
    id INTEGER PRIMARY KEY,
    order_code TEXT,
    customer_name TEXT,
    folder_name TEXT,
    work_date TEXT NOT NULL,
    status TEXT,
    is_printed INTEGER DEFAULT 0,
    printed_at TEXT,
    is_delivered INTEGER DEFAULT 0,
    delivered_at TEXT,
    delivered_by TEXT,
    note TEXT,
    has_issues INTEGER DEFAULT 0,
    is_locked INTEGER DEFAULT 0,
    has_thumbnail INTEGER DEFAULT 0,
    thumbnail_file_name TEXT,
    customer_bill_id INTEGER,
    total_quantity INTEGER DEFAULT 0,
    items_json TEXT,
    updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_orders_work_date ON cloud_orders(work_date);
CREATE INDEX IF NOT EXISTS idx_orders_customer ON cloud_orders(customer_name);

CREATE TABLE IF NOT EXISTS cloud_bills (
    id INTEGER PRIMARY KEY,
    bill_number TEXT UNIQUE,
    bill_type TEXT,
    customer_id INTEGER,
    customer_name_snapshot TEXT,
    period_date TEXT,
    total_amount INTEGER DEFAULT 0,
    is_paid INTEGER DEFAULT 0,
    paid_at TEXT,
    lines_json TEXT,
    adjustments_json TEXT,
    has_jpeg INTEGER DEFAULT 0,
    locked_at TEXT,
    exported_at TEXT,
    updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_bills_period ON cloud_bills(period_date);
CREATE INDEX IF NOT EXISTS idx_bills_customer ON cloud_bills(customer_name_snapshot);
CREATE INDEX IF NOT EXISTS idx_bills_paid ON cloud_bills(is_paid);

CREATE TABLE IF NOT EXISTS cloud_reports (
    report_key TEXT PRIMARY KEY,
    data_json TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS cloud_sync_meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
