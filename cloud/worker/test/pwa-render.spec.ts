import { readFileSync } from 'node:fs';
import { URL } from 'node:url';
import { Script, runInNewContext } from 'node:vm';
import { it, expect } from 'vitest';
import { toMobileBill } from '../src/mobile-bill';
import { CloudBillDto } from '../src/types';

const html = readFileSync(new URL('../public/index.html', import.meta.url), 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)![1];
const viewBill = script.slice(script.indexOf('    async function viewBill('), script.indexOf('    async function toggleBillPaymentInModal'));
const escapeHtml = script.slice(script.indexOf('    function escapeHtml('), script.indexOf('    function escapeHtml(') + script.slice(script.indexOf('    function escapeHtml(')).indexOf('\n    }') + 6);

it('parses the complete deployed PWA script', () => { expect(() => new Script(script)).not.toThrow(); });
it.each(['Admin', 'Staff'] as const)('renders the actual bill modal for %s without undefined or financial leakage', async role => {
  const bill: CloudBillDto = { id: 1, billNumber: 'AUDIT-1', billType: 'Customer', customerNameSnapshot: 'Audit Customer',
    periodDate: '2026-10-02', totalAmount: 75000, isPaid: false, hasJpeg: false,
    lines: [{ id: 1, description: '<unsafe>', quantity: 15, unitPrice: 5000, lineTotal: 75000 }], adjustments: [] };
  const body = { innerHTML: '' };
  const title = { innerText: '' };
  const modal = { classList: { remove() {} } };
  const document = { getElementById(id: string) { return id === 'previewModalBody' ? body : id === 'previewModal' ? modal : title; } };
  await runInNewContext(`let currentRole=${JSON.stringify(role)}, authToken='fixture'; ${escapeHtml}\n${viewBill}\nviewBill(1)`, {
    document, fetch: async () => ({ ok: true, json: async () => toMobileBill(bill, role) })
  });
  expect(body.innerHTML).toContain('Audit Customer');
  expect(body.innerHTML).toContain('2026-10-02');
  expect(body.innerHTML).toContain('&lt;unsafe&gt;');
  expect(body.innerHTML).not.toContain('undefined');
  expect(body.innerHTML).not.toContain('/api/bills/1/image');
  if (role === 'Admin') expect(body.innerHTML).toContain('75.000');
  else expect(body.innerHTML).not.toContain('75.000');
});
