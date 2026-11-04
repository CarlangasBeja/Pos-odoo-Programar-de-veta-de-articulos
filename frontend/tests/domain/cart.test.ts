import { describe, expect, it } from 'vitest';
import { addProduct, cartTotals, changeQuantity, emptyCart, itemCount, removeProduct, restoreLine, validateQuantity } from '../../src/domain/cart';
import { formatMoney, roundToPrecision } from '../../src/domain/money';
import { agua, cafe, COP, descontinuado, USD } from '../fakes';

function cartWith(...items: Array<[typeof cafe, number]>) {
  return items.reduce((cart, [p, q]) => {
    const result = addProduct(cart, p, q);
    if (!result.ok) throw new Error(result.error);
    return result.cart;
  }, emptyCart);
}

/** P07: el carrito actualiza cantidades y el total provisional. */
describe('Carrito (P07)', () => {
  it('agrega productos y suma la cantidad cuando se repite el mismo producto', () => {
    const cart = cartWith([cafe, 1], [cafe, 2], [agua, 1]);

    expect(cart.lines).toHaveLength(2);
    expect(cart.lines[0]?.quantity).toBe(3);
    expect(itemCount(cart)).toBe(4);
  });

  it('actualiza el total provisional al cambiar cantidades', () => {
    const cart = cartWith([cafe, 2], [agua, 1]);
    expect(cartTotals(cart, USD, 'global').total).toBe(29.75);

    const changed = changeQuantity(cart, cafe.id, 5);
    expect(changed.ok).toBe(true);
    if (changed.ok) {
      const totals = cartTotals(changed.cart, USD, 'global');
      expect(totals.untaxed).toBe(55);
      expect(totals.tax).toBe(10.45);
      expect(totals.total).toBe(65.45);
    }
  });

  it('aplica el descuento manual al total provisional', () => {
    const totals = cartTotals(cartWith([cafe, 2], [agua, 1]), USD, 'global', 10);

    expect(totals.total).toBe(26.78); // mismo valor que calcula el backend con las reglas de Odoo
    expect(totals.discount).toBe(2.97);
  });

  it.each([0, -1, Number.NaN, 10_001, 1.2345])('rechaza la cantidad inválida %s (RN-02)', (quantity) => {
    expect(validateQuantity(quantity)).not.toBeNull();
    const result = changeQuantity(cartWith([cafe, 1]), cafe.id, quantity);
    expect(result.ok).toBe(false);
  });

  it('no agrega un producto que no se puede vender y explica por qué', () => {
    const result = addProduct(emptyCart, descontinuado);

    expect(result).toEqual({ ok: false, error: 'No está habilitado para el punto de venta.' });
  });

  it('quita un producto y lo puede restaurar en su posición (Deshacer)', () => {
    const cart = cartWith([cafe, 1], [agua, 2]);
    const removed = removeProduct(cart, cafe.id);
    expect(removed.lines.map((l) => l.product.id)).toEqual([agua.id]);

    const restored = restoreLine(removed, cart.lines[0]!, 0);
    expect(restored.lines.map((l) => l.product.id)).toEqual([cafe.id, agua.id]);
  });

  it('un carrito vacío totaliza cero', () => {
    expect(cartTotals(emptyCart, COP, 'perLine')).toMatchObject({ total: 0, tax: 0, untaxed: 0 });
  });
});

describe('Dinero', () => {
  it('redondea mitad hacia arriba como Odoo, sin errores de coma flotante', () => {
    expect(roundToPrecision(2.675, 0.01)).toBe(2.68);
    expect(roundToPrecision(1.005, 0.01)).toBe(1.01);
    expect(roundToPrecision(1500.5, 1)).toBe(1501);
    expect(roundToPrecision(-2.675, 0.01)).toBe(-2.68);
  });

  it('formatea con la convención colombiana y la posición del símbolo de Odoo', () => {
    expect(formatMoney(19671, COP)).toBe('$ 19.671');
    expect(formatMoney(1234.5, USD)).toBe('$ 1.234,50');
    expect(formatMoney(10, { ...USD, symbol: '€', symbolPosition: 'after' })).toBe('10,00 €');
  });
});
