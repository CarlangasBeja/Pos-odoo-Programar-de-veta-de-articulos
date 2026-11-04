import { Minus, Plus, Trash2 } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import type { TicketChange } from '../../../application/cartStore';
import { type CartLine, UNUSUAL_QUANTITY, validateQuantity } from '../../../domain/cart';
import { type Currency, formatMoney, formatQuantity } from '../../../domain/money';
import type { LineAmounts } from '../../../domain/tax';
import { Button } from '../../components/Button';
import { FieldMessage } from '../../components/Feedback';

interface TicketLineProps {
  readonly line: CartLine;
  readonly amounts: LineAmounts | undefined;
  readonly currency: Currency;
  readonly locked: boolean;
  readonly onQuantity: (quantity: number) => TicketChange;
  readonly onRemove: () => void;
}

/** RF-04: modificar la cantidad (con validación inmediata) o quitar el producto. */
export function TicketLine({ line, amounts, currency, locked, onQuantity, onRemove }: TicketLineProps) {
  const inputId = useId();
  const [draft, setDraft] = useState(formatQuantity(line.quantity));
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setDraft(formatQuantity(line.quantity));
    setError(null);
  }, [line.quantity]);

  const commit = (value: number) => {
    const validation = validateQuantity(value);
    if (validation) {
      setError(validation);
      return;
    }
    const change = onQuantity(value);
    setError(change.ok ? null : change.error);
  };

  const commitDraft = () => {
    const parsed = Number(draft.replace(/\./g, '').replace(',', '.'));
    if (parsed === line.quantity) {
      setDraft(formatQuantity(line.quantity));
      setError(null);
      return;
    }
    commit(parsed);
  };

  const warning = !error && line.quantity >= UNUSUAL_QUANTITY ? `Cantidad poco común (${formatQuantity(line.quantity)}). Verifique antes de cobrar.` : null;

  return (
    <li className="ticket-line">
      <div className="ticket-line__main">
        <span className="ticket-line__name">{line.product.name}</span>
        <span className="ticket-line__unit num">
          {formatMoney(line.product.priceWithTaxes, currency)} c/u
        </span>
      </div>
      <div className="ticket-line__controls">
        <div className="stepper" role="group" aria-label={`Cantidad de ${line.product.name}`}>
          <Button
            variant="ghost"
            size="sm"
            iconOnly
            icon={<Minus size={16} />}
            aria-label="Restar uno"
            disabled={locked || line.quantity <= 1}
            onClick={() => commit(line.quantity - 1)}
          />
          <label htmlFor={inputId} className="visually-hidden">
            Cantidad
          </label>
          <input
            id={inputId}
            className="stepper__input num"
            inputMode="decimal"
            value={draft}
            disabled={locked}
            aria-invalid={error ? true : undefined}
            onChange={(e) => setDraft(e.target.value)}
            onBlur={commitDraft}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                commitDraft();
              }
            }}
          />
          <Button variant="ghost" size="sm" iconOnly icon={<Plus size={16} />} aria-label="Sumar uno" disabled={locked} onClick={() => commit(line.quantity + 1)} />
        </div>
        <span className="ticket-line__total num">{amounts ? formatMoney(amounts.total, currency) : '—'}</span>
        <Button variant="ghost" size="sm" iconOnly icon={<Trash2 size={16} />} aria-label={`Quitar ${line.product.name}`} disabled={locked} onClick={onRemove} />
      </div>
      <FieldMessage error={error} warning={warning} />
    </li>
  );
}
