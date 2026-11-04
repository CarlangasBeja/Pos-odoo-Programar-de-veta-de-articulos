import { AppError } from '../errors';
import type { AuthGateway, Operator, TokenStore } from '../ports';

/*
 * Casos de uso de la sesión del operador. Uno por clase y con un solo método `execute` (estilo Use Case / Interactor):
 * no se agrupan en un "AuthService" para que cada operación tenga sus propias dependencias y se pruebe por separado.
 * El token solo vive en la pestaña actual (sessionStorage).
 */

/** Identifica al operador y guarda su token. */
export class LoginUseCase {
  constructor(
    private readonly auth: AuthGateway,
    private readonly tokens: TokenStore,
  ) {}

  async execute(username: string, password: string): Promise<Operator> {
    if (!username.trim() || !password) {
      throw new AppError('validation', 'Ingrese su usuario y su contraseña.', 'AUTH.REQUIRED');
    }
    const token = await this.auth.login(username.trim(), password);
    this.tokens.save(token.accessToken, token.expiresIn);
    try {
      return await this.auth.me();
    } catch (error) {
      this.tokens.clear();
      throw error;
    }
  }
}

/** Recupera la sesión si la pestaña ya tenía un token válido. */
export class RestoreSessionUseCase {
  constructor(
    private readonly auth: AuthGateway,
    private readonly tokens: TokenStore,
  ) {}

  async execute(): Promise<Operator | null> {
    if (!this.tokens.read()) {
      return null;
    }
    try {
      return await this.auth.me();
    } catch {
      this.tokens.clear();
      return null;
    }
  }
}

/** Cierra la sesión del operador en esta pestaña. */
export class LogoutUseCase {
  constructor(private readonly tokens: TokenStore) {}

  execute(): void {
    this.tokens.clear();
  }
}
