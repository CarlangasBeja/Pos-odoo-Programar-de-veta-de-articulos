import '@fontsource/atkinson-hyperlegible/400.css';
import '@fontsource/atkinson-hyperlegible/700.css';
import './presentation/styles/tokens.css';
import './presentation/styles/base.css';
import './presentation/styles/components.css';
import './presentation/styles/screens.css';

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { TicketStore } from './application/cartStore';
import { ApiClient } from './infrastructure/http/apiClient';
import { HttpAuthGateway, HttpCatalogGateway, HttpSalesGateway, HttpSessionGateway } from './infrastructure/http/gateways';
import { JsPdfReceiptRenderer } from './infrastructure/pdf/jsPdfReceiptRenderer';
import { CryptoKeyGenerator, SessionTokenStore } from './infrastructure/storage/browser';
import { App } from './presentation/App';
import { createServices } from './presentation/context/services';

/*
 * Raíz de composición: el ÚNICO lugar que conoce las implementaciones concretas.
 * Para cambiar de backend (o usar dobles en pruebas) solo se cambian estas líneas.
 */
const tokens = new SessionTokenStore();
const api = new ApiClient({ baseUrl: import.meta.env.VITE_API_BASE_URL ?? '', tokens });

const services = createServices({
  api,
  ticket: new TicketStore(),
  tokens,
  keys: new CryptoKeyGenerator(),
  auth: new HttpAuthGateway(api),
  catalog: new HttpCatalogGateway(api),
  sessions: new HttpSessionGateway(api),
  sales: new HttpSalesGateway(api),
  receipts: new JsPdfReceiptRenderer(),
});

const root = document.getElementById('root');
if (!root) {
  throw new Error('No se encontró el elemento #root');
}

createRoot(root).render(
  <StrictMode>
    <App services={services} />
  </StrictMode>,
);
