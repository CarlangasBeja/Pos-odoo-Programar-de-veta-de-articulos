import type { OrderDetail } from './sale';

/** Nota de venta lista para imprimir: solo datos ya confirmados por Odoo, sin cálculos de la interfaz. */
export interface SaleReceipt {
  readonly title: string;
  readonly storeName: string;
  readonly orderName: string;
  readonly reference: string | null;
  readonly date: string;
  readonly sessionName: string;
  readonly lines: ReadonlyArray<{
    readonly name: string;
    readonly quantity: number;
    readonly unitPrice: number;
    readonly discountPercent: number;
    readonly total: number;
  }>;
  readonly untaxed: number;
  readonly tax: number;
  readonly total: number;
  readonly payments: ReadonlyArray<{ readonly methodName: string; readonly amount: number }>;
  readonly change: number;
  readonly fileName: string;
}

/** Arma la nota de venta a partir de la orden leída de Odoo (la fuente de verdad). */
export function buildSaleReceipt(order: OrderDetail): SaleReceipt {
  const storeName = order.sessionName.split('/')[0]?.trim() || 'Punto de venta';
  const safeName = order.name.replace(/[^\w-]+/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');
  return {
    title: 'Nota de venta',
    storeName,
    orderName: order.name,
    reference: order.posReference,
    date: order.date,
    sessionName: order.sessionName,
    lines: order.lines.map((l) => ({
      name: l.productName,
      quantity: l.quantity,
      unitPrice: l.unitPrice,
      discountPercent: l.discountPercent,
      total: l.total,
    })),
    untaxed: order.amountTotal - order.amountTax,
    tax: order.amountTax,
    total: order.amountTotal,
    payments: order.payments.filter((p) => !p.isChange).map((p) => ({ methodName: p.methodName, amount: p.amount })),
    change: Math.abs(order.amountReturn),
    fileName: `nota-de-venta-${safeName || order.id}.pdf`,
  };
}
