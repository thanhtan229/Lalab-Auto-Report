-- Applied once through D1's migration ledger. Legacy events retain their IDs.
ALTER TABLE cloud_operational_events ADD COLUMN operation_id TEXT;
ALTER TABLE cloud_operational_events ADD COLUMN request_json TEXT;
CREATE UNIQUE INDEX idx_events_operation ON cloud_operational_events(operation_id);
ALTER TABLE cloud_orders ADD COLUMN delivery_revision INTEGER NOT NULL DEFAULT 0;
ALTER TABLE cloud_orders ADD COLUMN note_revision INTEGER NOT NULL DEFAULT 0;
ALTER TABLE cloud_bills ADD COLUMN payment_revision INTEGER NOT NULL DEFAULT 0;
UPDATE cloud_orders SET delivery_revision = COALESCE((SELECT MAX(id) FROM cloud_operational_events WHERE entity_type = 'order_delivered' AND entity_id = cloud_orders.id), 0),
 note_revision = COALESCE((SELECT MAX(id) FROM cloud_operational_events WHERE entity_type = 'order_note' AND entity_id = cloud_orders.id), 0);
UPDATE cloud_bills SET payment_revision = COALESCE((SELECT MAX(id) FROM cloud_operational_events WHERE entity_type = 'bill_payment' AND entity_id = cloud_bills.id), 0);
