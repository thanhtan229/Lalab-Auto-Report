import { describe, it, expect } from 'vitest';
import { toMobileBill } from '../src/mobile-bill';
import { CloudBillDto } from '../src/types';

describe('LAN/PWA bill response contract', () => {
  const bill: CloudBillDto = {
    id: 1,
    billNumber: 'AUDIT-1',
    billType: 'Customer',
    customerNameSnapshot: 'Audit Customer',
    periodDate: '2026-10-02',
    totalAmount: 75000,
    productSubtotal: 75000,
    adjustmentsTotal: 0,
    isPaid: false,
    hasJpeg: false,
    lines: [{ id: 1, description: 'Photo', size: '10x15', quantity: 15, unitPrice: 5000, lineTotal: 75000 }],
    adjustments: []
  };

  it('maps customer/date/total and Desktop description to the PWA fields', () => {
    const body = toMobileBill(bill, 'Admin');
    expect(body.customerName).toBe('Audit Customer');
    expect(body.periodEnd).toBe('2026-10-02');
    expect(body.grandTotal).toBe(75000);
    expect(body.lines[0].description).toBe('Photo');
    expect(body.lines[0].size).toBe('10x15');
    expect(body.lines[0].quantity).toBe(15);
    expect(body.lines[0].unitPrice).toBe(5000);
    expect(body.lines[0].lineTotal).toBe(75000);
    expect(body.hasExportFile).toBe(false);
    expect(body.hasPaymentQr).toBe(false);
  });

  it('preserves full structural equivalence with Desktop LAN contract', () => {
    const adminBill = toMobileBill(bill, 'Admin');
    // Verify all keys expected by LAN MobileWebServer contract
    const expectedKeys = [
      'id', 'billNumber', 'billType', 'customerName', 'phone', 'shippingAddress',
      'periodStart', 'periodEnd', 'status', 'isPaid', 'paidAt', 'note',
      'hasExportFile', 'hasPaymentQr', 'productSubtotal', 'adjustmentsTotal',
      'grandTotal', 'lines', 'adjustments'
    ];
    for (const key of expectedKeys) {
      expect(adminBill).toHaveProperty(key);
    }

    expect(adminBill.id).toBe(1);
    expect(adminBill.billNumber).toBe('AUDIT-1');
    expect(adminBill.billType).toBe('Customer');
    expect(adminBill.customerName).toBe('Audit Customer');
    expect(adminBill.phone).toBeNull();
    expect(adminBill.shippingAddress).toBeNull();
    expect(adminBill.periodStart).toBeNull();
    expect(adminBill.periodEnd).toBe('2026-10-02');
    expect(adminBill.status).toBe('Draft');
    expect(adminBill.isPaid).toBe(false);
    expect(adminBill.paidAt).toBeNull();
    expect(adminBill.note).toBeNull();
    expect(adminBill.hasExportFile).toBe(false);
    expect(adminBill.hasPaymentQr).toBe(false);
    expect(adminBill.productSubtotal).toBe(75000);
    expect(adminBill.adjustmentsTotal).toBe(0);
    expect(adminBill.grandTotal).toBe(75000);
    expect(adminBill.lines).toHaveLength(1);
    expect(adminBill.adjustments).toEqual([]);
  });

  it('keeps empty arrays safe and preserves authoritative total', () => {
    expect(toMobileBill({ ...bill, lines: [], hasJpeg: true }, 'Admin').hasExportFile).toBe(true);
    expect(toMobileBill({ ...bill, lines: [] }, 'Admin').grandTotal).toBe(75000);
    expect(toMobileBill({ ...bill, lines: [] }, 'Admin').lines).toEqual([]);
  });

  it('handles null/undefined adjustments and calculates adjustmentsTotal correctly', () => {
    const billWithAdj: CloudBillDto = {
      ...bill,
      adjustmentsTotal: null,
      adjustments: [
        { id: 1, description: 'Giảm giá VIP', amount: 5000, type: 'Discount', direction: 'Deduct' },
        { id: 2, description: 'Phụ thu gấp', amount: 2000, type: 'Fee', direction: 'Add' }
      ]
    };
    const admin = toMobileBill(billWithAdj, 'Admin');
    expect(admin.adjustmentsTotal).toBe(-3000);
    expect(admin.adjustments).toHaveLength(2);
    expect(admin.adjustments![0]).toEqual({
      id: 1, description: 'Giảm giá VIP', amount: 5000, type: 'Discount', direction: 'Deduct'
    });

    // Zero adjustment
    const billZeroAdj: CloudBillDto = {
      ...bill,
      adjustmentsTotal: 0,
      adjustments: [{ id: 1, description: 'KM 0đ', amount: 0, type: 'Discount', direction: 'Deduct' }]
    };
    expect(toMobileBill(billZeroAdj, 'Admin').adjustmentsTotal).toBe(0);

    // Missing adjustments array
    const billNoAdj = { ...bill, adjustments: undefined as any, adjustmentsTotal: undefined as any };
    expect(toMobileBill(billNoAdj, 'Admin').adjustmentsTotal).toBe(0);
    expect(toMobileBill(billNoAdj, 'Admin').adjustments).toEqual([]);
  });

  it('keeps the same structural fields with Staff financial redaction', () => {
    const body = toMobileBill(bill, 'Staff');
    expect(body.customerName).toBe('Audit Customer');
    expect(body.lines[0].description).toBe('Photo');
    expect(body.lines[0].quantity).toBe(15);
    expect(body.lines[0].unitPrice).toBeNull();
    expect(body.lines[0].lineTotal).toBeNull();
    expect(body.productSubtotal).toBeNull();
    expect(body.adjustmentsTotal).toBeNull();
    expect(body.grandTotal).toBeNull();
    expect(body.adjustments).toBeNull();
    expect(body.hasExportFile).toBe(false);
    expect(body.hasPaymentQr).toBe(false);
  });

  it('redacts export file for Staff even when hasJpeg is true', () => {
    const body = toMobileBill({ ...bill, hasJpeg: true }, 'Staff');
    expect(body.hasExportFile).toBe(false);
  });
});
