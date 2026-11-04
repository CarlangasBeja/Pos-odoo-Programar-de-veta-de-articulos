/** Clasificación de los errores que la interfaz sabe presentar. */
export type AppErrorKind =
  | 'network'
  | 'timeout'
  | 'unauthorized'
  | 'forbidden'
  | 'validation'
  | 'business'
  | 'notFound'
  | 'conflict'
  | 'unavailable'
  | 'unexpected';

export interface ErrorDetail {
  readonly code: string;
  readonly message: string;
  readonly target: string | null;
}

/** Error comprensible para el operador (P08). El mensaje siempre dice qué pasó y qué hacer. */
export class AppError extends Error {
  constructor(
    public readonly kind: AppErrorKind,
    message: string,
    public readonly code = 'APP.ERROR',
    public readonly details: readonly ErrorDetail[] = [],
    public readonly status: number | null = null,
  ) {
    super(message);
    this.name = 'AppError';
  }

  /** Mensaje del primer error asociado a un campo (por ejemplo "discount" o "lines[0].quantity"). */
  messageFor(target: string): string | null {
    return this.details.find((d) => d.target === target)?.message ?? null;
  }

  /** Errores en los que reintentar exactamente la misma operación tiene sentido. */
  get isTransient(): boolean {
    return this.kind === 'network' || this.kind === 'timeout' || this.kind === 'unavailable';
  }
}

export function toAppError(error: unknown): AppError {
  if (error instanceof AppError) {
    return error;
  }
  return new AppError('unexpected', 'Ocurrió un error inesperado en la aplicación. Recargue la página e intente de nuevo.', 'APP.UNEXPECTED');
}
