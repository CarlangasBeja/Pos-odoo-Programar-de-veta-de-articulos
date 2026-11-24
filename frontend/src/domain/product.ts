import type { Tax } from './tax';

export type AvailabilityStatus = 'available' | 'outOfStock' | 'notTracked';

export interface ProductCategory {
  readonly id: number;
  readonly name: string;
}

/** Producto del catálogo de Odoo tal como lo entrega el backend. El precio nunca lo decide el frontend (RN-03). */
export interface Product {
  readonly id: number;
  readonly name: string;
  readonly code: string | null;
  readonly barcode: string | null;
  readonly unitPrice: number;
  readonly priceWithTaxes: number;
  readonly priceIncludesTax: boolean;
  readonly taxes: readonly Tax[];
  readonly categories: readonly ProductCategory[];
  readonly availability: { readonly status: AvailabilityStatus; readonly quantity: number | null };
  readonly sellable: boolean;
  readonly notSellableReasons: readonly string[];
}

/** Coincidencia exacta por código de barras o referencia interna (lectores de código de barras envían el código + Enter). */
export function matchesCodeExactly(product: Product, text: string): boolean {
  const needle = text.trim().toLowerCase();
  return needle.length > 0 && (product.barcode?.toLowerCase() === needle || product.code?.toLowerCase() === needle);
}

/** Motivo para pedir confirmación antes de agregar una unidad más de un producto al ticket. */
export type StockConfirmation =
  | { readonly reason: 'outOfStock' }
  | { readonly reason: 'exceedsStock'; readonly available: number };

/**
 * Odoo permite vender sin existencias (puede haber unidades en bodega o en exhibición sin registrar), así que el POS no
 * lo bloquea, pero pide confirmación: al agregar un producto con 0 unidades, o la primera unidad que supera las registradas.
 */
export function stockConfirmationFor(product: Product, quantityInCart: number): StockConfirmation | null {
  const { availability } = product;
  if (availability.status === 'outOfStock') {
    return quantityInCart === 0 ? { reason: 'outOfStock' } : null;
  }
  if (availability.status === 'available' && availability.quantity !== null) {
    const available = availability.quantity;
    return quantityInCart <= available && quantityInCart + 1 > available ? { reason: 'exceedsStock', available } : null;
  }
  return null;
}
