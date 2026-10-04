import { describe, it, expect } from 'vitest';
import { toMobileBill } from '../src/mobile-bill';
import { CloudBillDto } from '../src/types';

describe('LAN/PWA bill response contract', () => {
  const bill: CloudBillDto = { id: 1, billNumber: 'AUDIT-1', billType: 'Customer',
    customerNameSnapshot: 'Audit Customer', periodDate: '2026-10-02', totalAmount: 75000,
    isPaid: false, hasJpeg: false,
    lines: [{ id: 1, description: 'Photo', quantity: 15, unitPrice: 5000, lineTotal: 75000 }], adjustments: [] };
  it('maps customer/date/total and Desktop description to the PWA fields', () => {
    const body = toMobileBill(bill, 'Admin');
    expect(body.customerName).toBe('Audit Customer');
    expect(body.periodEnd).toBe('2026-10-02');
    expect(body.grandTotal).toBe(75000);
    expect(body.lines[0].description).toBe('Photo');
    expect(body.lines[0].lineTotal).toBe(75000);
    expect(body.hasExportFile).toBe(false);
    expect(body.hasPaymentQr).toBe(false);
  });
  it('keeps empty arrays safe and preserves authoritative total', () => {
    expect(toMobileBill({ ...bill, lines: [], hasJpeg: true }, 'Admin').hasExportFile).toBe(true);
    expect(toMobileBill({ ...bill, lines: [] }, 'Admin').grandTotal).toBe(75000);
  });
  it('keeps the same structural fields with Staff financial redaction', () => {
    const body = toMobileBill(bill, 'Staff');
    expect(body.customerName).toBe('Audit Customer');
    expect(body.lines[0].description).toBe('Photo');
    expect(body.grandTotal).toBeNull();
    expect(body.lines[0].unitPrice).toBeNull();
    expect(body.adjustments).toBeNull();
  });
});
