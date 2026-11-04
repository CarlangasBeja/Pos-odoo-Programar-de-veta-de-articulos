export type DiscountKind = 'manual' | 'automatic';

/** Política de descuento habilitada en el backend para el rol del operador. */
export interface DiscountPolicy {
  readonly code: string;
  readonly name: string;
  readonly description: string;
  readonly kind: DiscountKind;
  /** Máximo autorizado para el rol (solo políticas manuales). */
  readonly maxPercentage: number | null;
}

export interface DiscountSelection {
  readonly policy: DiscountPolicy;
  readonly percentage: number | null;
}

/**
 * Validación previa en el cliente para avisar antes de enviar (RF-08).
 * El backend vuelve a validar siempre: esta comprobación solo mejora la experiencia.
 */
export function validateDiscount(selection: DiscountSelection): string | null {
  const { policy, percentage } = selection;
  if (policy.kind === 'automatic') {
    return null;
  }
  if (percentage === null || !Number.isFinite(percentage) || percentage <= 0) {
    return 'Indique un porcentaje de descuento mayor que cero.';
  }
  if (Math.round(percentage * 100) / 100 !== percentage) {
    return 'El porcentaje admite como máximo dos decimales.';
  }
  if (policy.maxPercentage !== null && percentage > policy.maxPercentage) {
    return `Su rol puede aplicar como máximo ${policy.maxPercentage} % de descuento manual. Pida a un supervisor que registre la venta.`;
  }
  if (percentage > 100) {
    return 'El descuento no puede superar el 100 %.';
  }
  return null;
}

/** Porcentaje que se puede reflejar en el total provisional (los automáticos dependen de reglas del servidor). */
export function provisionalPercent(selection: DiscountSelection | null): number {
  return selection?.policy.kind === 'manual' && validateDiscount(selection) === null ? (selection.percentage ?? 0) : 0;
}
