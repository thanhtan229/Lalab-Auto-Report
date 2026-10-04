import { CloudBillDto, UserRole } from './types';

// Storage projection names are independent of the LAN/PWA API contract.
export function toMobileBill(bill: CloudBillDto, role: UserRole) {
  const admin = role === 'Admin';
  const adjustmentsTotal = bill.adjustmentsTotal ?? bill.adjustments.reduce((sum, a) => sum + (a.direction === 'Deduct' ? -a.amount : a.amount), 0);
  const lines = (Array.isArray(bill.lines) ? bill.lines : []).map(line => ({
    id: line.id,
    description: line.description ?? line.productName ?? '',
    size: line.size ?? null,
    quantity: line.quantity,
    unitPrice: admin ? line.unitPrice ?? null : null,
    lineTotal: admin ? line.lineTotal ?? null : null
  }));
  return {
    id: bill.id, billNumber: bill.billNumber, billType: bill.billType,
    customerName: bill.customerNameSnapshot, phone: null, shippingAddress: null,
    periodStart: null, periodEnd: bill.periodDate,
    status: bill.exportedAt ? 'Exported' : bill.lockedAt ? 'Locked' : 'Draft',
    isPaid: bill.isPaid, paidAt: bill.paidAt ?? null, note: null,
    hasExportFile: admin && bill.hasJpeg, hasPaymentQr: false,
    productSubtotal: admin ? (bill.productSubtotal ?? Math.max(0, bill.totalAmount - adjustmentsTotal)) : null,
    adjustmentsTotal: admin ? adjustmentsTotal : null,
    grandTotal: admin ? bill.totalAmount : null,
    lines,
    adjustments: admin ? bill.adjustments.map(a => ({
      id: a.id, description: a.description ?? '', amount: a.amount, type: a.type, direction: a.direction ?? 'Add'
    })) : null
  };
}
