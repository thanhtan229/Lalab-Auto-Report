import { describe, it, expect } from 'vitest';
import worker from '../src/index';
import { createAuthToken } from '../src/auth';
import { Env } from '../src/types';

describe('Financial role boundary', () => {
  const env = { JWT_SECRET: 'configured-test-key', DB: {
    prepare() { return { bind() { return this; }, async first() { return {
      id: 1, bill_number: 'BILL', customer_name_snapshot: 'Customer', period_date: '2026-10-02',
      total_amount: 75000, lines_json: '[{"id":1,"productName":"Photo","quantity":15,"unitPrice":5000,"lineTotal":75000}]', adjustments_json: '[]'
    }; } }; }
  } } as unknown as Env;
  const ctx = {} as ExecutionContext;
  it.each(['toggle-payment', 'image'])('refuses Staff financial %s', async route => {
    const token = await createAuthToken('Staff', env.JWT_SECRET);
    const res = await worker.fetch(new Request('https://local/api/bills/1/' + route, {
      method: route === 'toggle-payment' ? 'POST' : 'GET', headers: { Authorization: 'Bearer ' + token }
    }), env, ctx);
    expect(res.status).toBe(403);
  });
  it('redacts Staff bill amounts while Admin retains them', async () => {
    for (const role of ['Staff', 'Admin'] as const) {
      const token = await createAuthToken(role, env.JWT_SECRET);
      const res = await worker.fetch(new Request('https://local/api/bills/1', { headers: { Authorization: 'Bearer ' + token } }), env, ctx);
      expect(res.status).toBe(200);
      const body = await res.json() as any;
      expect(body.grandTotal).toBe(role === 'Admin' ? 75000 : null);
      expect(body.lines[0].lineTotal).toBe(role === 'Admin' ? 75000 : null);
      if (role === 'Staff') expect(JSON.stringify(body)).not.toContain('75000');
    }
  });
  it('refuses unauthenticated financial access', async () => {
    expect((await worker.fetch(new Request('https://local/api/bills/1/image'), env, ctx)).status).toBe(401);
  });
});
