import { createContext, type ReactNode, useContext } from 'react';
import type { TicketStore } from '../../application/cartStore';
import type { AuthGateway, CatalogGateway, KeyGenerator, SalesGateway, SessionGateway, TokenStore } from '../../application/ports';
import { LoginUseCase, LogoutUseCase, RestoreSessionUseCase } from '../../application/usecases/auth';
import { StartCheckoutUseCase } from '../../application/usecases/checkout';
import {
  CheckOdooConnectionUseCase,
  GetActiveSessionUseCase,
  GetDiscountPoliciesUseCase,
  GetProductCategoriesUseCase,
  GetSaleDetailUseCase,
  PreviewTicketUseCase,
  SearchProductsUseCase,
  SearchSalesHistoryUseCase,
} from '../../application/usecases/queries';
import type { ApiClient } from '../../infrastructure/http/apiClient';

/**
 * Casos de uso disponibles para la interfaz, uno por operación. Se crean una sola vez en la raíz de composición
 * (main.tsx); los componentes solo llaman a `execute` y no conocen HTTP ni las implementaciones.
 */
export interface Services {
  readonly api: ApiClient;
  readonly ticket: TicketStore;
  readonly login: LoginUseCase;
  readonly restoreSession: RestoreSessionUseCase;
  readonly logout: LogoutUseCase;
  readonly searchProducts: SearchProductsUseCase;
  readonly getProductCategories: GetProductCategoriesUseCase;
  readonly getActiveSession: GetActiveSessionUseCase;
  readonly checkOdooConnection: CheckOdooConnectionUseCase;
  readonly getDiscountPolicies: GetDiscountPoliciesUseCase;
  readonly previewTicket: PreviewTicketUseCase;
  readonly searchSalesHistory: SearchSalesHistoryUseCase;
  readonly getSaleDetail: GetSaleDetailUseCase;
  readonly startCheckout: StartCheckoutUseCase;
}

/** Puertos que necesitan los casos de uso. La raíz de composición entrega las implementaciones (HTTP o dobles de prueba). */
export interface ServiceDependencies {
  readonly api: ApiClient;
  readonly ticket: TicketStore;
  readonly tokens: TokenStore;
  readonly keys: KeyGenerator;
  readonly auth: AuthGateway;
  readonly catalog: CatalogGateway;
  readonly sessions: SessionGateway;
  readonly sales: SalesGateway;
}

/** Conecta cada caso de uso con los puertos que usa (inyección por constructor, sin contenedor). */
export function createServices(deps: ServiceDependencies): Services {
  return {
    api: deps.api,
    ticket: deps.ticket,
    login: new LoginUseCase(deps.auth, deps.tokens),
    restoreSession: new RestoreSessionUseCase(deps.auth, deps.tokens),
    logout: new LogoutUseCase(deps.tokens),
    searchProducts: new SearchProductsUseCase(deps.catalog),
    getProductCategories: new GetProductCategoriesUseCase(deps.catalog),
    getActiveSession: new GetActiveSessionUseCase(deps.sessions),
    checkOdooConnection: new CheckOdooConnectionUseCase(deps.sessions),
    getDiscountPolicies: new GetDiscountPoliciesUseCase(deps.sales),
    previewTicket: new PreviewTicketUseCase(deps.sales),
    searchSalesHistory: new SearchSalesHistoryUseCase(deps.sales),
    getSaleDetail: new GetSaleDetailUseCase(deps.sales),
    startCheckout: new StartCheckoutUseCase(deps.sales, deps.keys, deps.ticket),
  };
}

const ServicesContext = createContext<Services | null>(null);

export function ServicesProvider({ services, children }: { readonly services: Services; readonly children: ReactNode }) {
  return <ServicesContext.Provider value={services}>{children}</ServicesContext.Provider>;
}

export function useServices(): Services {
  const services = useContext(ServicesContext);
  if (!services) {
    throw new Error('useServices debe usarse dentro de ServicesProvider');
  }
  return services;
}
