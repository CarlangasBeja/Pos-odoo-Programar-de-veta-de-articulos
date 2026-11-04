import type { DiscountPolicy } from '../../domain/discount';
import type { Product, ProductCategory } from '../../domain/product';
import type { OrderDetail, OrderSummary, PosSession, SalePreview } from '../../domain/sale';
import type { TicketState } from '../cartStore';
import { AppError } from '../errors';
import type { CatalogGateway, HealthStatus, HistoryQuery, Page, ProductQuery, SalesGateway, SessionGateway } from '../ports';
import { toSaleDraft } from './checkout';

/*
 * Casos de uso de consulta (no cambian nada). Uno por clase con un solo método `execute`; reciben un AbortSignal
 * para que la interfaz cancele la búsqueda anterior cuando el usuario sigue escribiendo.
 */

export const MAX_SEARCH_LENGTH = 80;

/** RF-01 / RF-02: búsqueda del catálogo con filtros. */
export class SearchProductsUseCase {
  constructor(private readonly catalog: CatalogGateway) {}

  execute(query: ProductQuery, signal?: AbortSignal): Promise<Page<Product>> {
    const search = query.search.trim().slice(0, MAX_SEARCH_LENGTH);
    return this.catalog.search({ ...query, search }, signal);
  }
}

/** Categorías del punto de venta para los filtros del catálogo. */
export class GetProductCategoriesUseCase {
  constructor(private readonly catalog: CatalogGateway) {}

  execute(signal?: AbortSignal): Promise<readonly ProductCategory[]> {
    return this.catalog.categories(signal);
  }
}

/** Sesión POS vigente en Odoo y si permite vender. */
export class GetActiveSessionUseCase {
  constructor(private readonly sessions: SessionGateway) {}

  execute(signal?: AbortSignal): Promise<PosSession> {
    return this.sessions.activeSession(signal);
  }
}

/** Estado de la conexión entre la API y Odoo (indicador de la barra superior). */
export class CheckOdooConnectionUseCase {
  constructor(private readonly sessions: SessionGateway) {}

  execute(signal?: AbortSignal): Promise<HealthStatus> {
    return this.sessions.health(signal);
  }
}

/** RF-06: políticas de descuento habilitadas para el rol del operador. */
export class GetDiscountPoliciesUseCase {
  constructor(private readonly sales: SalesGateway) {}

  execute(signal?: AbortSignal): Promise<readonly DiscountPolicy[]> {
    return this.sales.discountPolicies(signal);
  }
}

/** Comprueba con el backend que el descuento elegido está autorizado y calcula su efecto antes de aplicarlo. */
export class PreviewTicketUseCase {
  constructor(private readonly sales: SalesGateway) {}

  execute(sessionId: number, ticket: TicketState, signal?: AbortSignal): Promise<SalePreview> {
    return this.sales.preview(toSaleDraft(sessionId, ticket), signal);
  }
}

/** RF-13: historial de ventas con búsqueda y filtros. */
export class SearchSalesHistoryUseCase {
  constructor(private readonly sales: SalesGateway) {}

  execute(query: HistoryQuery, signal?: AbortSignal): Promise<Page<OrderSummary>> {
    if (query.from && query.to && query.from > query.to) {
      return Promise.reject(new AppError('validation', 'La fecha inicial no puede ser posterior a la final.', 'HISTORY.INVALID_RANGE'));
    }
    return this.sales.history({ ...query, search: query.search.trim() }, signal);
  }
}

/** RF-14: detalle de una venta leído desde Odoo. */
export class GetSaleDetailUseCase {
  constructor(private readonly sales: SalesGateway) {}

  execute(orderId: number, signal?: AbortSignal): Promise<OrderDetail> {
    return this.sales.detail(orderId, signal);
  }
}
