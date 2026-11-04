import { type DependencyList, useCallback, useEffect, useRef, useState, useSyncExternalStore } from 'react';
import type { TicketState } from '../../application/cartStore';
import { type AppError, toAppError } from '../../application/errors';
import { useServices } from '../context/services';

/** Estado del ticket (se vuelve a pintar cada vez que cambia el almacén observable). */
export function useTicket(): TicketState {
  const { ticket } = useServices();
  return useSyncExternalStore(ticket.subscribe, ticket.getSnapshot, ticket.getSnapshot);
}

export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delayMs);
    return () => window.clearTimeout(timer);
  }, [value, delayMs]);
  return debounced;
}

export interface QueryState<T> {
  readonly data: T | undefined;
  readonly error: AppError | null;
  /** Primera carga (sin datos previos). */
  readonly loading: boolean;
  /** Actualizando con datos previos visibles (por ejemplo, mientras se escribe en el buscador). */
  readonly refreshing: boolean;
  readonly reload: () => void;
}

/**
 * Ejecuta una consulta y la cancela si cambian los parámetros antes de que termine
 * (evita que una respuesta vieja pise a una nueva al escribir rápido en un buscador).
 */
export function useQuery<T>(load: (signal: AbortSignal) => Promise<T>, deps: DependencyList, enabled = true): QueryState<T> {
  const [data, setData] = useState<T | undefined>(undefined);
  const [error, setError] = useState<AppError | null>(null);
  const [pending, setPending] = useState(enabled);
  const [version, setVersion] = useState(0);
  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    if (!enabled) {
      setPending(false);
      return undefined;
    }
    const controller = new AbortController();
    setPending(true);
    loadRef
      .current(controller.signal)
      .then((result) => {
        if (!controller.signal.aborted) {
          setData(result);
          setError(null);
        }
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) {
          setError(toAppError(reason));
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setPending(false);
        }
      });
    return () => controller.abort();
    // Las dependencias las define quien llama (igual que en useEffect).
  }, [...deps, version, enabled]);

  const reload = useCallback(() => setVersion((v) => v + 1), []);
  return { data, error, loading: pending && data === undefined, refreshing: pending && data !== undefined, reload };
}

export function useMediaQuery(query: string): boolean {
  const getMatch = () => typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia(query).matches;
  const [matches, setMatches] = useState(getMatch);
  useEffect(() => {
    if (typeof window.matchMedia !== 'function') {
      return undefined;
    }
    const media = window.matchMedia(query);
    const onChange = () => setMatches(media.matches);
    onChange();
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, [query]);
  return matches;
}

/** Aviso del navegador si se intenta cerrar o recargar la pestaña con trabajo sin guardar. */
export function useLeaveWarning(active: boolean): void {
  useEffect(() => {
    if (!active) {
      return undefined;
    }
    const handler = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [active]);
}
