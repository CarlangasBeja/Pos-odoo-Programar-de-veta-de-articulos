import { describe, expect, it } from 'vitest';
import type { AuthGateway, Operator } from '../../src/application/ports';
import { LoginUseCase, LogoutUseCase, RestoreSessionUseCase } from '../../src/application/usecases/auth';
import { MAX_SEARCH_LENGTH, SearchProductsUseCase, SearchSalesHistoryUseCase } from '../../src/application/usecases/queries';
import { cafe, FakeCatalog, FakeSales, MemoryTokenStore } from '../fakes';

const ANA: Operator = { username: 'cajero', displayName: 'Ana Cajera', role: 'cashier', roleLabel: 'Cajero' };

/** Doble del puerto de autenticación: permite simular que el servidor rechaza el token. */
class FakeAuth implements AuthGateway {
  meFails = false;
  readonly logins: string[] = [];

  async login(username: string) {
    this.logins.push(username);
    return { accessToken: 'token-de-prueba', expiresIn: 3600 };
  }

  async me(): Promise<Operator> {
    if (this.meFails) throw new Error('token rechazado');
    return ANA;
  }
}

describe('Casos de uso de la sesión del operador (uno por clase)', () => {
  it('LoginUseCase guarda el token y devuelve el operador', async () => {
    const auth = new FakeAuth();
    const tokens = new MemoryTokenStore();

    const operator = await new LoginUseCase(auth, tokens).execute('  cajero  ', 'Cajero2026!');

    expect(operator).toEqual(ANA);
    expect(auth.logins).toEqual(['cajero']);
    expect(tokens.read()).toBe('token-de-prueba');
  });

  it('LoginUseCase exige usuario y contraseña antes de llamar a la API', async () => {
    const auth = new FakeAuth();

    await expect(new LoginUseCase(auth, new MemoryTokenStore()).execute(' ', '')).rejects.toMatchObject({ code: 'AUTH.REQUIRED' });
    expect(auth.logins).toEqual([]);
  });

  it('LoginUseCase no deja un token guardado si no se pudo identificar al operador', async () => {
    const auth = new FakeAuth();
    auth.meFails = true;
    const tokens = new MemoryTokenStore();

    await expect(new LoginUseCase(auth, tokens).execute('cajero', 'Cajero2026!')).rejects.toThrow();
    expect(tokens.read()).toBeNull();
  });

  it('RestoreSessionUseCase descarta un token que el servidor ya no acepta', async () => {
    const auth = new FakeAuth();
    auth.meFails = true;
    const tokens = new MemoryTokenStore();
    tokens.token = 'vencido';

    expect(await new RestoreSessionUseCase(auth, tokens).execute()).toBeNull();
    expect(tokens.read()).toBeNull();
  });

  it('LogoutUseCase borra el token de la pestaña', () => {
    const tokens = new MemoryTokenStore();
    tokens.token = 'token';

    new LogoutUseCase(tokens).execute();

    expect(tokens.read()).toBeNull();
  });
});

describe('Casos de uso de consulta', () => {
  it('SearchProductsUseCase recorta los espacios y limita la longitud del texto', async () => {
    const catalog = new FakeCatalog([cafe]);

    await new SearchProductsUseCase(catalog).execute({
      search: `   ${'x'.repeat(MAX_SEARCH_LENGTH + 20)}   `,
      categoryId: null,
      onlyInStock: false,
      sort: 'nameAsc',
      page: 1,
      pageSize: 24,
    });

    expect(catalog.queries[0]?.search).toHaveLength(MAX_SEARCH_LENGTH);
  });

  it('SearchSalesHistoryUseCase rechaza un rango de fechas invertido sin llamar a la API', async () => {
    const useCase = new SearchSalesHistoryUseCase(new FakeSales());

    await expect(
      useCase.execute({ search: '', state: 'all', from: '2026-10-30', to: '2026-10-01', page: 1, pageSize: 15 }),
    ).rejects.toMatchObject({ code: 'HISTORY.INVALID_RANGE' });
  });
});
