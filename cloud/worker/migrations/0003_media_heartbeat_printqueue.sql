-- 0003_media_heartbeat_printqueue.sql: Schema for Thumbnail, Device Heartbeat, and Print Commands

-- 1. Order Thumbnail Metadata
ALTER TABLE cloud_orders ADD COLUMN thumbnail_status TEXT NOT NULL DEFAULT 'NONE';
ALTER TABLE cloud_orders ADD COLUMN thumbnail_key TEXT;
ALTER TABLE cloud_orders ADD COLUMN thumbnail_version TEXT;

-- 2. Device Heartbeat Tracking
CREATE TABLE IF NOT EXISTS cloud_heartbeats (
    device_id TEXT PRIMARY KEY,
    lan_url TEXT NOT NULL,
    app_version TEXT,
    last_heartbeat_at TEXT NOT NULL
);

-- 3. Cloud Remote Print Command Queue
CREATE TABLE IF NOT EXISTS cloud_print_commands (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    order_id INTEGER NOT NULL,
    bill_id INTEGER,
    command_type TEXT NOT NULL, -- 'print_order_label', 'print_bill_label'
    status TEXT NOT NULL DEFAULT 'PENDING', -- 'PENDING', 'PROCESSING', 'COMPLETED', 'FAILED'
    requested_by TEXT,
    created_at TEXT NOT NULL,
    processed_at TEXT,
    error_message TEXT
);

CREATE INDEX IF NOT EXISTS idx_print_commands_status ON cloud_print_commands(status);
CREATE INDEX IF NOT EXISTS idx_print_commands_order ON cloud_print_commands(order_id);
