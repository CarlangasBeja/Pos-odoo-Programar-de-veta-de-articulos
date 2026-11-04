import type { Currency } from '../../domain/money';

const dateTime = new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' });
const time = new Intl.DateTimeFormat('es-CO', { hour: '2-digit', minute: '2-digit' });

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : dateTime.format(date);
}

export function formatTime(value: Date): string {
  return time.format(value);
}

/** Moneda mientras se carga la sesión (no se muestra dinero sin saber la precisión real). */
export const PLACEHOLDER_CURRENCY: Currency = { code: 'XXX', symbol: '$', symbolPosition: 'before', decimalPlaces: 0, rounding: 1 };

/** Convierte lo que escribe el operador ("12.500", "12,5", "$ 12.500") en número según la precisión de la moneda. */
export function parseAmount(text: string, currency: Currency): number {
  const cleaned = text.replace(/[^\d.,]/g, '');
  if (!cleaned) return Number.NaN;
  if (currency.decimalPlaces === 0) {
    return Number(cleaned.replace(/[.,]/g, ''));
  }
  // Con decimales: el último separador es el decimal; los demás son de miles.
  const lastSeparator = Math.max(cleaned.lastIndexOf(','), cleaned.lastIndexOf('.'));
  if (lastSeparator < 0) return Number(cleaned);
  const integer = cleaned.slice(0, lastSeparator).replace(/[.,]/g, '');
  const decimals = cleaned.slice(lastSeparator + 1);
  if (decimals.length > currency.decimalPlaces) {
    return Number(cleaned.replace(/[.,]/g, ''));
  }
  return Number(`${integer || '0'}.${decimals}`);
}

export function todayIso(): string {
  const now = new Date();
  const offset = now.getTimezoneOffset() * 60_000;
  return new Date(now.getTime() - offset).toISOString().slice(0, 10);
}
