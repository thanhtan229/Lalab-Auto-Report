import { describe, it, expect } from 'vitest';
import worker from '../src/index';
import { createAuthToken, verifyAuthToken } from '../src/auth';
import { Env } from '../src/types';

const ctx = {} as ExecutionContext;
const configured: Env = {
  DB: {
    prepare: () => ({
      bind: () => ({
        first: async () => ({ value: 'test-stream-id' }),
        all: async () => ({ results: [] }),
        run: async () => ({ success: true })
      }),
      first: async () => ({ value: 'test-stream-id' }),
      all: async () => ({ results: [] }),
      run: async () => ({ success: true })
    }),
    batch: async () => []
  } as unknown as D1Database,
  JWT_SECRET: 'random-test-secret-32-chars-long!',
  SYNC_SECRET: 'random-sync-secret-32-chars-long!',
  ADMIN_PIN: '986754',
  STAFF_PIN: '123987'
};

async function createForgedToken(payloadObj: object, rawSecret: string): Promise<string> {
  const encoder = new TextEncoder();
  const payloadJson = JSON.stringify(payloadObj);
  const payloadEncoded = btoa(payloadJson).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  const key = await crypto.subtle.importKey(
    'raw',
    encoder.encode(rawSecret),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  const sig = await crypto.subtle.sign('HMAC', key, encoder.encode(payloadEncoded));
  let binary = '';
  const bytes = new Uint8Array(sig);
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  const sigEncoded = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${payloadEncoded}.${sigEncoded}`;
}

describe('Worker Auth Hardening Suite (16 security test gates)', () => {
  // Gate 1: JWT_SECRET missing
  it('1. rejects missing JWT_SECRET (undefined) with 503 fail closed', async () => {
    const env = { ...configured, JWT_SECRET: undefined };
    const res = await worker.fetch(new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"986754"}' }), env, ctx);
    expect(res.status).toBe(503);
    expect(await verifyAuthToken('valid.signature', undefined)).toBeNull();
  });

  // Gate 2: JWT_SECRET blank / whitespace
  it('2. rejects blank JWT_SECRET with 503 fail closed', async () => {
    for (const blank of ['', '   ', '\t\n']) {
      const env = { ...configured, JWT_SECRET: blank };
      const res = await worker.fetch(new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"986754"}' }), env, ctx);
      expect(res.status).toBe(503);
      expect(await verifyAuthToken('valid.signature', blank)).toBeNull();
    }
  });

  // Gate 3: historical/default JWT key rejected as configured secret
  it('3. rejects historical/default JWT key as configured secret with 503', async () => {
    const env = { ...configured, JWT_SECRET: 'lalab-secret-salt-2026-tinix' };
    const res = await worker.fetch(new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"986754"}' }), env, ctx);
    expect(res.status).toBe(503);
    expect(await verifyAuthToken('any.signature', 'lalab-secret-salt-2026-tinix')).toBeNull();
  });

  // Gate 4: forged Admin token using default/source-known key rejected
  it('4. rejects forged Admin token signed with historical/default key', async () => {
    const exp = Math.floor(Date.now() / 1000) + 3600;
    const forgedToken = await createForgedToken({ role: 'Admin', exp }, 'lalab-secret-salt-2026-tinix');

    // Token must be rejected by verifyAuthToken
    expect(await verifyAuthToken(forgedToken, configured.JWT_SECRET)).toBeNull();

    // Token must be rejected by worker protected route
    const res = await worker.fetch(
      new Request('https://local/api/auth/me', { headers: { Authorization: `Bearer ${forgedToken}` } }),
      configured,
      ctx
    );
    expect(res.status).toBe(401);
  });

  // Gate 5: valid configured JWT accepted for Admin
  it('5. accepts valid configured JWT for Admin role', async () => {
    const token = await createAuthToken('Admin', configured.JWT_SECRET);
    const payload = await verifyAuthToken(token, configured.JWT_SECRET);
    expect(payload?.role).toBe('Admin');
    expect(payload?.exp).toBeGreaterThan(Math.floor(Date.now() / 1000));

    const res = await worker.fetch(
      new Request('https://local/api/auth/me', { headers: { Authorization: `Bearer ${token}` } }),
      configured,
      ctx
    );
    expect(res.status).toBe(200);
    const body = await res.json() as { authenticated: boolean; role: string };
    expect(body.authenticated).toBe(true);
    expect(body.role).toBe('Admin');
  });

  // Gate 6: valid configured JWT accepted for Staff
  it('6. accepts valid configured JWT for Staff role', async () => {
    const token = await createAuthToken('Staff', configured.JWT_SECRET);
    const payload = await verifyAuthToken(token, configured.JWT_SECRET);
    expect(payload?.role).toBe('Staff');

    const res = await worker.fetch(
      new Request('https://local/api/auth/me', { headers: { Authorization: `Bearer ${token}` } }),
      configured,
      ctx
    );
    expect(res.status).toBe(200);
    const body = await res.json() as { authenticated: boolean; role: string };
    expect(body.authenticated).toBe(true);
    expect(body.role).toBe('Staff');
  });

  // Gate 7: expired JWT rejected
  it('7. rejects expired JWT with null verification and 401', async () => {
    const expiredToken = await createAuthToken('Admin', configured.JWT_SECRET, -1);
    expect(await verifyAuthToken(expiredToken, configured.JWT_SECRET)).toBeNull();

    const res = await worker.fetch(
      new Request('https://local/api/auth/me', { headers: { Authorization: `Bearer ${expiredToken}` } }),
      configured,
      ctx
    );
    expect(res.status).toBe(401);
  });

  // Gate 8: invalid/unknown role rejected
  it('8. rejects invalid/unknown role in token payload', async () => {
    const badRoleToken = await createAuthToken('Owner' as any, configured.JWT_SECRET);
    expect(await verifyAuthToken(badRoleToken, configured.JWT_SECRET)).toBeNull();

    const exp = Math.floor(Date.now() / 1000) + 3600;
    const guestToken = await createForgedToken({ role: 'Guest', exp }, configured.JWT_SECRET!);
    expect(await verifyAuthToken(guestToken, configured.JWT_SECRET)).toBeNull();
  });

  // Gate 9: malformed token structure rejected
  it('9. rejects malformed token structure (no dot, empty, extra segments)', async () => {
    expect(await verifyAuthToken('', configured.JWT_SECRET)).toBeNull();
    expect(await verifyAuthToken('no-dot-token', configured.JWT_SECRET)).toBeNull();
    expect(await verifyAuthToken('part1.part2.part3', configured.JWT_SECRET)).toBeNull();
    expect(await verifyAuthToken('.', configured.JWT_SECRET)).toBeNull();
  });

  // Gate 10: malformed payload rejected
  it('10. rejects malformed payload (invalid base64, non-JSON, missing/invalid exp)', async () => {
    expect(await verifyAuthToken('!!!.???', configured.JWT_SECRET)).toBeNull();

    const nonJsonToken = btoa('not-a-json-payload') + '.invalidsig';
    expect(await verifyAuthToken(nonJsonToken, configured.JWT_SECRET)).toBeNull();

    const missingExp = await createForgedToken({ role: 'Admin' }, configured.JWT_SECRET!);
    expect(await verifyAuthToken(missingExp, configured.JWT_SECRET)).toBeNull();

    const stringExp = await createForgedToken({ role: 'Admin', exp: 'infinite' }, configured.JWT_SECRET!);
    expect(await verifyAuthToken(stringExp, configured.JWT_SECRET)).toBeNull();
  });

  // Gate 11: SYNC_SECRET missing
  it('11. rejects missing SYNC_SECRET (undefined) with 503 fail closed', async () => {
    const env = { ...configured, SYNC_SECRET: undefined };
    const res1 = await worker.fetch(
      new Request('https://local/api/sync/batch', { method: 'POST', headers: { 'X-Sync-Secret': 'any' }, body: '{}' }),
      env,
      ctx
    );
    expect(res1.status).toBe(503);

    const res2 = await worker.fetch(
      new Request('https://local/api/sync/v2/pull', { method: 'GET', headers: { 'X-Sync-Secret': 'any' } }),
      env,
      ctx
    );
    expect(res2.status).toBe(503);
  });

  // Gate 12: SYNC_SECRET blank
  it('12. rejects blank SYNC_SECRET with 503 fail closed', async () => {
    for (const blank of ['', '   ']) {
      const env = { ...configured, SYNC_SECRET: blank };
      const res = await worker.fetch(
        new Request('https://local/api/sync/batch', { method: 'POST', headers: { 'X-Sync-Secret': blank }, body: '{}' }),
        env,
        ctx
      );
      expect(res.status).toBe(503);
    }
  });

  // Gate 13: historical default SYNC_SECRET rejected as configured secret
  it('13. rejects historical default SYNC_SECRET as configured secret with 503', async () => {
    const env = { ...configured, SYNC_SECRET: 'lalab-sync-secret-2026' };
    const res = await worker.fetch(
      new Request('https://local/api/sync/batch', { method: 'POST', headers: { 'X-Sync-Secret': 'lalab-sync-secret-2026' }, body: '{}' }),
      env,
      ctx
    );
    expect(res.status).toBe(503);
  });

  // Gate 14: wrong or default sync secret rejected with 403
  it('14. rejects wrong or default sync secret with 403 Forbidden', async () => {
    const resWrong = await worker.fetch(
      new Request('https://local/api/sync/batch', { method: 'POST', headers: { 'X-Sync-Secret': 'wrong-sync-secret' }, body: '{}' }),
      configured,
      ctx
    );
    expect(resWrong.status).toBe(403);

    const resDefault = await worker.fetch(
      new Request('https://local/api/sync/batch', { method: 'POST', headers: { 'X-Sync-Secret': 'lalab-sync-secret-2026' }, body: '{}' }),
      configured,
      ctx
    );
    expect(resDefault.status).toBe(403);

    const resNoHeader = await worker.fetch(
      new Request('https://local/api/sync/batch', { method: 'POST', body: '{}' }),
      configured,
      ctx
    );
    expect(resNoHeader.status).toBe(403);
  });

  // Gate 15: valid configured sync secret accepted
  it('15. accepts valid configured sync secret', async () => {
    const resBatch = await worker.fetch(
      new Request('https://local/api/sync/batch', {
        method: 'POST',
        headers: { 'X-Sync-Secret': configured.SYNC_SECRET!, 'Content-Type': 'application/json' },
        body: JSON.stringify({ orders: [], bills: [] })
      }),
      configured,
      ctx
    );
    expect(resBatch.status).toBe(200);

    const resPull = await worker.fetch(
      new Request('https://local/api/sync/v2/pull?cursor=0&limit=10', {
        method: 'GET',
        headers: { 'X-Sync-Secret': configured.SYNC_SECRET! }
      }),
      configured,
      ctx
    );
    expect(resPull.status).toBe(200);
  });

  // Gate 16: no default Cloud PIN and distinct PIN requirement
  it('16. enforces no default Cloud PIN and distinct ADMIN/STAFF PINs', async () => {
    // Missing ADMIN_PIN -> 503
    const envNoPin = { ...configured, ADMIN_PIN: undefined };
    const resNoPin = await worker.fetch(
      new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"986754"}' }),
      envNoPin,
      ctx
    );
    expect(resNoPin.status).toBe(503);

    // Blank ADMIN_PIN -> 503
    const envBlankPin = { ...configured, ADMIN_PIN: '   ' };
    const resBlankPin = await worker.fetch(
      new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"986754"}' }),
      envBlankPin,
      ctx
    );
    expect(resBlankPin.status).toBe(503);

    // Identical ADMIN_PIN and STAFF_PIN -> 503
    const envSamePins = { ...configured, ADMIN_PIN: '123456', STAFF_PIN: '123456' };
    const resSamePins = await worker.fetch(
      new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"123456"}' }),
      envSamePins,
      ctx
    );
    expect(resSamePins.status).toBe(503);

    // Correct ADMIN_PIN -> 200 Admin
    const resAdmin = await worker.fetch(
      new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"986754"}' }),
      configured,
      ctx
    );
    expect(resAdmin.status).toBe(200);
    const bodyAdmin = await resAdmin.json() as { success: boolean; role: string };
    expect(bodyAdmin.success).toBe(true);
    expect(bodyAdmin.role).toBe('Admin');

    // Correct STAFF_PIN -> 200 Staff
    const resStaff = await worker.fetch(
      new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"123987"}' }),
      configured,
      ctx
    );
    expect(resStaff.status).toBe(200);
    const bodyStaff = await resStaff.json() as { success: boolean; role: string };
    expect(bodyStaff.success).toBe(true);
    expect(bodyStaff.role).toBe('Staff');

    // Default or wrong PIN -> 401
    const resWrong = await worker.fetch(
      new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"000000"}' }),
      configured,
      ctx
    );
    expect(resWrong.status).toBe(401);
  });
});
