import { it, expect } from 'vitest';
import { createSqliteD1 } from './sqlite-d1';
import { claimPrintCommand, createPrintCommand, getPendingPrintCommands, updatePrintCommandStatus, upsertSyncBatch, getBillById } from '../src/db';
import { toMobileBill } from '../src/mobile-bill';
import worker from '../src/index';
import { Env } from '../src/types';

it('claims once for competing consumers; retains PROCESSING across polls without automatic physical retry', async () => {
  const { db } = createSqliteD1();
  const command = await createPrintCommand(db, 1, null, 'print_order_label', 'Staff');
  const results = await Promise.all([claimPrintCommand(db, command.id, 'consumer-one-token'), claimPrintCommand(db, command.id, 'consumer-two-token')]);
  expect(results.filter(Boolean)).toHaveLength(1);
  expect(await getPendingPrintCommands(db)).toEqual([]);
  expect(await claimPrintCommand(db, command.id, 'consumer-three-token')).toBe(false);
  expect(await updatePrintCommandStatus(db, command.id, 'COMPLETED', null, 'wrong-owner-token')).toBe(false);
  expect(await updatePrintCommandStatus(db, command.id, 'COMPLETED', null, 'consumer-one-token')).toBe(true);
  // Lost response: terminal ACK is idempotent, never returned to PENDING.
  expect(await updatePrintCommandStatus(db, command.id, 'COMPLETED', null, 'consumer-one-token')).toBe(true);
  expect(await updatePrintCommandStatus(db, command.id, 'FAILED', 'bad retry', 'consumer-one-token')).toBe(false);
  expect(await getPendingPrintCommands(db)).toEqual([]);
});

it('stores authoritative product and adjustment snapshots through SQL projection and mobile contract', async () => {
  const { db } = createSqliteD1();
  await upsertSyncBatch(db, { bills: [{ id: 1, billNumber: 'B1', billType: 'Customer', customerNameSnapshot: 'Fixture', periodDate: '2026-10-02', totalAmount: 70000, productSubtotal: 75000, adjustmentsTotal: -5000, isPaid: false, hasJpeg: false,
    linesJson: JSON.stringify([{ id: 1, description: 'Included', quantity: 15, unitPrice: 5000, lineTotal: 75000 }]), adjustmentsJson: JSON.stringify([{ id: 1, amount: 5000, direction: 'Deduct', type: 'Discount' }]) }] });
  const stored = (await getBillById(db, 1))!;
  const admin = toMobileBill(stored, 'Admin');
  expect(admin.productSubtotal).toBe(75000);
  expect(admin.adjustmentsTotal).toBe(-5000);
  expect(admin.grandTotal).toBe(70000);
  expect(admin.lines).toHaveLength(1);
  expect(toMobileBill(stored, 'Staff').productSubtotal).toBeNull();
  // Even inconsistent historical lines cannot override a persisted snapshot.
  expect(toMobileBill({ ...stored, lines: [...stored.lines, { id: 2, quantity: 5, unitPrice: 5000, lineTotal: 25000 }] }, 'Admin').productSubtotal).toBe(75000);
});

it('HTTP rejects legacy unclaimed ACK, wrong claim owner and a terminal-to-pending retry', async () => {
  const { db } = createSqliteD1();
  const command = await createPrintCommand(db, 1, null, 'print_order_label', 'Staff');
  const env = { DB: db, SYNC_SECRET: 'synthetic-sync-secret' } as Env;
  const ctx = {} as ExecutionContext;
  async function post(action: string, body: object) {
    return worker.fetch(new Request(`https://test/api/sync/print-commands/${command.id}/${action}`, {
      method: 'POST', headers: { 'X-Sync-Secret': env.SYNC_SECRET!, 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    }), env, ctx);
  }
  expect((await post('status', { status: 'COMPLETED' })).status).toBe(400);
  expect((await post('claim', { claimToken: 'consumer-one-token' })).status).toBe(200);
  expect((await post('claim', { claimToken: 'consumer-two-token' })).status).toBe(409);
  expect((await post('status', { claimToken: 'consumer-two-token', status: 'COMPLETED' })).status).toBe(409);
  expect((await post('status', { claimToken: 'consumer-one-token', status: 'PENDING' })).status).toBe(400);
  expect((await post('status', { claimToken: 'consumer-one-token', status: 'FAILED', errorMessage: 'Inspect printer manually' })).status).toBe(200);
  expect(await getPendingPrintCommands(db)).toEqual([]);
});
