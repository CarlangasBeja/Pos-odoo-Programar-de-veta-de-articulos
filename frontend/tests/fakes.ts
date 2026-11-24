import type { SaleCustomer } from '../src/domain/customer';
import type { CatalogGateway, KeyGenerator, Page, PaymentInput, ProductQuery, SaleDraft, SalesGateway, SessionGateway, TokenStore } from '../src/application/ports';
import type { DiscountPolicy } from '../src/domain/discount';
import type { Currency } from '../src/domain/money';
import type { Product } from '../src/domain/product';
import type { OrderDetail, OrderSummary, PosSession, SalePreview, SaleRegistration } from '../src/domain/sale';

export const COP: Currency = { code: 'COP', symbol: '$', symbolPosition: 'before', decimalPlaces: 0, rounding: 1 };
export const USD: Currency = { code: 'USD', symbol: '$', symbolPosition: 'before', decimalPlaces: 2, rounding: 0.01 };

export function product(overrides: Partial<Product> & Pick<Product, 'id' | 'name'>): Product {
  return {
    code: `P-${overrides.id}`,
    barcode: null,
    unitPrice: 10,
    priceWithTaxes: 11.9,
    priceIncludesTax: false,
    taxes: [{ id: 18, name: 'IVA 19 %', rate: 19, priceIncluded: false }],
    categories: [],
    availability: { status: 'notTracked', quantity: null },
    sellable: true,
    notSellableReasons: [],
    ...overrides,
  };
}

export const cafe = product({ id: 14, name: 'Café americano', unitPrice: 10, priceWithTaxes: 11.9, code: 'CAF-001', barcode: '7701000000011' });
export const agua = product({
  id: 16,
  name: 'Agua mineral',
  unitPrice: 5.95,
  priceWithTaxes: 5.95,
  priceIncludesTax: true,
  taxes: [{ id: 20, name: 'IVA 19 % incluido', rate: 19, priceIncluded: true }],
});
export const descontinuado = product({ id: 24, name: 'Producto descontinuado', sellable: false, notSellableReasons: ['No está habilitado para el punto de venta.'] });

export const openSession: PosSession = {
  id: 1,
  name: 'POS/00001',
  state: 'opened',
  stateLabel: 'abierta',
  configName: 'POS Web .NET',
  openedAt: null,
  currency: USD,
  taxRounding: 'global',
  paymentMethods: [
    { id: 2, name: 'Efectivo', type: 'cash', allowsChange: true, supported: true },
    { id: 3, name: 'Tarjeta', type: 'bank', allowsChange: false, supported: true },
  ],
  acceptsSales: true,
  issues: [],
};

export class MemoryTokenStore implements TokenStore {
  token: string | null = null;
  read() {
    return this.token;
  }
  save(token: string) {
    this.token = token;
  }
  clear() {
    this.token = null;
  }
}

export class SequenceKeys implements KeyGenerator {
  private counter = 0;
  readonly issued: string[] = [];
  next(): string {
    const key = `00000000-0000-4000-8000-${String(++this.counter).padStart(12, '0')}`;
    this.issued.push(key);
    return key;
  }
}

export class FakeCatalog implements CatalogGateway {
  readonly queries: ProductQuery[] = [];
  constructor(private readonly products: Product[]) {}
  async search(query: ProductQuery): Promise<Page<Product>> {
    this.queries.push(query);
    const items = this.products.filter((p) => p.name.toLowerCase().includes(query.search.toLowerCase()));
    return { items, page: 1, pageSize: query.pageSize, totalCount: items.length, totalPages: 1 };
  }
  async categories() {
    return [];
  }
}

export class FakeSessions implements SessionGateway {
  async activeSession() {
    return openSession;
  }
  async health() {
    return { healthy: true, description: 'Odoo conectado.' };
  }
}

type RegisterBehavior = (key: string, draft: SaleDraft, payment: PaymentInput) => Promise<SaleRegistration>;

export class FakeSales implements SalesGateway {
  readonly registerCalls: Array<{ key: string; draft: SaleDraft; payment: PaymentInput; customer?: SaleCustomer }> = [];
  registerBehavior: RegisterBehavior = async (key, _draft, payment) => registration(key, 'registered', payment.amountTendered);
  checkBehavior: (key: string) => Promise<SaleRegistration> = async (key) => registration(key, 'registered', 50);
  policies: DiscountPolicy[] = [];

  async discountPolicies() {
    return this.policies;
  }
  async preview(draft: SaleDraft): Promise<SalePreview> {
    return { sessionId: draft.sessionId, lines: [], grossBeforeDiscount: 29.75, discount: null, untaxed: 25, taxAmount: 4.75, total: 29.75, taxes: [] };
  }
  register(key: string, draft: SaleDraft, payment: PaymentInput, customer?: SaleCustomer) {
    this.registerCalls.push({ key, draft, payment, customer });
    return this.registerBehavior(key, draft, payment);
  }
  checkAttempt(key: string) {
    return this.checkBehavior(key);
  }
  async history(): Promise<Page<OrderSummary>> {
    return { items: [], page: 1, pageSize: 15, totalCount: 0, totalPages: 0 };
  }
  async detail(): Promise<OrderDetail> {
    throw new Error('no usado');
  }
}

export function registration(key: string, status: 'registered' | 'uncertain', tendered: number): SaleRegistration {
  return {
    saleKey: key,
    status,
    message: status === 'registered' ? 'Venta registrada y pagada en Odoo (POS - 7).' : 'No se recibió confirmación de Odoo.',
    orderId: status === 'registered' ? 7 : null,
    orderName: status === 'registered' ? 'POS - 7' : null,
    posReference: null,
    orderState: status === 'registered' ? 'paid' : null,
    total: 29.75,
    amountTendered: tendered,
    change: tendered - 29.75,
    paymentMethodName: 'Efectivo',
    paymentMethodType: 'cash',
    processedAt: new Date().toISOString(),
    replayed: false,
  };
}
