/**
 * Dinero y moneda. Capa de dominio: TypeScript puro, sin React ni HTTP.
 * La moneda (precisión y símbolo) viene de Odoo a través de la sesión POS.
 */
export type SymbolPosition = 'before' | 'after';

export interface Currency {
  readonly code: string;
  readonly symbol: string;
  readonly symbolPosition: SymbolPosition;
  readonly decimalPlaces: number;
  /** Unidad mínima de redondeo (0.01, 1, 0.05…), igual que res.currency.rounding en Odoo. */
  readonly rounding: number;
}

/**
 * Redondeo "mitad hacia arriba" con corrección de coma flotante, igual que `roundPrecision`
 * del cliente web de Odoo (web/static/src/core/utils/numbers.js).
 */
export function roundToPrecision(value: number, rounding: number): number {
  if (!value) {
    return 0;
  }
  const precision = rounding > 0 ? rounding : 1;
  const normalized = Math.abs(value / precision);
  const epsilon = 2 ** (Math.log2(normalized) - 52);
  const rounded = Math.round(normalized + epsilon) * precision * Math.sign(value);
  return Number(rounded.toFixed(decimalsOf(precision)));
}

export function roundMoney(value: number, currency: Pick<Currency, 'rounding'>): number {
  return roundToPrecision(value, currency.rounding);
}

function decimalsOf(rounding: number): number {
  const text = rounding.toString();
  const dot = text.indexOf('.');
  return dot < 0 ? 0 : text.length - dot - 1;
}

const formatters = new Map<number, Intl.NumberFormat>();

function formatterFor(decimals: number): Intl.NumberFormat {
  let formatter = formatters.get(decimals);
  if (!formatter) {
    formatter = new Intl.NumberFormat('es-CO', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    formatters.set(decimals, formatter);
  }
  return formatter;
}

/** Formatea con el símbolo y la posición configurados en Odoo; separadores de miles y decimales de Colombia. */
export function formatMoney(value: number, currency: Currency): string {
  const number = formatterFor(currency.decimalPlaces).format(value);
  return currency.symbolPosition === 'after' ? `${number} ${currency.symbol}` : `${currency.symbol} ${number}`;
}

export function formatQuantity(value: number): string {
  return new Intl.NumberFormat('es-CO', { maximumFractionDigits: 3 }).format(value);
}
