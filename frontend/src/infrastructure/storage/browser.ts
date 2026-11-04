import type { KeyGenerator, TokenStore } from '../../application/ports';

const TOKEN_KEY = 'pos.token';

/**
 * Token del operador en sessionStorage: se borra al cerrar la pestaña y no se comparte entre pestañas.
 * Si el navegador bloquea el almacenamiento, se usa memoria (la sesión dura lo que dure la página).
 */
export class SessionTokenStore implements TokenStore {
  private memory: { token: string; expiresAt: number } | null = null;

  read(): string | null {
    const stored = this.load();
    if (!stored || stored.expiresAt <= Date.now()) {
      this.clear();
      return null;
    }
    return stored.token;
  }

  save(token: string, expiresInSeconds: number): void {
    const value = { token, expiresAt: Date.now() + expiresInSeconds * 1000 };
    this.memory = value;
    try {
      sessionStorage.setItem(TOKEN_KEY, JSON.stringify(value));
    } catch {
      // almacenamiento no disponible: se conserva en memoria
    }
  }

  clear(): void {
    this.memory = null;
    try {
      sessionStorage.removeItem(TOKEN_KEY);
    } catch {
      // sin almacenamiento disponible
    }
  }

  private load(): { token: string; expiresAt: number } | null {
    try {
      const raw = sessionStorage.getItem(TOKEN_KEY);
      return raw ? (JSON.parse(raw) as { token: string; expiresAt: number }) : this.memory;
    } catch {
      return this.memory;
    }
  }
}

/**
 * UUID v4 para las claves de idempotencia. `randomUUID` solo existe en contextos seguros (https o localhost);
 * si el POS se abre por la IP de la red local, se arma el UUID con `getRandomValues`, que sí está disponible.
 */
export class CryptoKeyGenerator implements KeyGenerator {
  next(): string {
    if (typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = ((bytes[6] ?? 0) & 0x0f) | 0x40;
    bytes[8] = ((bytes[8] ?? 0) & 0x3f) | 0x80;
    const hex = [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }
}

export type Theme = 'light' | 'dark';
const THEME_KEY = 'pos.theme';

/** Preferencia de tema por navegador (conveniencia local; no es un dato de negocio). */
export const themeStorage = {
  read(): Theme | null {
    try {
      const value = localStorage.getItem(THEME_KEY);
      return value === 'light' || value === 'dark' ? value : null;
    } catch {
      return null;
    }
  },
  save(theme: Theme): void {
    try {
      localStorage.setItem(THEME_KEY, theme);
    } catch {
      // sin almacenamiento disponible: el tema dura lo que dure la página
    }
  },
};
