import { describe, expect, it } from 'vitest';
import { calculateTaxes, type Tax, type TaxRoundingMode } from '../../src/domain/tax';
// Los mismos 600 vectores generados con el motor de impuestos real de Odoo 20 que usa el backend.
import file from '../../../backend/tests/Pos.UnitTests/Domain/Data/odoo20-tax-vectors.json';

interface Vector {
  currency: string;
  currencyRounding: number;
  roundingMethod: 'round_globally' | 'round_per_line';
  lines: Array<{ priceUnit: number; quantity: number; discount: number; taxes: string[] }>;
  expected: { untaxed: number; tax: number; total: number };
}

const taxes: Record<string, Tax> = Object.fromEntries(
  Object.entries(file.taxes as Record<string, { amount: number; priceIncluded: boolean }>).map(([key, t], index) => [
    key,
    { id: index + 1, name: key, rate: t.amount, priceIncluded: t.priceIncluded },
  ]),
);

const vectors = file.vectors as Vector[];

/** El total provisional del frontend sigue las mismas reglas verificadas que el backend y Odoo. */
describe('Impuestos: paridad con Odoo 20', () => {
  it('usa vectores de Odoo 20', () => {
    expect(file.odooVersion.startsWith('20.')).toBe(true);
    expect(vectors.length).toBeGreaterThanOrEqual(600);
  });

  it.each(vectors.map((v, i) => [i, v] as const))('vector %i coincide', (_index, vector) => {
    const mode: TaxRoundingMode = vector.roundingMethod === 'round_per_line' ? 'perLine' : 'global';
    const result = calculateTaxes(
      vector.lines.map((l) => ({ unitPrice: l.priceUnit, quantity: l.quantity, discountPercent: l.discount, taxes: l.taxes.map((k) => taxes[k]!) })),
      { rounding: vector.currencyRounding },
      mode,
    );

    expect(result.total).toBeCloseTo(vector.expected.total, 6);
    expect(result.tax).toBeCloseTo(vector.expected.tax, 6);
    expect(result.untaxed).toBeCloseTo(vector.expected.untaxed, 6);
  });
});
