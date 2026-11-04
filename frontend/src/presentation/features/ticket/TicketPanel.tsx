import { Lock, Percent, ReceiptText, Trash2, X } from 'lucide-react';
import { useMemo } from 'react';
import { cartTotals, itemCount } from '../../../domain/cart';
import { provisionalPercent } from '../../../domain/discount';
import { formatMoney, formatQuantity } from '../../../domain/money';
import { Button } from '../../components/Button';
import { usePos } from '../../context/app';
import { useConfirm, useToast } from '../../context/feedback';
import { useServices } from '../../context/services';
import { useTicket } from '../../hooks/hooks';
import { formatTime, PLACEHOLDER_CURRENCY } from '../../utils/format';
import { TicketLine } from './TicketLine';

interface TicketPanelProps {
  readonly onCheckout: () => void;
  readonly onDiscount: () => void;
  readonly variant?: 'aside' | 'sheet';
}

/** El ticket de la venta en preparación: cantidades editables, descuento y total provisional (RF-03 a RF-05). */
export function TicketPanel({ onCheckout, onDiscount, variant = 'aside' }: TicketPanelProps) {
  const { ticket } = useServices();
  const { session } = usePos();
  const state = useTicket();
  const toast = useToast();
  const confirm = useConfirm();

  const current = session.data;
  const currency = current?.currency ?? PLACEHOLDER_CURRENCY;
  const totals = useMemo(
    () => cartTotals(state.cart, currency, current?.taxRounding ?? 'global', provisionalPercent(state.discount)),
    [state.cart, currency, current?.taxRounding, state.discount],
  );
  const lines = state.cart.lines;
  const count = itemCount(state.cart);

  const blockedReason = !current
    ? session.error
      ? 'No se pudo consultar la sesión POS en Odoo.'
      : 'Consultando la sesión POS…'
    : !current.acceptsSales
      ? (current.issues[0] ?? `La sesión ${current.name} no está abierta en Odoo.`)
      : lines.length === 0
        ? 'Agregue al menos un producto para cobrar.'
        : null;

  const remove = (productId: number) => {
    const removed = ticket.remove(productId);
    if (removed) {
      toast({
        tone: 'info',
        title: `Se quitó «${removed.line.product.name}»`,
        action: { label: 'Deshacer', onClick: () => ticket.undoRemove(removed.line, removed.index) },
        duration: 6000,
      });
    }
  };

  const clear = async () => {
    const ok = await confirm({
      title: '¿Vaciar el ticket?',
      message: `Se quitarán ${lines.length} producto(s)${state.discount ? ' y el descuento aplicado' : ''}. Esta acción no se puede deshacer.`,
      confirmLabel: 'Vaciar ticket',
      tone: 'danger',
    });
    if (ok) {
      ticket.clear();
      toast({ tone: 'success', title: 'Ticket vacío' });
    }
  };

  const removeDiscount = () => {
    const change = ticket.setDiscount(null);
    if (change.ok) toast({ tone: 'info', title: 'Se quitó el descuento' });
  };

  const discount = state.discount;
  const automaticDiscount = discount?.policy.kind === 'automatic';

  return (
    <section className={`ticket ticket--${variant}`} aria-labelledby={`ticket-title-${variant}`}>
      <div className="ticket__paper">
        <header className="ticket__header">
          <div>
            <h2 className="ticket__title" id={`ticket-title-${variant}`}>
              <ReceiptText size={20} aria-hidden="true" /> Ticket
            </h2>
            <p className="ticket__meta num">
              {current?.name ?? 'Sin sesión'} <span aria-hidden="true">/</span> {formatTime(new Date())}
            </p>
          </div>
          {lines.length > 0 && (
            <Button variant="ghost" size="sm" icon={<Trash2 size={16} />} onClick={clear} disabled={state.locked}>
              Vaciar
            </Button>
          )}
        </header>

        {state.locked && (
          <p className="ticket__locked">
            <Lock size={16} aria-hidden="true" /> Cobro en curso: el ticket no se puede editar.
          </p>
        )}

        {lines.length === 0 ? (
          <div className="ticket__empty">
            <p className="ticket__empty-title">El ticket está vacío</p>
            <p>Toque un producto del catálogo o escanee su código para agregarlo.</p>
          </div>
        ) : (
          <ol className="ticket__lines">
            {lines.map((line, index) => (
              <TicketLine
                key={line.product.id}
                line={line}
                amounts={totals.lines[index]}
                currency={currency}
                locked={state.locked}
                onQuantity={(quantity) => ticket.setQuantity(line.product.id, quantity)}
                onRemove={() => remove(line.product.id)}
              />
            ))}
          </ol>
        )}

        {lines.length > 0 && (
          <div className="ticket__discount">
            {discount ? (
              <div className="ticket__discount-applied">
                <Percent size={16} aria-hidden="true" />
                <span>
                  {discount.policy.name}
                  {discount.percentage ? ` (${discount.percentage} %)` : ''}
                  {automaticDiscount && <span className="muted"> se calcula al cobrar</span>}
                </span>
                <Button variant="ghost" size="sm" iconOnly icon={<X size={16} />} aria-label="Quitar descuento" onClick={removeDiscount} disabled={state.locked} />
              </div>
            ) : (
              <Button variant="ghost" size="sm" icon={<Percent size={16} />} onClick={onDiscount} disabled={state.locked}>
                Aplicar descuento
              </Button>
            )}
          </div>
        )}

        <dl className="ticket__totals num">
          <div>
            <dt>Productos</dt>
            <dd>{formatQuantity(count)}</dd>
          </div>
          <div>
            <dt>Subtotal sin impuestos</dt>
            <dd>{formatMoney(totals.untaxed, currency)}</dd>
          </div>
          {totals.discount > 0 && (
            <div className="ticket__total-discount">
              <dt>Descuento</dt>
              <dd>− {formatMoney(totals.discount, currency)}</dd>
            </div>
          )}
          <div>
            <dt>Impuestos</dt>
            <dd>{formatMoney(totals.tax, currency)}</dd>
          </div>
          <div className="ticket__grand">
            <dt>
              Total
              <span className="ticket__provisional">provisional, Odoo lo verifica al cobrar</span>
            </dt>
            <dd>{formatMoney(totals.total, currency)}</dd>
          </div>
        </dl>
      </div>

      <div className="ticket__actions">
        <Button variant="primary" size="lg" block onClick={onCheckout} disabled={blockedReason !== null || state.locked}>
          Cobrar {lines.length > 0 && <span className="num">{formatMoney(totals.total, currency)}</span>}
        </Button>
        {blockedReason && lines.length > 0 && <p className="ticket__blocked">{blockedReason}</p>}
      </div>
    </section>
  );
}
