import type { OrderState } from '../../../domain/sale';

export function stateBadgeClass(state: OrderState): string {
  return state === 'paid' || state === 'posted' ? 'badge badge--success' : state === 'cancelled' ? 'badge badge--danger' : 'badge badge--warning';
}
