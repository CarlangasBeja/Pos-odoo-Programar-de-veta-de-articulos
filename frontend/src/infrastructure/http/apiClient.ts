import { AppError, type AppErrorKind, type ErrorDetail } from '../../application/errors';
import type { TokenStore } from '../../application/ports';

export interface ApiClientOptions {
  readonly baseUrl: string;
  readonly tokens: TokenStore;
  readonly timeoutMs?: number;
  readonly fetchImpl?: typeof fetch;
}

export interface RequestOptions {
  readonly query?: Record<string, string | number | boolean | null | undefined>;
  readonly headers?: Record<string, string>;
  readonly signal?: AbortSignal;
  readonly timeoutMs?: number;
}

interface ProblemDetails {
  readonly title?: string;
  readonly detail?: string;
  readonly code?: string;
  readonly errors?: ReadonlyArray<{ code?: string; message?: string; target?: string | null }>;
}

const NETWORK_MESSAGE =
  'No se pudo conectar con el servidor del POS. Verifique que la API (.NET) esté en ejecución y que tenga conexión de red.';
const TIMEOUT_MESSAGE = 'El servidor del POS no respondió a tiempo.';

const FALLBACK_BY_STATUS: Record<number, string> = {
  400: 'Los datos enviados no son válidos.',
  401: 'Su sesión no es válida o expiró. Inicie sesión de nuevo.',
  403: 'No tiene permisos para realizar esta operación.',
  404: 'El recurso solicitado no existe.',
  409: 'La operación entra en conflicto con otra que está en curso.',
  422: 'La operación no cumple las reglas del negocio.',
  429: 'Demasiados intentos. Espere un momento e intente de nuevo.',
  503: 'Odoo no está disponible en este momento. Intente de nuevo en unos segundos.',
};

function kindFor(status: number): AppErrorKind {
  if (status === 400) return 'validation';
  if (status === 401) return 'unauthorized';
  if (status === 403) return 'forbidden';
  if (status === 404) return 'notFound';
  if (status === 409) return 'conflict';
  if (status === 422 || status === 429) return 'business';
  if (status === 502 || status === 503 || status === 504) return 'unavailable';
  return 'unexpected';
}

/**
 * Cliente HTTP de la API .NET. Es el único lugar que conoce fetch, el token Bearer y el formato
 * Problem Details (RFC 9457): todo lo demás recibe datos o un AppError con un mensaje comprensible (P08).
 */
export class ApiClient {
  private readonly baseUrl: string;
  private readonly timeoutMs: number;
  private readonly fetchImpl: typeof fetch;
  private unauthorizedHandler: (() => void) | null = null;

  constructor(private readonly options: ApiClientOptions) {
    this.baseUrl = options.baseUrl.replace(/\/$/, '');
    this.timeoutMs = options.timeoutMs ?? 20_000;
    this.fetchImpl = options.fetchImpl ?? ((input, init) => fetch(input, init));
  }

  /** Se invoca cuando el servidor rechaza el token (sesión vencida). */
  onUnauthorized(handler: (() => void) | null): void {
    this.unauthorizedHandler = handler;
  }

  get<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('GET', path, undefined, options);
  }

  post<T>(path: string, body: unknown, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('POST', path, body, options);
  }

  /** Solicitud que devuelve el estado HTTP sin convertirlo en error (para el estado de salud). */
  async probe<T>(path: string, signal?: AbortSignal): Promise<{ readonly status: number; readonly body: T | null }> {
    const response = await this.send('GET', path, undefined, { signal });
    return { status: response.status, body: (await this.readJson(response)) as T | null };
  }

  private async request<T>(method: string, path: string, body: unknown, options: RequestOptions): Promise<T> {
    const response = await this.send(method, path, body, options);
    if (response.ok) {
      return (response.status === 204 ? undefined : await this.readJson(response)) as T;
    }

    const problem = (await this.readJson(response)) as ProblemDetails | null;
    const error = this.toError(response.status, problem);
    if (error.kind === 'unauthorized' && !path.endsWith('/auth/login')) {
      this.options.tokens.clear();
      this.unauthorizedHandler?.();
    }
    throw error;
  }

  private async send(method: string, path: string, body: unknown, options: RequestOptions): Promise<Response> {
    const controller = new AbortController();
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, options.timeoutMs ?? this.timeoutMs);
    const abortFromCaller = () => controller.abort();
    options.signal?.addEventListener('abort', abortFromCaller, { once: true });

    const headers: Record<string, string> = { Accept: 'application/json', ...options.headers };
    const token = this.options.tokens.read();
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }

    try {
      return await this.fetchImpl(this.url(path, options.query), {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: controller.signal,
      });
    } catch (error) {
      if (options.signal?.aborted) {
        throw error; // cancelado por quien llamó (por ejemplo, el usuario siguió escribiendo): no es un error que mostrar
      }
      if (timedOut) {
        throw new AppError('timeout', TIMEOUT_MESSAGE, 'HTTP.TIMEOUT');
      }
      throw new AppError('network', NETWORK_MESSAGE, 'HTTP.NETWORK');
    } finally {
      clearTimeout(timer);
      options.signal?.removeEventListener('abort', abortFromCaller);
    }
  }

  private url(path: string, query?: RequestOptions['query']): string {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query ?? {})) {
      if (value !== null && value !== undefined && value !== '') {
        params.set(key, String(value));
      }
    }
    const search = params.toString();
    return `${this.baseUrl}${path}${search ? `?${search}` : ''}`;
  }

  private async readJson(response: Response): Promise<unknown> {
    const text = await response.text();
    if (!text) {
      return null;
    }
    try {
      return JSON.parse(text) as unknown;
    } catch {
      return null;
    }
  }

  private toError(status: number, problem: ProblemDetails | null): AppError {
    const details: ErrorDetail[] = (problem?.errors ?? []).map((e) => ({
      code: e.code ?? 'ERROR',
      message: e.message ?? '',
      target: e.target ?? null,
    }));
    const message = problem?.detail || FALLBACK_BY_STATUS[status] || `El servidor respondió con un error inesperado (HTTP ${status}).`;
    return new AppError(kindFor(status), message, problem?.code ?? `HTTP.${status}`, details, status);
  }
}
