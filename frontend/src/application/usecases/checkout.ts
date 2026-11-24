import { FINAL_CONSUMER, type SaleCustomer } from '../../domain/customer';
import type { TicketState, TicketStore } from '../cartStore';
import { AppError, toAppError } from '../errors';
import type { KeyGenerator, PaymentInput, SaleDraft, SalesGateway } from '../ports';
import type { SalePreview, SaleRegistration } from '../../domain/sale';

/** Odoo no dejó crear el cliente: la venta no se envió y la clave sigue libre. */
export const CUSTOMER_FORBIDDEN = 'ODOO.CUSTOMER_FORBIDDEN';
export const REJECTED_BY_ODOO = 'SALE.REJECTED_BY_ODOO';
const ATTEMPT_NOT_FOUND = 'SALE.ATTEMPT_NOT_FOUND';

export type CheckoutOutcome =
  | { readonly kind: 'registered'; readonly sale: SaleRegistration }
  | { readonly kind: 'uncertain'; readonly sale: SaleRegistration | null; readonly message: string }
  | { readonly kind: 'failed'; readonly error: AppError };

export function toSaleDraft(sessionId: number, ticket: TicketState): SaleDraft {
  return {
    sessionId,
    lines: ticket.cart.lines.map((l) => ({ productId: l.product.id, quantity: l.quantity })),
    discount: ticket.discount ? { policyCode: ticket.discount.policy.code, percentage: ticket.discount.percentage } : null,
  };
}

/**
 * Proceso de cobro de UNA venta. Administra la clave de idempotencia:
 * - se crea al abrir el cobro y se reutiliza en cada reintento (RN-08: nunca se duplica la venta);
 * - solo se renueva cuando la venta terminó (registrada) o cuando Odoo la rechazó de forma definitiva.
 * Mientras el resultado es incierto, el ticket queda bloqueado y no se informa éxito (RN-07, RF-17).
 */
export class CheckoutProcess {
  private key: string;
  private uncertain = false;

  constructor(
    private readonly sales: SalesGateway,
    private readonly keys: KeyGenerator,
    private readonly ticket: TicketStore,
    private readonly sessionId: number,
  ) {
    this.key = keys.next();
  }

  get idempotencyKey(): string {
    return this.key;
  }

  /** Mientras haya un resultado incierto no se puede cancelar el cobro ni editar el ticket. */
  get isUncertain(): boolean {
    return this.uncertain;
  }

  /** Total verificado por el backend con precios y descuentos de Odoo (RN-03, RN-04). */
  verify(signal?: AbortSignal): Promise<SalePreview> {
    return this.sales.preview(toSaleDraft(this.sessionId, this.ticket.getSnapshot()), signal);
  }

  /** Registra la venta. Un reintento con la misma clave debe enviar el mismo pago y el mismo cliente. */
  async pay(payment: PaymentInput, customer: SaleCustomer = FINAL_CONSUMER): Promise<CheckoutOutcome> {
    const draft = toSaleDraft(this.sessionId, this.ticket.getSnapshot());
    this.ticket.lock();
    try {
      return this.settle(await this.sales.register(this.key, draft, payment, customer));
    } catch (error) {
      return this.handleFailure(toAppError(error));
    }
  }

  /** Consulta el estado real de una venta incierta sin reenviarla. */
  async checkStatus(): Promise<CheckoutOutcome> {
    try {
      return this.settle(await this.sales.checkAttempt(this.key));
    } catch (error) {
      const appError = toAppError(error);
      if (appError.code === ATTEMPT_NOT_FOUND) {
        // El servidor nunca recibió la venta: es seguro volver a intentar o editar el ticket.
        this.uncertain = false;
        this.ticket.unlock();
        return {
          kind: 'failed',
          error: new AppError('notFound', 'La venta no llegó al servidor, no se registró nada. Puede cobrar de nuevo.', ATTEMPT_NOT_FOUND),
        };
      }
      return { kind: 'uncertain', sale: null, message: appError.message };
    }
  }

  /** Cierra el cobro sin registrar. Solo se permite si no hay un resultado pendiente de confirmar. */
  cancel(): boolean {
    if (this.uncertain) {
      return false;
    }
    this.ticket.unlock();
    return true;
  }

  private settle(sale: SaleRegistration): CheckoutOutcome {
    if (sale.status === 'registered') {
      this.uncertain = false;
      this.ticket.clear();
      this.key = this.keys.next();
      return { kind: 'registered', sale };
    }
    this.uncertain = true;
    return { kind: 'uncertain', sale, message: sale.message };
  }

  private handleFailure(error: AppError): CheckoutOutcome {
    if (error.code === CUSTOMER_FORBIDDEN) {
      // Se sabe con certeza que la venta no llegó a Odoo (falló antes, al crear el cliente): no es un resultado incierto.
      this.uncertain = false;
      this.ticket.unlock();
      return { kind: 'failed', error };
    }
    if (error.isTransient) {
      // Pudo o no llegar al servidor: se trata como incierto y se conserva la misma clave.
      this.uncertain = true;
      return {
        kind: 'uncertain',
        sale: null,
        message: `${error.message} No vuelva a cobrar todavía: verifique el estado; reintentar es seguro y no duplica la venta.`,
      };
    }

    this.uncertain = false;
    this.ticket.unlock();
    if (error.code === REJECTED_BY_ODOO) {
      this.key = this.keys.next(); // Odoo guardó ese rechazo para esta clave; el próximo intento es una venta nueva.
    }
    return { kind: 'failed', error };
  }
}

/** RF-09 a RF-12: abre el cobro del ticket actual con una clave de idempotencia nueva. */
export class StartCheckoutUseCase {
  constructor(
    private readonly sales: SalesGateway,
    private readonly keys: KeyGenerator,
    private readonly ticket: TicketStore,
  ) {}

  execute(sessionId: number): CheckoutProcess {
    return new CheckoutProcess(this.sales, this.keys, this.ticket, sessionId);
  }
}
