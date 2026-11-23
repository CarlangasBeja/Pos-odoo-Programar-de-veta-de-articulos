import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { TicketStore } from '../../src/application/cartStore';
import type { CatalogGateway, ReceiptDocumentRenderer, SalesGateway, SessionGateway } from '../../src/application/ports';
import { ApiClient } from '../../src/infrastructure/http/apiClient';
import { HttpAuthGateway } from '../../src/infrastructure/http/gateways';
import { PosProvider, ThemeProvider } from '../../src/presentation/context/app';
import { ConfirmProvider, ToastProvider } from '../../src/presentation/context/feedback';
import { createServices, type Services, ServicesProvider } from '../../src/presentation/context/services';
import { FakeCatalog, FakeSales, FakeSessions, MemoryTokenStore, SequenceKeys } from '../fakes';

export interface TestServicesOptions {
  readonly catalog?: CatalogGateway;
  readonly sessions?: SessionGateway;
  readonly sales?: SalesGateway;
  readonly fetchImpl?: typeof fetch;
  readonly receipts?: ReceiptDocumentRenderer;
}

/** Arma los servicios de la aplicación con dobles de prueba (sin red). */
export function testServices(options: TestServicesOptions = {}): Services {
  const tokens = new MemoryTokenStore();
  const api = new ApiClient({ baseUrl: 'http://api.test', tokens, fetchImpl: options.fetchImpl ?? (() => Promise.reject(new TypeError('sin red'))) });
  return createServices({
    api,
    ticket: new TicketStore(),
    tokens,
    keys: new SequenceKeys(),
    auth: new HttpAuthGateway(api),
    catalog: options.catalog ?? new FakeCatalog([]),
    sessions: options.sessions ?? new FakeSessions(),
    sales: options.sales ?? new FakeSales(),
    receipts: options.receipts ?? { render: () => Promise.resolve(new Blob(['%PDF-1.3'], { type: 'application/pdf' })) },
  });
}

export function renderInApp(ui: ReactElement, services: Services, { withPos = true } = {}) {
  return render(
    <ServicesProvider services={services}>
      <ThemeProvider>
        <ToastProvider>
          <ConfirmProvider>{withPos ? <PosProvider>{ui}</PosProvider> : ui}</ConfirmProvider>
        </ToastProvider>
      </ThemeProvider>
    </ServicesProvider>,
  );
}
