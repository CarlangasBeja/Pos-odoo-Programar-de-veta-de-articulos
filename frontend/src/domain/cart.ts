import type { Currency } from './money';
import type { Product } from './product';
import { calculateTaxes, type LineAmounts, type TaxRoundingMode } from './tax';

export const MAX_QUANTITY = 10_000;
export const MAX_LINES = 100;
/** A partir de esta cantidad se advierte al operador (posible error de digitación). */
export const UNUSUAL_QUANTITY = 50;

export interface CartLine {
  readonly product: Product;
  readonly quantity: number;
}

/** Venta en preparación (carrito). Inmutable: cada operación devuelve un carrito nuevo. */
export interface Cart {
  readonly lines: readonly CartLine[];
}

export type CartResult = { readonly ok: true; readonly cart: Cart } | { readonly ok: false; readonly error: string };

export const emptyCart: Cart = { lines: [] };

/** RN-02: la cantidad es mayor que cero, acotada y con tres decimales como máximo. */
export function validateQuantity(quantity: number): string | null {
  if (!Number.isFinite(quantity)) {
    return 'Ingrese una cantidad numérica.';
  }
  if (quantity <= 0) {
    return 'La cantidad debe ser mayor que cero.';
  }
  if (quantity > MAX_QUANTITY) {
    return `La cantidad máxima por producto es ${MAX_QUANTITY.toLocaleString('es-CO')}.`;
  }
  if (Math.round(quantity * 1000) / 1000 !== quantity) {
    return 'La cantidad admite como máximo tres decimales.';
  }
  return null;
}

export function addProduct(cart: Cart, product: Product, quantity = 1): CartResult {
  if (!product.sellable) {
    return { ok: false, error: product.notSellableReasons[0] ?? `«${product.name}» no se puede vender desde el POS.` };
  }

  const existing = cart.lines.find((line) => line.product.id === product.id);
  const newQuantity = (existing?.quantity ?? 0) + quantity;
  const error = validateQuantity(newQuantity);
  if (error) {
    return { ok: false, error };
  }

  if (existing) {
    return { ok: true, cart: { lines: cart.lines.map((l) => (l.product.id === product.id ? { ...l, quantity: newQuantity } : l)) } };
  }

  if (cart.lines.length >= MAX_LINES) {
    return { ok: false, error: `Una venta admite como máximo ${MAX_LINES} productos distintos.` };
  }

  return { ok: true, cart: { lines: [...cart.lines, { product, quantity }] } };
}

export function changeQuantity(cart: Cart, productId: number, quantity: number): CartResult {
  const error = validateQuantity(quantity);
  if (error) {
    return { ok: false, error };
  }
  if (!cart.lines.some((l) => l.product.id === productId)) {
    return { ok: false, error: 'El producto ya no está en el ticket.' };
  }
  return { ok: true, cart: { lines: cart.lines.map((l) => (l.product.id === productId ? { ...l, quantity } : l)) } };
}

export function removeProduct(cart: Cart, productId: number): Cart {
  return { lines: cart.lines.filter((l) => l.product.id !== productId) };
}

/** Vuelve a insertar una línea eliminada en su posición original (para "Deshacer"). */
export function restoreLine(cart: Cart, line: CartLine, index: number): Cart {
  if (cart.lines.some((l) => l.product.id === line.product.id)) {
    return cart;
  }
  const lines = [...cart.lines];
  lines.splice(Math.min(index, lines.length), 0, line);
  return { lines };
}

export function itemCount(cart: Cart): number {
  return cart.lines.reduce((sum, l) => sum + l.quantity, 0);
}

export interface CartTotals {
  readonly lines: readonly LineAmounts[];
  readonly gross: number;
  readonly untaxed: number;
  readonly tax: number;
  readonly total: number;
  readonly discount: number;
}

/**
 * Totales provisionales (P07). <paramref name="discountPercent"/> solo se aplica cuando el descuento
 * es manual; los automáticos los calcula el backend al verificar la venta.
 */
export function cartTotals(cart: Cart, currency: Currency, mode: TaxRoundingMode, discountPercent = 0): CartTotals {
  const toTaxable = (percent: number) =>
    cart.lines.map((l) => ({ unitPrice: l.product.unitPrice, quantity: l.quantity, discountPercent: percent, taxes: l.product.taxes }));

  const gross = calculateTaxes(toTaxable(0), currency, mode);
  const discounted = discountPercent > 0 ? calculateTaxes(toTaxable(discountPercent), currency, mode) : gross;

  return {
    lines: discounted.lines,
    gross: gross.total,
    untaxed: discounted.untaxed,
    tax: discounted.tax,
    total: discounted.total,
    discount: Number((gross.total - discounted.total).toFixed(currency.decimalPlaces)),
  };
}
