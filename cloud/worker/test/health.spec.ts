import { describe, it, expect } from 'vitest';
import worker from '../src/index';
import { Env } from '../src/types';

describe('Worker Health Check', () => {
  it('should return 200 with status ok on /api/health', async () => {
    const request = new Request('http://localhost:8787/api/health');
    const env: Env = {
      DB: {} as D1Database, JWT_SECRET: 'test-configured-jwt', SYNC_SECRET: 'test-configured-sync', ADMIN_PIN: '987654',
      WORKSHOP_NAME: 'XƯỞNG IN ẢNH CHUYÊN NGHIỆP'
    };
    const ctx = {
      waitUntil: () => {},
      passThroughOnException: () => {}
    } as unknown as ExecutionContext;

    const response = await worker.fetch(request, env, ctx);
    expect(response.status).toBe(200);

    const body = await response.json() as { status: string; appName: string; workshopName: string };
    expect(body.status).toBe('ok');
    expect(body.appName).toBe('Lalab Cloud Worker 24/7');
    expect(body.workshopName).toBe('XƯỞNG IN ẢNH CHUYÊN NGHIỆP');
  });

  it('should return 404 for unknown api routes', async () => {
    const request = new Request('http://localhost:8787/api/unknown');
    const env: Env = {
      DB: {} as D1Database
    };
    const ctx = {} as unknown as ExecutionContext;

    const response = await worker.fetch(request, env, ctx);
    expect(response.status).toBe(404);
  });
});
