import { describe, expect, it, vi } from 'vitest';
import { AppError } from '../../src/application/errors';
import { ApiClient } from '../../src/infrastructure/http/apiClient';
import { HttpSalesGateway } from '../../src/infrastructure/http/gateways';
import { MemoryTokenStore } from '../fakes';

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });
}

function client(fetchImpl: typeof fetch, tokens = new MemoryTokenStore(), timeoutMs?: number) {
  return new ApiClient({ baseUrl: 'http://api.test', tokens, fetchImpl, timeoutMs });
}

async function captureError(promise: Promise<unknown>): Promise<AppError> {
  try {
    await promise;
  } catch (error) {
    if (error instanceof AppError) return error;
    throw error;
  }
  throw new Error('Se esperaba un error');
}

/** P08: errores comprensibles cuando falla la API .NET. */
describe('Cliente HTTP (P08)', () => {
  it('si la API no está disponible, explica qué revisar', async () => {
    const api = client(vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = await captureError(api.get('/api/products'));

    expect(error.kind).toBe('network');
    expect(error.message).toBe('No se pudo conectar con el servidor del POS. Verifique que la API (.NET) esté en ejecución y que tenga conexión de red.');
    expect(error.isTransient).toBe(true);
  });

  it('muestra el detalle en español que envía el backend (Problem Details)', async () => {
    const api = client(
      vi.fn().mockResolvedValue(
        jsonResponse(403, {
          title: 'Operación no autorizada',
          detail: 'Descuento no autorizado: el rol Cajero puede aplicar como máximo 10 %.',
          code: 'DISCOUNT.NOT_AUTHORIZED',
          errors: [{ code: 'DISCOUNT.NOT_AUTHORIZED', message: 'Descuento no autorizado', target: 'discount' }],
        }),
      ),
    );

    const error = await captureError(api.post('/api/sales/preview', {}));

    expect(error.kind).toBe('forbidden');
    expect(error.code).toBe('DISCOUNT.NOT_AUTHORIZED');
    expect(error.message).toContain('como máximo 10 %');
    expect(error.messageFor('discount')).toBe('Descuento no autorizado');
  });

  it('si el servidor responde sin cuerpo, usa un mensaje claro según el estado', async () => {
    const api = client(vi.fn().mockResolvedValue(new Response('', { status: 503 })));

    const error = await captureError(api.get('/api/pos/sessions/active'));

    expect(error.kind).toBe('unavailable');
    expect(error.message).toContain('Odoo no está disponible');
  });

  it('un tiempo de espera agotado se informa como timeout', async () => {
    const neverAnswers: typeof fetch = (_input, init) =>
      new Promise((_resolve, reject) => init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError'))));
    const api = client(neverAnswers, new MemoryTokenStore(), 20);

    const error = await captureError(api.get('/api/products'));

    expect(error.kind).toBe('timeout');
  });

  it('un 401 borra el token y avisa que la sesión expiró', async () => {
    const tokens = new MemoryTokenStore();
    tokens.save('viejo');
    const api = client(vi.fn().mockResolvedValue(jsonResponse(401, { detail: 'Su sesión no es válida o expiró.' })), tokens);
    const onUnauthorized = vi.fn();
    api.onUnauthorized(onUnauthorized);

    await captureError(api.get('/api/products'));

    expect(tokens.read()).toBeNull();
    expect(onUnauthorized).toHaveBeenCalledOnce();
  });

  it('envía el token y la clave de idempotencia al registrar una venta', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 'registered' }), { status: 201 }));
    const tokens = new MemoryTokenStore();
    tokens.save('token-123');
    const sales = new HttpSalesGateway(client(fetchImpl, tokens));

    await sales.register('clave-1', { sessionId: 1, lines: [{ productId: 14, quantity: 2 }], discount: null }, { paymentMethodId: 2, amountTendered: 50 });

    const [url, init] = fetchImpl.mock.calls[0] as [string, RequestInit];
    const headers = init.headers as Record<string, string>;
    expect(url).toBe('http://api.test/api/sales');
    expect(headers.Authorization).toBe('Bearer token-123');
    expect(headers['Idempotency-Key']).toBe('clave-1');
    expect(JSON.parse(init.body as string)).toEqual({
      sessionId: 1,
      lines: [{ productId: 14, quantity: 2 }],
      discount: null,
      payment: { paymentMethodId: 2, amountTendered: 50 },
    });
  });

  it('omite los filtros vacíos en la URL', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response('{"items":[]}', { status: 200 }));

    await client(fetchImpl).get('/api/products', { query: { search: '', categoryId: null, sort: 'priceAsc', page: 2 } });

    expect(fetchImpl.mock.calls[0]?.[0]).toBe('http://api.test/api/products?sort=priceAsc&page=2');
  });
});
