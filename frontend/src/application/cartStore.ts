import { addProduct, type Cart, type CartLine, changeQuantity, emptyCart, removeProduct, restoreLine } from '../domain/cart';
import type { DiscountSelection } from '../domain/discount';
import type { Product } from '../domain/product';

export interface TicketState {
  readonly cart: Cart;
  readonly discount: DiscountSelection | null;
  /** Mientras se cobra, el ticket no se edita (RN-06: una venta confirmada no se edita como venta en preparación). */
  readonly locked: boolean;
}

export type TicketChange = { readonly ok: true } | { readonly ok: false; readonly error: string };

type Listener = () => void;

const LOCKED_MESSAGE = 'El ticket está bloqueado mientras se procesa el cobro.';

/**
 * Estado del ticket independiente de React (patrón Observer): los componentes se suscriben
 * con useSyncExternalStore y los casos de uso lo modifican sin conocer la interfaz.
 */
export class TicketStore {
  private state: TicketState = { cart: emptyCart, discount: null, locked: false };
  private readonly listeners = new Set<Listener>();

  getSnapshot = (): TicketState => this.state;

  subscribe = (listener: Listener): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  add(product: Product, quantity = 1): TicketChange {
    return this.applyCart((cart) => addProduct(cart, product, quantity));
  }

  setQuantity(productId: number, quantity: number): TicketChange {
    return this.applyCart((cart) => changeQuantity(cart, productId, quantity));
  }

  /** Quita una línea y devuelve lo necesario para deshacer la acción. */
  remove(productId: number): { line: CartLine; index: number } | null {
    if (this.state.locked) {
      return null;
    }
    const index = this.state.cart.lines.findIndex((l) => l.product.id === productId);
    const line = this.state.cart.lines[index];
    if (!line) {
      return null;
    }
    const cart = removeProduct(this.state.cart, productId);
    this.set({ ...this.state, cart, discount: cart.lines.length === 0 ? null : this.state.discount });
    return { line, index };
  }

  undoRemove(line: CartLine, index: number): void {
    if (!this.state.locked) {
      this.set({ ...this.state, cart: restoreLine(this.state.cart, line, index) });
    }
  }

  setDiscount(discount: DiscountSelection | null): TicketChange {
    if (this.state.locked) {
      return { ok: false, error: LOCKED_MESSAGE };
    }
    this.set({ ...this.state, discount });
    return { ok: true };
  }

  clear(): void {
    this.set({ cart: emptyCart, discount: null, locked: false });
  }

  lock(): void {
    this.set({ ...this.state, locked: true });
  }

  unlock(): void {
    this.set({ ...this.state, locked: false });
  }

  private applyCart(change: (cart: Cart) => { ok: true; cart: Cart } | { ok: false; error: string }): TicketChange {
    if (this.state.locked) {
      return { ok: false, error: LOCKED_MESSAGE };
    }
    const result = change(this.state.cart);
    if (!result.ok) {
      return result;
    }
    this.set({ ...this.state, cart: result.cart });
    return { ok: true };
  }

  private set(state: TicketState): void {
    this.state = state;
    this.listeners.forEach((listener) => listener());
  }
}
