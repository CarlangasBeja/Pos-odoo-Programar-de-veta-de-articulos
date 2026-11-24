import { FINAL_CONSUMER, type SaleCustomer } from '../../domain/customer';
import type {
  AuthGateway,
  CatalogGateway,
  HistoryQuery,
  Operator,
  Page,
  PaymentInput,
  ProductQuery,
  SaleDraft,
  SalesGateway,
  SessionGateway,
} from '../../application/ports';
import type { DiscountPolicy } from '../../domain/discount';
import type { Product, ProductCategory } from '../../domain/product';
import type { OrderDetail, OrderSummary, PosSession, SalePreview, SaleRegistration } from '../../domain/sale';
import type { ApiClient } from './apiClient';

/*
 * Adaptadores HTTP que implementan los puertos de Application contra la API .NET.
 * Los contratos JSON de la API ya coinciden con los tipos del dominio, así que la traducción es directa.
 */

const REGISTER_TIMEOUT_MS = 60_000; // el backend espera a Odoo hasta 45 s antes de declarar el resultado incierto

export class HttpCatalogGateway implements CatalogGateway {
  constructor(private readonly api: ApiClient) {}

  search(query: ProductQuery, signal?: AbortSignal): Promise<Page<Product>> {
    return this.api.get<Page<Product>>('/api/products', {
      signal,
      query: {
        search: query.search,
        categoryId: query.categoryId,
        onlyInStock: query.onlyInStock || null,
        sort: query.sort,
        page: query.page,
        pageSize: query.pageSize,
      },
    });
  }

  categories(signal?: AbortSignal): Promise<readonly ProductCategory[]> {
    return this.api.get<ProductCategory[]>('/api/products/categories', { signal });
  }
}

interface HealthBody {
  readonly status: string;
  readonly checks?: Record<string, { status: string; description: string | null }>;
}

export class HttpSessionGateway implements SessionGateway {
  constructor(private readonly api: ApiClient) {}

  activeSession(signal?: AbortSignal): Promise<PosSession> {
    return this.api.get<PosSession>('/api/pos/sessions/active', { signal });
  }

  async health(signal?: AbortSignal) {
    const { status, body } = await this.api.probe<HealthBody>('/api/health', signal);
    const odoo = body?.checks?.odoo;
    return {
      healthy: status === 200 && odoo?.status === 'healthy',
      description: odoo?.description ?? (status === 200 ? 'Odoo conectado.' : 'No fue posible comunicarse con Odoo.'),
    };
  }
}

export class HttpSalesGateway implements SalesGateway {
  constructor(private readonly api: ApiClient) {}

  discountPolicies(signal?: AbortSignal): Promise<readonly DiscountPolicy[]> {
    return this.api.get<DiscountPolicy[]>('/api/discounts/policies', { signal });
  }

  preview(draft: SaleDraft, signal?: AbortSignal): Promise<SalePreview> {
    return this.api.post<SalePreview>('/api/sales/preview', draft, { signal });
  }

  register(idempotencyKey: string, draft: SaleDraft, payment: PaymentInput, customer: SaleCustomer = FINAL_CONSUMER): Promise<SaleRegistration> {
    const customerBody = customer.kind === 'identified' ? { name: customer.name, identification: customer.identification } : null;
    return this.api.post<SaleRegistration>('/api/sales', { ...draft, payment, customer: customerBody }, {
      headers: { 'Idempotency-Key': idempotencyKey },
      timeoutMs: REGISTER_TIMEOUT_MS,
    });
  }

  checkAttempt(idempotencyKey: string): Promise<SaleRegistration> {
    return this.api.get<SaleRegistration>(`/api/sales/attempts/${encodeURIComponent(idempotencyKey)}`);
  }

  history(query: HistoryQuery, signal?: AbortSignal): Promise<Page<OrderSummary>> {
    return this.api.get<Page<OrderSummary>>('/api/sales', {
      signal,
      query: {
        search: query.search,
        state: query.state === 'all' ? null : query.state,
        from: query.from,
        to: query.to,
        page: query.page,
        pageSize: query.pageSize,
      },
    });
  }

  detail(orderId: number, signal?: AbortSignal): Promise<OrderDetail> {
    return this.api.get<OrderDetail>(`/api/sales/${orderId}`, { signal });
  }
}

export class HttpAuthGateway implements AuthGateway {
  constructor(private readonly api: ApiClient) {}

  login(username: string, password: string) {
    return this.api.post<{ accessToken: string; expiresIn: number }>('/api/auth/login', { username, password });
  }

  me(): Promise<Operator> {
    return this.api.get<Operator>('/api/auth/me');
  }
}
