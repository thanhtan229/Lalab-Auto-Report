import { describe, it, expect } from 'vitest';
import worker from '../src/index';
import { createAuthToken, verifyAuthToken } from '../src/auth';
import { Env } from '../src/types';

const ctx = {} as ExecutionContext;
const configured = { DB: {} as D1Database, JWT_SECRET: 'random-test-secret', SYNC_SECRET: 'random-sync-secret', ADMIN_PIN: '986754' } as Env;

describe('Fail closed configuration and token validation', () => {
  it.each([undefined, '', '   ', 'lalab-secret-salt-2026-tinix'])('rejects missing/source-known JWT secret %s', async secret => {
    const env = { ...configured, JWT_SECRET: secret };
    const response = await worker.fetch(new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"123456"}' }), env, ctx);
    expect(response.status).toBe(503);
    expect(await verifyAuthToken('anything.signature', secret)).toBeNull();
  });
  it('has no default PIN or sync key', async () => {
    const env = { ...configured, ADMIN_PIN: undefined, SYNC_SECRET: undefined };
    expect((await worker.fetch(new Request('https://local/api/auth/login', { method: 'POST', body: '{"pin":"123456"}' }), env, ctx)).status).toBe(503);
    expect((await worker.fetch(new Request('https://local/api/sync/batch', { method: 'POST', headers: { 'X-Sync-Secret': 'lalab-sync-secret-2026' }, body: '{}' }), env, ctx)).status).toBe(503);
  });
  it('accepts configured token and refuses expired or malformed payloads', async () => {
    const token = await createAuthToken('Admin', configured.JWT_SECRET);
    expect((await verifyAuthToken(token, configured.JWT_SECRET))?.role).toBe('Admin');
    expect(await verifyAuthToken(token + '.extra', configured.JWT_SECRET)).toBeNull();
    expect(await verifyAuthToken(await createAuthToken('Admin', configured.JWT_SECRET, -1), configured.JWT_SECRET)).toBeNull();
    expect(await verifyAuthToken(await createAuthToken('Owner' as any, configured.JWT_SECRET), configured.JWT_SECRET)).toBeNull();
  });
});
