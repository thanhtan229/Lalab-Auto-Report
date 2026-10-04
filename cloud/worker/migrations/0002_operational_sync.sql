-- 0002_operational_sync.sql: Two-Way Operational Status Events Schema

CREATE TABLE IF NOT EXISTS cloud_operational_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    entity_type TEXT NOT NULL, -- 'order_delivered', 'bill_payment', 'order_note'
    entity_id INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_events_created ON cloud_operational_events(created_at);
CREATE INDEX IF NOT EXISTS idx_events_entity ON cloud_operational_events(entity_type, entity_id);
