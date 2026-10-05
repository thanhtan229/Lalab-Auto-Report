import { it, expect } from 'vitest';
import { createSqliteD1 } from './sqlite-d1';
import { acceptOperation, getEventPage, getReconciliationSnapshot } from '../src/operations';
import { upsertSyncBatch, getUnpaidCustomers } from '../src/db';
import worker from '../src/index';
import { Env, SyncBatchPayload } from '../src/types';

function fixture() {
  const result = createSqliteD1();
  result.sqlite.exec("INSERT INTO cloud_orders (id, work_date, updated_at) VALUES (1, '2026-10-02', 'same'); INSERT INTO cloud_bills (id, bill_number, updated_at) VALUES (1, 'B1', 'same');");
  return result;
}
it.each([0, 1, 500, 501, 1501])('pages %i events with identical timestamps without skipping or advancing past page', async count => {
  const { db, sqlite } = fixture();
  const insert = sqlite.prepare("INSERT INTO cloud_operational_events (entity_type, entity_id, payload_json, created_at) VALUES ('order_note', 1, '{\"note\":\"same\"}', 'same')");
  for (let i = 0; i < count; i++) insert.run();
  let cursor = 0, received: number[] = [];
  do {
    const page = await getEventPage(db, cursor);
    expect(page.nextCursor).toBe(page.events.at(-1)?.id ?? cursor);
    expect(await getEventPage(db, cursor)).toEqual(page);
    received.push(...page.events.map(e => e.id)); cursor = page.nextCursor;
    if (!page.hasMore) break;
  } while (true);
  expect(received).toEqual(Array.from({ length: count }, (_, i) => i + 1));
});
it('sees a new event committed between pages', async () => {
  const { db } = fixture();
  await acceptOperation(db, { operationId: 'first-operation', entityType: 'order_note', entityId: 1, value: 'first' });
  const first = await getEventPage(db, 0, 1);
  await acceptOperation(db, { operationId: 'second-operation', entityType: 'order_note', entityId: 1, value: 'second' });
  expect((await getEventPage(db, first.nextCursor, 1)).events[0].id).toBeGreaterThan(first.nextCursor);
});
it('retries a toggle once, rejects ID reuse and preserves newer per-field revisions', async () => {
  const { db, sqlite } = fixture();
  const mutation = { operationId: 'payment-operation', entityType: 'bill_payment' as const, entityId: 1, toggle: true };
  const first = await acceptOperation(db, mutation);
  expect(JSON.parse(first.payloadJson).isPaid).toBe(true);
  const newer = await acceptOperation(db, { operationId: 'payment-newer', entityType: 'bill_payment', entityId: 1, value: false });
  expect(await acceptOperation(db, mutation)).toEqual(first);
  expect(sqlite.prepare('SELECT is_paid, payment_revision FROM cloud_bills').get()).toMatchObject({ is_paid: 0, payment_revision: newer.id });
  await expect(acceptOperation(db, { ...mutation, toggle: false, value: true })).rejects.toMatchObject({ status: 409 });
  expect((await getEventPage(db, 0)).events).toHaveLength(2);
});
it('rolls back event insert when the state update fails', async () => {
  const { db, sqlite } = fixture();
  sqlite.exec("CREATE TRIGGER fail_state BEFORE UPDATE ON cloud_orders BEGIN SELECT RAISE(ABORT, 'injected'); END;");
  const operation = { operationId: 'failed-operation', entityType: 'order_note' as const, entityId: 1, value: 'new' };
  await expect(acceptOperation(db, operation)).rejects.toThrow('injected');
  expect((await getEventPage(db, 0)).events).toHaveLength(0);
  sqlite.exec('DROP TRIGGER fail_state');
  expect((await acceptOperation(db, operation)).id).toBe(1);
});
it('projection updates cannot overwrite payment, delivery or note between pull and push', async () => {
  const { db, sqlite } = fixture();
  await acceptOperation(db, { operationId: 'delivered-operation', entityType: 'order_delivered', entityId: 1, value: true });
  await acceptOperation(db, { operationId: 'note-operation', entityType: 'order_note', entityId: 1, value: 'server' });
  await acceptOperation(db, { operationId: 'paid-operation', entityType: 'bill_payment', entityId: 1, value: true });
  const batch: SyncBatchPayload = { orders: [{ id: 1, orderCode: 'updated', customerName: 'C', folderName: 'C', workDate: '2026-10-02', status: 'Ready', isPrinted: false, isDelivered: false, note: 'stale', hasIssues: false, isLocked: false, hasThumbnail: false, totalQuantity: 1, itemsJson: '[]' }],
    bills: [{ id: 1, billNumber: 'B1', billType: 'Customer', customerNameSnapshot: 'C', periodDate: '2026-10-02', totalAmount: 75000, isPaid: false, linesJson: '[]', adjustmentsJson: '[]', hasJpeg: false }] };
  await upsertSyncBatch(db, batch);
  expect(sqlite.prepare('SELECT is_delivered, note, order_code FROM cloud_orders').get()).toMatchObject({ is_delivered: 1, note: 'server', order_code: 'updated' });
  expect(sqlite.prepare('SELECT is_paid, total_amount FROM cloud_bills').get()).toMatchObject({ is_paid: 1, total_amount: 75000 });
});
it.each(['-1', '1.5', 'NaN', '9007199254740992'])('rejects malformed cursor %s at the HTTP boundary', async cursor => {
  const { db } = fixture();
  const response = await worker.fetch(new Request('https://local/api/sync/v2/pull?cursor=' + cursor, { headers: { 'X-Sync-Secret': 'test-configured-secret' } }), { DB: db, SYNC_SECRET: 'test-configured-secret' } as Env, {} as ExecutionContext);
  expect(response.status).toBe(400);
});
it('negotiates V2 and retains the legacy pull response', async () => {
  const { db } = fixture(); const env = { DB: db, SYNC_SECRET: 'test-configured-secret' } as Env;
  for (const route of ['v2/pull?cursor=0', 'pull']) {
    const response = await worker.fetch(new Request('https://local/api/sync/' + route, { headers: { 'X-Sync-Secret': env.SYNC_SECRET! } }), env, {} as ExecutionContext);
    expect(response.status).toBe(200);
    const body = await response.json() as any;
    expect(Array.isArray(body.events)).toBe(true);
    if (route.startsWith('v2')) expect(body.protocolVersion).toBe(2);
  }
});
it('captures revision-zero bootstrap fields and current cursor in one reconciliation snapshot', async () => {
  const { db } = fixture();
  const before = await getReconciliationSnapshot(db); expect(before.cursor).toBe(0); expect(before.fields).toHaveLength(3);
  await acceptOperation(db, { operationId: 'snapshot-note', entityType: 'order_note', entityId: 1, value: 'latest' });
  const after = await getReconciliationSnapshot(db); expect(after.streamId).toBe(before.streamId); expect(after.cursor).toBe(1);
  expect(after.fields.find((f: any) => f.entityType === 'order_note')).toMatchObject({ revision: 1, payloadJson: '{"note":"latest"}' });
});
it('requires the configured sync secret for reconciliation', async () => {
  const { db } = fixture();
  const response = await worker.fetch(new Request('https://local/api/sync/v2/reconciliation'), {DB:db,SYNC_SECRET:'configured-secret'} as Env, {} as ExecutionContext);
  expect(response.status).toBe(403);
});
it('debt detail includes bill contract and reflects server payment even if Desktop cached report is stale', async () => {
  const { db, sqlite } = fixture();
  sqlite.exec("UPDATE cloud_bills SET total_amount=75000, customer_name_snapshot='Fixture', period_date='2026-10-02'; INSERT INTO cloud_reports(report_key,data_json,updated_at) VALUES ('unpaid_customers','[{\"totalDebt\":75000}]','same');");
  expect((await getUnpaidCustomers(db))[0]).toMatchObject({ totalDebt: 75000, bills: [{billNumber:'B1',grandTotal:75000}] });
  await acceptOperation(db,{operationId:'collect-payment',entityType:'bill_payment',entityId:1,value:true});
  expect(await getUnpaidCustomers(db)).toEqual([]);
});

it.each([0, -1, 501])('rejects invalid limit %i at the query boundary', async limit => {
  const { db } = fixture();
  await expect(getEventPage(db, 0, limit)).rejects.toMatchObject({ status: 400 });
});

it.each([1, 500])('accepts valid boundary limit %i', async limit => {
  const { db } = fixture();
  const page = await getEventPage(db, 0, limit);
  expect(page.success).toBe(true);
  expect(page.events).toEqual([]);
});

it('handles concurrent duplicate operation requests idempotently', async () => {
  const { db, sqlite } = fixture();
  const mutation = { operationId: 'concurrent-op-1', entityType: 'order_note' as const, entityId: 1, value: 'concurrent-val' };
  const [res1, res2] = await Promise.all([
    acceptOperation(db, mutation),
    acceptOperation(db, mutation)
  ]);
  expect(res1).toEqual(res2);
  const rows = sqlite.prepare("SELECT * FROM cloud_operational_events WHERE operation_id = 'concurrent-op-1'").all();
  expect(rows).toHaveLength(1);
});

it('assigns monotonically increasing revisions for sequential mutations', async () => {
  const { db, sqlite } = fixture();
  const m1 = await acceptOperation(db, { operationId: 'seq-op-1', entityType: 'order_note', entityId: 1, value: 'val-1' });
  const m2 = await acceptOperation(db, { operationId: 'seq-op-2', entityType: 'order_note', entityId: 1, value: 'val-2' });
  expect(m2.id).toBeGreaterThan(m1.id);
  const order = sqlite.prepare('SELECT note, note_revision FROM cloud_orders WHERE id = 1').get() as any;
  expect(order.note).toBe('val-2');
  expect(order.note_revision).toBe(m2.id);
});

it('verifies projection batch sync never creates operational events or mutates revisions', async () => {
  const { db, sqlite } = fixture();
  const m = await acceptOperation(db, { operationId: 'proj-op-1', entityType: 'order_delivered', entityId: 1, value: true });
  expect(m.id).toBe(1);

  const batch: SyncBatchPayload = {
    orders: [{
      id: 1, orderCode: 'ORD-1', customerName: 'C1', folderName: 'F1', workDate: '2026-10-02',
      status: 'Done', isPrinted: true, isDelivered: false, note: 'ignore', hasIssues: false,
      isLocked: true, hasThumbnail: false, totalQuantity: 10, itemsJson: '[]'
    }],
    bills: [{
      id: 1, billNumber: 'B1', billType: 'Customer', customerNameSnapshot: 'C1', periodDate: '2026-10-02',
      totalAmount: 100000, isPaid: false, linesJson: '[]', adjustmentsJson: '[]', hasJpeg: false
    }]
  };

  // Run batch twice to verify idempotence and zero side effects
  await upsertSyncBatch(db, batch);
  await upsertSyncBatch(db, batch);

  const eventsCount = sqlite.prepare('SELECT COUNT(*) as cnt FROM cloud_operational_events').get() as any;
  expect(eventsCount.cnt).toBe(1); // Only the initial mutation event, zero events from batch sync

  const order = sqlite.prepare('SELECT is_delivered, delivery_revision, note_revision, status FROM cloud_orders WHERE id = 1').get() as any;
  expect(order.is_delivered).toBe(1);
  expect(order.delivery_revision).toBe(1);
  expect(order.note_revision).toBe(0);
  expect(order.status).toBe('Done');

  const bill = sqlite.prepare('SELECT is_paid, payment_revision, total_amount FROM cloud_bills WHERE id = 1').get() as any;
  expect(bill.is_paid).toBe(0);
  expect(bill.payment_revision).toBe(0);
  expect(bill.total_amount).toBe(100000);
});

it('preserves unknown/future event types without skipping or dropping them', async () => {
  const { db, sqlite } = fixture();
  sqlite.exec("INSERT INTO cloud_operational_events (entity_type, entity_id, payload_json, created_at, operation_id) VALUES ('unknown_future_event', 99, '{\"meta\":\"future\"}', '2026-10-05T00:00:00Z', 'future-op-123')");
  const page = await getEventPage(db, 0);
  expect(page.events).toHaveLength(1);
  expect(page.events[0]).toMatchObject({
    id: 1,
    entityType: 'unknown_future_event',
    entityId: 99,
    payloadJson: '{"meta":"future"}',
    operationId: 'future-op-123'
  });
});

it('accepts deliveredBy as null without rejecting as invalid actor', async () => {
  const { db } = fixture();
  const mutation = { operationId: 'deliv-null-op', entityType: 'order_delivered' as const, entityId: 1, toggle: true, deliveredBy: null };
  const res = await acceptOperation(db, mutation);
  expect(res.operationId).toBe('deliv-null-op');
  const payload = JSON.parse(res.payloadJson);
  expect(payload.isDelivered).toBe(true);
  expect(payload.deliveredBy).toBe('Mobile');
});


