import { type Currency, roundMoney } from './money';

export interface Tax {
  readonly id: number;
  readonly name: string;
  /** Porcentaje: 19 = 19 %. */
  readonly rate: number;
  readonly priceIncluded: boolean;
}

export type TaxRoundingMode = 'perLine' | 'global';

export interface TaxableLine {
  readonly unitPrice: number;
  readonly quantity: number;
  readonly discountPercent: number;
  readonly taxes: readonly Tax[];
}

export interface LineAmounts {
  readonly subtotal: number;
  readonly tax: number;
  readonly total: number;
}

export interface TaxCalculation {
  readonly lines: readonly LineAmounts[];
  readonly untaxed: number;
  readonly tax: number;
  readonly total: number;
}

interface RawLine {
  readonly excluded: number;
  readonly included: number;
  readonly taxes: ReadonlyArray<{ tax: Tax; amount: number }>;
}

/**
 * Total provisional con las mismas reglas que el backend (que a su vez replica a Odoo 20).
 * Es solo una vista previa: el total que se cobra lo verifica siempre el backend antes del pago.
 */
export function calculateTaxes(
  lines: readonly TaxableLine[],
  currency: Pick<Currency, 'rounding'>,
  mode: TaxRoundingMode,
): TaxCalculation {
  const round = (value: number) => roundMoney(value, currency);
  const roundIfPerLine = (value: number) => (mode === 'perLine' ? round(value) : value);

  const raw: RawLine[] = lines.map((line) => {
    let base = line.quantity * (line.unitPrice * (1 - line.discountPercent / 100));
    base = roundIfPerLine(base);
    const included = line.taxes.find((t) => t.priceIncluded);
    if (included) {
      const rate = included.rate / 100;
      const amount = roundIfPerLine((base * rate) / (1 + rate));
      return { excluded: roundIfPerLine(base - amount), included: base, taxes: [{ tax: included, amount }] };
    }
    const taxes = line.taxes.map((tax) => ({ tax, amount: roundIfPerLine((base * tax.rate) / 100) }));
    const totalIncluded = roundIfPerLine(base + taxes.reduce((sum, t) => sum + t.amount, 0));
    return { excluded: base, included: totalIncluded, taxes };
  });

  const byTax = new Map<number, number>();
  for (const line of raw) {
    for (const { tax, amount } of line.taxes) {
      byTax.set(tax.id, (byTax.get(tax.id) ?? 0) + amount);
    }
  }
  const tax = [...byTax.values()].reduce((sum, amount) => sum + round(amount), 0);
  const hasExcludedTax = raw.some((line) => line.taxes.some((t) => !t.tax.priceIncluded && round(t.amount) !== 0));

  let untaxed: number;
  let total: number;
  if (hasExcludedTax) {
    untaxed = round(raw.reduce((sum, l) => sum + l.excluded, 0));
    total = round(untaxed + tax);
  } else {
    total = round(raw.reduce((sum, l) => sum + l.included, 0));
    untaxed = round(total - tax);
  }

  return {
    lines: raw.map((l) => {
      const subtotal = round(l.excluded);
      const lineTotal = round(l.included);
      return { subtotal, tax: round(lineTotal - subtotal), total: lineTotal };
    }),
    untaxed,
    tax: round(tax),
    total,
  };
}
