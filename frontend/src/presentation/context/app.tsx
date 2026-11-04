import { createContext, type ReactNode, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { toAppError } from '../../application/errors';
import type { Operator } from '../../application/ports';
import type { DiscountPolicy } from '../../domain/discount';
import type { PosSession } from '../../domain/sale';
import { type Theme, themeStorage } from '../../infrastructure/storage/browser';
import { type QueryState, useQuery } from '../hooks/hooks';
import { useToast } from './feedback';
import { useServices } from './services';

/* ------------------------------------ Tema ------------------------------------ */

interface ThemeContextValue {
  readonly theme: Theme;
  readonly toggle: () => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

function initialTheme(): Theme {
  const stored = themeStorage.read();
  if (stored) return stored;
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function ThemeProvider({ children }: { readonly children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(initialTheme);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'dark' ? '#0d1517' : '#eef2f1');
  }, [theme]);

  const toggle = useCallback(() => {
    setTheme((current) => {
      const next = current === 'dark' ? 'light' : 'dark';
      themeStorage.save(next);
      return next;
    });
  }, []);

  const value = useMemo(() => ({ theme, toggle }), [theme, toggle]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (!context) throw new Error('useTheme debe usarse dentro de ThemeProvider');
  return context;
}

/* ------------------------------ Operador (sesión) ------------------------------ */

interface AuthContextValue {
  readonly operator: Operator | null;
  readonly restoring: boolean;
  readonly login: (username: string, password: string) => Promise<void>;
  readonly logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { readonly children: ReactNode }) {
  const { login: loginUseCase, restoreSession, logout: logoutUseCase, api, ticket } = useServices();
  const toast = useToast();
  const [operator, setOperator] = useState<Operator | null>(null);
  const [restoring, setRestoring] = useState(true);

  useEffect(() => {
    let active = true;
    restoreSession.execute().then((restored) => {
      if (active) {
        setOperator(restored);
        setRestoring(false);
      }
    });
    return () => {
      active = false;
    };
  }, [restoreSession]);

  const operatorRef = useRef<Operator | null>(null);
  operatorRef.current = operator;

  // Si el servidor rechaza el token (sesión vencida), se vuelve al inicio de sesión con un aviso claro.
  useEffect(() => {
    api.onUnauthorized(() => {
      if (operatorRef.current) {
        toast({ tone: 'warning', title: 'Su sesión expiró', message: 'Inicie sesión de nuevo para continuar.' });
      }
      setOperator(null);
    });
    return () => api.onUnauthorized(null);
  }, [api, toast]);

  const login = useCallback(
    async (username: string, password: string) => {
      const logged = await loginUseCase.execute(username, password);
      setOperator(logged);
    },
    [loginUseCase],
  );

  const logout = useCallback(() => {
    logoutUseCase.execute();
    ticket.clear();
    setOperator(null);
  }, [logoutUseCase, ticket]);

  const value = useMemo(() => ({ operator, restoring, login, logout }), [operator, restoring, login, logout]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth debe usarse dentro de AuthProvider');
  return context;
}

/* ----------------------- Sesión POS, conexión y descuentos ----------------------- */

interface PosContextValue {
  readonly session: QueryState<PosSession>;
  readonly odoo: { readonly healthy: boolean | null; readonly description: string };
  readonly policies: readonly DiscountPolicy[];
}

const PosContext = createContext<PosContextValue | null>(null);

const SESSION_REFRESH_MS = 60_000;
const HEALTH_REFRESH_MS = 30_000;

export function PosProvider({ children }: { readonly children: ReactNode }) {
  const { getActiveSession, checkOdooConnection, getDiscountPolicies } = useServices();
  const session = useQuery((signal) => getActiveSession.execute(signal), []);
  const policies = useQuery((signal) => getDiscountPolicies.execute(signal), []);
  const [odoo, setOdoo] = useState<PosContextValue['odoo']>({ healthy: null, description: 'Comprobando conexión con Odoo…' });

  // La sesión se vuelve a consultar periódicamente y al volver a la pestaña: alguien pudo cerrarla en Odoo.
  const { reload } = session;
  useEffect(() => {
    const timer = window.setInterval(reload, SESSION_REFRESH_MS);
    const onVisible = () => document.visibilityState === 'visible' && reload();
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [reload]);

  useEffect(() => {
    let active = true;
    const check = () =>
      checkOdooConnection
        .execute()
        .then((result) => active && setOdoo(result))
        .catch((error: unknown) => active && setOdoo({ healthy: false, description: toAppError(error).message }));
    check();
    const timer = window.setInterval(check, HEALTH_REFRESH_MS);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, [checkOdooConnection]);

  const value = useMemo(() => ({ session, odoo, policies: policies.data ?? [] }), [session, odoo, policies.data]);
  return <PosContext.Provider value={value}>{children}</PosContext.Provider>;
}

export function usePos(): PosContextValue {
  const context = useContext(PosContext);
  if (!context) throw new Error('usePos debe usarse dentro de PosProvider');
  return context;
}
