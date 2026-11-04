import type { TaxRoundingMode } from './tax';
import type { Currency } from './money';

export type PaymentMethodType = 'cash' | 'bank' | 'other';

export interface PaymentMethod {
  readonly id: number;
  readonly name: string;
  readonly type: PaymentMethodType;
  readonly allowsChange: boolean;
  readonly supported: boolean;
}

export type SessionState = 'openingControl' | 'opened' | 'closingControl' | 'closed';

/** Sesión POS vigente en Odoo. */
export interface PosSession {
  readonly id: number;
  readonly name: string;
  readonly state: SessionState;
  readonly stateLabel: string;
  readonly configName: string;
  readonly openedAt: string | null;
  readonly currency: Currency;
  readonly taxRounding: TaxRoundingMode;
  readonly paymentMethods: readonly PaymentMethod[];
  readonly acceptsSales: boolean;
  readonly issues: readonly string[];
}

export interface SaleLineView {
  readonly productId: number;
  readonly productName: string;
  readonly productCode: string | null;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly discountPercent: number;
  readonly grossAmount: number;
  readonly subtotal: number;
  readonly taxAmount: number;
  readonly total: number;
}

/** Venta calculada y verificada por el backend con datos de Odoo (todavía no registrada). */
export interface SalePreview {
  readonly sessionId: number;
  readonly lines: readonly SaleLineView[];
  readonly grossBeforeDiscount: number;
  readonly discount: { readonly policyCode: string; readonly description: string; readonly amount: number } | null;
  readonly untaxed: number;
  readonly taxAmount: number;
  readonly total: number;
  readonly taxes: ReadonlyArray<{ readonly taxId: number; readonly name: string; readonly amount: number }>;
}

export type OrderState = 'draft' | 'paid' | 'posted' | 'cancelled' | 'unknown';

export interface SaleRegistration {
  readonly saleKey: string;
  readonly status: 'registered' | 'uncertain';
  readonly message: string;
  readonly orderId: number | null;
  readonly orderName: string | null;
  readonly posReference: string | null;
  readonly orderState: OrderState | null;
  readonly total: number;
  readonly amountTendered: number;
  readonly change: number;
  readonly paymentMethodName: string;
  readonly paymentMethodType: PaymentMethodType;
  readonly processedAt: string;
  readonly replayed: boolean;
}

export interface OrderSummary {
  readonly id: number;
  readonly name: string;
  readonly posReference: string | null;
  readonly date: string;
  readonly state: OrderState;
  readonly amountTotal: number;
  readonly amountPaid: number;
  readonly sessionName: string;
  readonly note: string | null;
}

export interface OrderDetail {
  readonly id: number;
  readonly name: string;
  readonly posReference: string | null;
  readonly saleKey: string | null;
  readonly date: string;
  readonly state: OrderState;
  readonly sessionName: string;
  readonly amountTax: number;
  readonly amountTotal: number;
  readonly amountPaid: number;
  readonly amountReturn: number;
  readonly note: string | null;
  readonly lines: ReadonlyArray<{
    readonly productId: number;
    readonly productName: string;
    readonly quantity: number;
    readonly unitPrice: number;
    readonly discountPercent: number;
    readonly subtotal: number;
    readonly total: number;
  }>;
  readonly payments: ReadonlyArray<{ readonly methodName: string; readonly amount: number; readonly isChange: boolean; readonly date: string | null }>;
}

export const ORDER_STATE_LABELS: Record<OrderState, string> = {
  draft: 'Nueva (sin pagar)',
  paid: 'Pagada',
  posted: 'Contabilizada',
  cancelled: 'Cancelada',
  unknown: 'Desconocido',
};

/** Validación del cobro antes de enviarlo: efectivo admite cambio, banco exige el monto exacto (RN-05). */
export function validatePayment(method: PaymentMethod | undefined, tendered: number, total: number): string | null {
  if (!method) {
    return 'Seleccione un método de pago.';
  }
  if (!method.supported) {
    return `«${method.name}» no se puede usar desde este POS.`;
  }
  if (!Number.isFinite(tendered) || tendered <= 0) {
    return 'Ingrese el monto recibido.';
  }
  if (tendered < total) {
    return 'El monto recibido es menor que el total a cobrar.';
  }
  if (!method.allowsChange && tendered !== total) {
    return `Con «${method.name}» el monto debe ser exactamente el total.`;
  }
  return null;
}

/** Sugerencias de billetes para cobrar en efectivo rápidamente (total exacto y redondeos hacia arriba). */
export function quickCashAmounts(total: number, currency: Currency): number[] {
  const steps = currency.decimalPlaces === 0 || total >= 1000 ? [1000, 2000, 5000, 10000, 20000, 50000, 100000] : [1, 5, 10, 20, 50, 100];
  const amounts = new Set<number>([total]);
  for (const step of steps) {
    const rounded = Math.ceil(total / step) * step;
    if (rounded > total) {
      amounts.add(rounded);
    }
    if (amounts.size >= 4) {
      break;
    }
  }
  return [...amounts].sort((a, b) => a - b);
}
