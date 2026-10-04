import { OperationalEventDto } from './types';

export interface OperationalMutation {
  operationId: string;
  entityType: 'order_delivered' | 'order_note' | 'bill_payment';
  entityId: number;
  value?: boolean | string;
  toggle?: boolean;
  deliveredBy?: string;
}

export class OperationError extends Error {
  constructor(message: string, public status = 400) { super(message); }
}

function mapEvent(row: any): OperationalEventDto {
  return { id: row.id, entityType: row.entity_type, entityId: row.entity_id,
    payloadJson: row.payload_json, createdAt: row.created_at, operationId: row.operation_id ?? null };
}

// The event insert and state update share one D1 transaction. A retried ID reads
// its original result; it never toggles again or acquires a newer revision.
export async function acceptOperation(db: D1Database, mutation: OperationalMutation): Promise<OperationalEventDto> {
  const { operationId, entityType, entityId } = mutation;
  if (typeof operationId !== 'string' || !/^[A-Za-z0-9_-]{8,128}$/.test(operationId)
    || !Number.isSafeInteger(entityId) || entityId <= 0) throw new OperationError('Invalid operation identity.');
  if (!['order_delivered', 'order_note', 'bill_payment'].includes(entityType)) throw new OperationError('Unknown operation type.');
  if (entityType === 'order_note' ? typeof mutation.value !== 'string' || mutation.value.length > 10000
    : mutation.toggle !== true && typeof mutation.value !== 'boolean') throw new OperationError('Invalid operation value.');
  if (mutation.deliveredBy !== undefined && (typeof mutation.deliveredBy !== 'string' || mutation.deliveredBy.length > 200))
    throw new OperationError('Invalid actor.');
  const requestJson = JSON.stringify({ entityType, entityId, value: mutation.value ?? null,
    toggle: mutation.toggle === true, deliveredBy: mutation.deliveredBy ?? null });
  const now = new Date().toISOString();
  const table = entityType === 'bill_payment' ? 'cloud_bills' : 'cloud_orders';
  let payloadSql: string;
  let args: any[];
  let assignments: string;
  let revision: string;
  if (entityType === 'order_note') {
    payloadSql = "json_object('note', ?)"; args = [mutation.value];
    revision = 'note_revision'; assignments = "note = json_extract(e.payload_json, '$.note')";
  } else {
    const payment = entityType === 'bill_payment';
    const column = payment ? 'is_paid' : 'is_delivered';
    const stateSql = mutation.toggle ? `CASE WHEN ${column} = 0 THEN 1 ELSE 0 END` : '?';
    const stateArgs = mutation.toggle ? [] : [mutation.value ? 1 : 0];
    payloadSql = payment
      ? `json_object('isPaid', json(CASE WHEN (${stateSql}) = 1 THEN 'true' ELSE 'false' END), 'paidAt', CASE WHEN (${stateSql}) = 1 THEN ? ELSE NULL END)`
      : `json_object('isDelivered', json(CASE WHEN (${stateSql}) = 1 THEN 'true' ELSE 'false' END), 'deliveredAt', CASE WHEN (${stateSql}) = 1 THEN ? ELSE NULL END, 'deliveredBy', CASE WHEN (${stateSql}) = 1 THEN ? ELSE NULL END)`;
    args = payment ? [...stateArgs, ...stateArgs, now] : [...stateArgs, ...stateArgs, now, ...stateArgs, mutation.deliveredBy ?? 'Mobile'];
    revision = payment ? 'payment_revision' : 'delivery_revision';
    assignments = payment ? "is_paid = json_extract(e.payload_json, '$.isPaid'), paid_at = json_extract(e.payload_json, '$.paidAt')"
      : "is_delivered = json_extract(e.payload_json, '$.isDelivered'), delivered_at = json_extract(e.payload_json, '$.deliveredAt'), delivered_by = json_extract(e.payload_json, '$.deliveredBy')";
  }
  await db.batch([
    db.prepare(`INSERT INTO cloud_operational_events (entity_type, entity_id, operation_id, request_json, payload_json, created_at)
      SELECT ?, id, ?, ?, ${payloadSql}, ? FROM ${table} WHERE id = ?
      ON CONFLICT(operation_id) DO NOTHING`).bind(entityType, operationId, requestJson, ...args, now, entityId),
    db.prepare(`UPDATE ${table} SET ${assignments}, ${revision} = e.id, updated_at = e.created_at
      FROM cloud_operational_events e WHERE ${table}.id = ? AND e.operation_id = ?
      AND e.entity_type = ? AND e.entity_id = ? AND e.request_json = ? AND ${table}.${revision} < e.id`)
      .bind(entityId, operationId, entityType, entityId, requestJson)
  ]);
  const row = await db.prepare('SELECT * FROM cloud_operational_events WHERE operation_id = ?').bind(operationId).first<any>();
  if (!row) throw new OperationError('Entity not found.', 404);
  if (row.request_json !== requestJson) throw new OperationError('Operation ID already used for a different request.', 409);
  return mapEvent(row);
}

export async function getEventPage(db: D1Database, cursor: number, limit = 500) {
  if (!Number.isSafeInteger(cursor) || cursor < 0 || !Number.isInteger(limit) || limit < 1 || limit > 500)
    throw new OperationError('Invalid cursor or limit.');
  const rows = (await db.prepare('SELECT * FROM cloud_operational_events WHERE id > ? ORDER BY id ASC LIMIT ?')
    .bind(cursor, limit + 1).all<any>()).results ?? [];
  const events = rows.slice(0, limit).map(mapEvent);
  const streamId = (await db.prepare("SELECT value FROM cloud_sync_meta WHERE key = 'event_stream_id'").first<{ value: string }>())?.value;
  if (!streamId) throw new OperationError("Event stream is not initialized.", 503);
  return { success: true, protocolVersion: 2, streamId, events,
    nextCursor: events.length ? events[events.length - 1].id : cursor, hasMore: rows.length > limit };
}

// One SQLite statement captures the cursor and current fields from the same
// read snapshot, including bootstrap fields that have never produced an event.
export async function getReconciliationSnapshot(db: D1Database) {
  const row = await db.prepare(`SELECT json_object('cursor', COALESCE((SELECT MAX(id) FROM cloud_operational_events),0),
    'streamId', (SELECT value FROM cloud_sync_meta WHERE key='event_stream_id'),
    'fields', json((SELECT json_group_array(json_object('entityType',entity_type,'entityId',entity_id,'revision',revision,'payloadJson',payload_json)) FROM (
      SELECT 'order_note' entity_type,id entity_id,note_revision revision,json_object('note',COALESCE(note,'')) payload_json FROM cloud_orders
      UNION ALL SELECT 'order_delivered',id,delivery_revision,json_object('isDelivered',json(CASE WHEN is_delivered=1 THEN 'true' ELSE 'false' END),'deliveredAt',delivered_at,'deliveredBy',delivered_by) FROM cloud_orders
      UNION ALL SELECT 'bill_payment',id,payment_revision,json_object('isPaid',json(CASE WHEN is_paid=1 THEN 'true' ELSE 'false' END),'paidAt',paid_at) FROM cloud_bills
    )))) snapshot`).first<{ snapshot: string }>();
  return { success: true, protocolVersion: 2, ...JSON.parse(row!.snapshot) };
}
