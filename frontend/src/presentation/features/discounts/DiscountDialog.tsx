import { BadgePercent } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { type AppError, toAppError } from '../../../application/errors';
import { type DiscountPolicy, validateDiscount } from '../../../domain/discount';
import { formatMoney } from '../../../domain/money';
import { Button } from '../../components/Button';
import { Banner, EmptyState, FieldMessage } from '../../components/Feedback';
import { Modal } from '../../components/Modal';
import { usePos } from '../../context/app';
import { useToast } from '../../context/feedback';
import { useServices } from '../../context/services';
import { useTicket } from '../../hooks/hooks';

interface DiscountDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
}

/** RF-06 a RF-08: el operador elige una política habilitada; el backend confirma que está autorizada antes de aplicarla. */
export function DiscountDialog({ open, onClose }: DiscountDialogProps) {
  const { previewTicket, ticket } = useServices();
  const { session, policies } = usePos();
  const state = useTicket();
  const toast = useToast();
  const percentId = useId();
  const [code, setCode] = useState<string | null>(null);
  const [percentText, setPercentText] = useState('');
  const [error, setError] = useState<AppError | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (open) {
      setCode(state.discount?.policy.code ?? policies[0]?.code ?? null);
      setPercentText(state.discount?.percentage ? String(state.discount.percentage) : '');
      setError(null);
      setLocalError(null);
    }
    // Solo al abrir: los cambios posteriores los hace el operador en el formulario.
  }, [open]);

  const policy: DiscountPolicy | undefined = policies.find((p) => p.code === code);
  const percentage = percentText.trim() === '' ? null : Number(percentText.replace(',', '.'));
  const currency = session.data?.currency;

  const apply = async () => {
    if (!policy || !session.data) return;
    const selection = { policy, percentage: policy.kind === 'manual' ? percentage : null };
    const validation = validateDiscount(selection);
    setLocalError(validation);
    setError(null);
    if (validation) return;

    setBusy(true);
    try {
      // Verificación en el servidor: precios de Odoo + autorización del rol (RN-04).
      const preview = await previewTicket.execute(session.data.id, { ...state, discount: selection });
      const change = ticket.setDiscount(selection);
      if (!change.ok) {
        setLocalError(change.error);
        return;
      }
      toast({
        tone: 'success',
        title: 'Descuento aplicado',
        message: preview.discount && currency ? `${preview.discount.description}: − ${formatMoney(preview.discount.amount, currency)}` : undefined,
      });
      onClose();
    } catch (reason) {
      setError(toAppError(reason));
    } finally {
      setBusy(false);
    }
  };

  const quickPercents = [5, 10, 15, 20, 30].filter((p) => policy?.maxPercentage == null || p <= policy.maxPercentage);

  return (
    <Modal
      open={open}
      title="Aplicar descuento"
      subtitle="Solo se muestran las políticas autorizadas para su rol."
      onClose={onClose}
      closable={!busy}
      width={520}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Cancelar
          </Button>
          <Button variant="primary" onClick={apply} busy={busy} disabled={!policy}>
            Verificar y aplicar
          </Button>
        </>
      }
    >
      {policies.length === 0 ? (
        <EmptyState icon={<BadgePercent size={36} aria-hidden="true" />} title="No hay descuentos disponibles">
          Su rol no tiene políticas de descuento habilitadas. Pida a un supervisor que registre la venta si corresponde un descuento.
        </EmptyState>
      ) : (
        <div className="discount-form">
          <fieldset className="option-list">
            <legend className="field__label">Política</legend>
            {policies.map((p) => (
              <label key={p.code} className="option">
                <input
                  type="radio"
                  name="discount-policy"
                  value={p.code}
                  checked={code === p.code}
                  onChange={() => {
                    setCode(p.code);
                    setLocalError(null);
                    setError(null);
                  }}
                />
                <span className="option__body">
                  <span className="option__title">{p.name}</span>
                  <span className="option__text">{p.description}</span>
                  {p.kind === 'manual' && p.maxPercentage !== null && <span className="option__meta">Máximo para su rol: {p.maxPercentage} %</span>}
                </span>
              </label>
            ))}
          </fieldset>

          {policy?.kind === 'manual' && (
            <div className="field">
              <label className="field__label" htmlFor={percentId}>
                Porcentaje de descuento
              </label>
              <div className="percent-input">
                <input
                  id={percentId}
                  className="input num"
                  inputMode="decimal"
                  value={percentText}
                  placeholder="0"
                  aria-invalid={localError ? true : undefined}
                  onChange={(e) => {
                    setPercentText(e.target.value);
                    setLocalError(null);
                  }}
                  data-autofocus
                />
                <span aria-hidden="true">%</span>
              </div>
              <div className="chips chips--tight">
                {quickPercents.map((p) => (
                  <button key={p} type="button" className="chip" aria-pressed={percentage === p} onClick={() => setPercentText(String(p))}>
                    {p} %
                  </button>
                ))}
              </div>
            </div>
          )}

          <FieldMessage error={localError} />
          {error && (
            <Banner tone={error.kind === 'forbidden' ? 'danger' : 'warning'} title={error.kind === 'forbidden' ? 'Descuento no autorizado' : 'No se aplicó el descuento'}>
              <p>{error.message}</p>
            </Banner>
          )}
        </div>
      )}
    </Modal>
  );
}
