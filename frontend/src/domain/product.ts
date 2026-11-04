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
