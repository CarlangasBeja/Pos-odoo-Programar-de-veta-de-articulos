import type { SaleCustomer } from '../domain/customer';
import type { DiscountPolicy } from '../domain/discount';
import type { Product, ProductCategory } from '../domain/product';
import type { OrderDetail, OrderState, OrderSummary, PosSession, SalePreview, SaleRegistration } from '../domain/sale';
import type { Currency } from '../domain/money';
import type { SaleReceipt } from '../domain/receipt';

/*
 * Puertos de la aplicación: describen lo que los casos de uso necesitan del exterior,
 * sin saber que detrás hay HTTP, fetch o una API .NET. La infraestructura los implementa.
 */

export interface Page<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
}

export type ProductSort = 'nameAsc' | 'nameDesc' | 'priceAsc' | 'priceDesc' | 'codeAsc';

export interface ProductQuery {
  readonly search: string;
  readonly categoryId: number | null;
  readonly onlyInStock: boolean;
  readonly sort: ProductSort;
  readonly page: number;
  readonly pageSize: number;
}

export interface CatalogGateway {
  search(query: ProductQuery, signal?: AbortSignal): Promise<Page<Product>>;
  categories(signal?: AbortSignal): Promise<readonly ProductCategory[]>;
}

export interface HealthStatus {
  readonly healthy: boolean;
  readonly description: string;
}

export interface SessionGateway {
  activeSession(signal?: AbortSignal): Promise<PosSession>;
  health(signal?: AbortSignal): Promise<HealthStatus>;
}

export interface SaleDraft {
  readonly sessionId: number;
  readonly lines: ReadonlyArray<{ readonly productId: number; readonly quantity: number }>;
  readonly discount: { readonly policyCode: string; readonly percentage: number | null } | null;
}

export interface PaymentInput {
  readonly paymentMethodId: number;
  readonly amountTendered: number;
}

export interface SalesGateway {
  discountPolicies(signal?: AbortSignal): Promise<readonly DiscountPolicy[]>;
  preview(draft: SaleDraft, signal?: AbortSignal): Promise<SalePreview>;
  /** Sin cliente (o consumidor final) la venta va sin datos del cliente. */
  register(idempotencyKey: string, draft: SaleDraft, payment: PaymentInput, customer?: SaleCustomer): Promise<SaleRegistration>;
  checkAttempt(idempotencyKey: string): Promise<SaleRegistration>;
  history(query: HistoryQuery, signal?: AbortSignal): Promise<Page<OrderSummary>>;
  detail(orderId: number, signal?: AbortSignal): Promise<OrderDetail>;
}

export type HistoryStateFilter = 'all' | Exclude<OrderState, 'unknown'>;

export interface HistoryQuery {
  readonly search: string;
  readonly state: HistoryStateFilter;
  readonly from: string | null;
  readonly to: string | null;
  readonly page: number;
  readonly pageSize: number;
}

export interface Operator {
  readonly username: string;
  readonly displayName: string;
  readonly role: 'cashier' | 'supervisor';
  readonly roleLabel: string;
}

export interface AuthGateway {
  login(username: string, password: string): Promise<{ readonly accessToken: string; readonly expiresIn: number }>;
  me(): Promise<Operator>;
}

export interface TokenStore {
  read(): string | null;
  save(token: string, expiresInSeconds: number): void;
  clear(): void;
}

/** Genera claves de idempotencia (UUID v4). */
export interface KeyGenerator {
  next(): string;
}

/** Genera el documento de una nota de venta (PDF). La implementación vive en Infrastructure. */
export interface ReceiptDocumentRenderer {
  render(receipt: SaleReceipt, currency: Currency): Promise<Blob>;
}
