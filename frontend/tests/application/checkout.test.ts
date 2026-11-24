import { beforeEach, describe, expect, it } from 'vitest';
import { TicketStore } from '../../src/application/cartStore';
import { AppError } from '../../src/application/errors';
import { CUSTOMER_FORBIDDEN, REJECTED_BY_ODOO, StartCheckoutUseCase } from '../../src/application/usecases/checkout';
import { agua, cafe, FakeSales, registration, SequenceKeys } from '../fakes';

const PAYMENT = { paymentMethodId: 2, amountTendered: 50 };

describe('Proceso de cobro', () => {
  let ticket: TicketStore;
  let sales: FakeSales;
  let keys: SequenceKeys;
  let startCheckout: StartCheckoutUseCase;

  beforeEach(() => {
    ticket = new TicketStore();
    ticket.add(cafe, 2);
    ticket.add(agua, 1);
    sales = new FakeSales();
    keys = new SequenceKeys();
    startCheckout = new StartCheckoutUseCase(sales, keys, ticket);
  });

  it('registra la venta, vacía el ticket y envía solo productos y cantidades (nunca precios)', async () => {
    const outcome = await startCheckout.execute(1).pay(PAYMENT);

    expect(outcome.kind).toBe('registered');
    expect(ticket.getSnapshot().cart.lines).toHaveLength(0);
    expect(sales.registerCalls[0]?.draft).toEqual({
      sessionId: 1,
      lines: [
        { productId: cafe.id, quantity: 2 },
        { productId: agua.id, quantity: 1 },
      ],
      discount: null,
    });
  });

  it('bloquea el ticket mientras se cobra (RN-06)', async () => {
    let resolve!: () => void;
    sales.registerBehavior = (key) => new Promise((r) => (resolve = () => r(registration(key, 'registered', 50))));

    const paying = startCheckout.execute(1).pay(PAYMENT);
    expect(ticket.getSnapshot().locked).toBe(true);
    expect(ticket.add(cafe).ok).toBe(false);

    resolve();
    await paying;
    expect(ticket.getSnapshot().locked).toBe(false);
  });

  it('P08: si la API .NET no responde, informa un mensaje comprensible y no declara éxito', async () => {
    sales.registerBehavior = async () => {
      throw new AppError('network', 'No se pudo conectar con el servidor del POS. Verifique que la API (.NET) esté en ejecución y que tenga conexión de red.', 'HTTP.NETWORK');
    };

    const outcome = await startCheckout.execute(1).pay(PAYMENT);

    expect(outcome.kind).toBe('uncertain');
    if (outcome.kind === 'uncertain') {
      expect(outcome.message).toContain('No se pudo conectar con el servidor del POS');
      expect(outcome.message).toContain('no duplica la venta');
    }
    expect(ticket.getSnapshot().cart.lines).toHaveLength(2);
    expect(ticket.getSnapshot().locked).toBe(true);
  });

  it('RN-08: los reintentos de una venta incierta usan la MISMA clave de idempotencia', async () => {
    sales.registerBehavior = async (key) => registration(key, 'uncertain', 50);
    const process = startCheckout.execute(1);

    await process.pay(PAYMENT);
    sales.registerBehavior = async (key) => registration(key, 'registered', 50);
    const retry = await process.pay(PAYMENT);

    expect(retry.kind).toBe('registered');
    expect(sales.registerCalls.map((c) => c.key)).toEqual([keys.issued[0], keys.issued[0]]);
  });

  it('verificar el estado concilia una venta incierta sin reenviarla', async () => {
    sales.registerBehavior = async (key) => registration(key, 'uncertain', 50);
    const process = startCheckout.execute(1);
    await process.pay(PAYMENT);

    const checked = await process.checkStatus();

    expect(checked.kind).toBe('registered');
    expect(sales.registerCalls).toHaveLength(1);
    expect(ticket.getSnapshot().cart.lines).toHaveLength(0);
  });

  it('si el servidor nunca recibió la venta, desbloquea el ticket para cobrar de nuevo', async () => {
    sales.registerBehavior = async () => {
      throw new AppError('timeout', 'El servidor del POS no respondió a tiempo.', 'HTTP.TIMEOUT');
    };
    sales.checkBehavior = async () => {
      throw new AppError('notFound', 'No hay ningún intento de venta registrado.', 'SALE.ATTEMPT_NOT_FOUND');
    };
    const process = startCheckout.execute(1);
    await process.pay(PAYMENT);

    const checked = await process.checkStatus();

    expect(checked.kind).toBe('failed');
    expect(process.isUncertain).toBe(false);
    expect(ticket.getSnapshot().locked).toBe(false);
  });

  it('un rechazo definitivo de Odoo desbloquea el ticket y renueva la clave para el próximo intento', async () => {
    sales.registerBehavior = async () => {
      throw new AppError('business', 'La venta no se registró. Odoo rechazó la operación.', REJECTED_BY_ODOO);
    };
    const process = startCheckout.execute(1);

    const outcome = await process.pay(PAYMENT);
    sales.registerBehavior = async (key) => registration(key, 'registered', 50);
    await process.pay(PAYMENT);

    expect(outcome.kind).toBe('failed');
    expect(sales.registerCalls[0]?.key).not.toBe(sales.registerCalls[1]?.key);
  });

  it('si Odoo no deja crear el cliente, la venta NO quedó incierta: se informa el error y se puede cobrar de nuevo', async () => {
    sales.registerBehavior = async () => {
      throw new AppError('unavailable', 'Odoo no permite al usuario de integración crear clientes.', CUSTOMER_FORBIDDEN);
    };
    const process = startCheckout.execute(1);
    const before = process.idempotencyKey;

    const outcome = await process.pay(PAYMENT, { kind: 'identified', name: 'Ana Pérez', identification: '1710034065' });

    expect(outcome.kind).toBe('failed');
    expect(process.isUncertain).toBe(false);
    expect(process.idempotencyKey).toBe(before);
    expect(ticket.getSnapshot().locked).toBe(false);
  });

  it('un error de validación conserva la clave (no quedó registrado nada en el servidor)', async () => {
    sales.registerBehavior = async () => {
      throw new AppError('business', 'El monto recibido es menor que el total.', 'PAYMENT.INSUFFICIENT');
    };
    const process = startCheckout.execute(1);
    const before = process.idempotencyKey;

    const outcome = await process.pay({ paymentMethodId: 2, amountTendered: 1 });

    expect(outcome.kind).toBe('failed');
    expect(process.idempotencyKey).toBe(before);
    expect(ticket.getSnapshot().locked).toBe(false);
  });

  it('no permite cancelar un cobro con resultado incierto', async () => {
    sales.registerBehavior = async (key) => registration(key, 'uncertain', 50);
    const process = startCheckout.execute(1);
    await process.pay(PAYMENT);

    expect(process.cancel()).toBe(false);
    expect(ticket.getSnapshot().locked).toBe(true);
  });
});
