import { Banknote, CircleCheckBig, CreditCard, RefreshCw, SearchCheck, TriangleAlert, Wallet } from 'lucide-react';
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { type AppError, toAppError } from '../../../application/errors';
import type { CheckoutOutcome, CheckoutProcess } from '../../../application/usecases/checkout';
import { formatMoney, formatQuantity } from '../../../domain/money';
import { type PaymentMethod, quickCashAmounts, type SalePreview, type SaleRegistration, validatePayment } from '../../../domain/sale';
import { Button } from '../../components/Button';
import { Banner, FieldMessage } from '../../components/Feedback';
import { Modal } from '../../components/Modal';
import { usePos } from '../../context/app';
import { useToast } from '../../context/feedback';
import { useServices } from '../../context/services';
import { parseAmount } from '../../utils/format';

interface CheckoutDialogProps {
  readonly open: boolean;
  readonly provisionalTotal: number;
  readonly onClose: () => void;
}

type Step =
  | { readonly kind: 'verifying' }
  | { readonly kind: 'verifyFailed'; readonly error: AppError }
  | { readonly kind: 'payment'; readonly preview: SalePreview }
  | { readonly kind: 'processing'; readonly preview: SalePreview }
  | { readonly kind: 'registered'; readonly sale: SaleRegistration }
  | { readonly kind: 'uncertain'; readonly preview: SalePreview | null; readonly sale: SaleRegistration | null; readonly message: string; readonly checking: boolean }
  | { readonly kind: 'failed'; readonly error: AppError };

const METHOD_ICONS = { cash: <Banknote size={18} aria-hidden="true" />, bank: <CreditCard size={18} aria-hidden="true" />, other: <Wallet size={18} aria-hidden="true" /> };

/**
 * Cobro (RF-09 a RF-12, RF-17). Pasos: verificar total con Odoo → registrar pago → resultado real.
 * No se puede cerrar mientras se registra ni mientras el resultado es incierto.
 */
export function CheckoutDialog({ open, provisionalTotal, onClose }: CheckoutDialogProps) {
  const { startCheckout } = useServices();
  const { session } = usePos();
  const toast = useToast();
  const amountId = useId();
  const process = useRef<CheckoutProcess | null>(null);
  const [step, setStep] = useState<Step>({ kind: 'verifying' });
  const [methodId, setMethodId] = useState<number | null>(null);
  const [amountText, setAmountText] = useState('');
  const [touched, setTouched] = useState(false);

  const current = session.data;
  const currency = current?.currency;
  const methods = useMemo(() => current?.paymentMethods ?? [], [current?.paymentMethods]);

  const verify = useCallback(async () => {
    if (!process.current) return;
    setStep({ kind: 'verifying' });
    try {
      const preview = await process.current.verify();
      setStep({ kind: 'payment', preview });
      setAmountText('');
      setTouched(false);
    } catch (reason) {
      setStep({ kind: 'verifyFailed', error: toAppError(reason) });
    }
  }, []);

  // Cada apertura del cobro es un proceso nuevo con su propia clave de idempotencia.
  useEffect(() => {
    if (open && current) {
      process.current = startCheckout.execute(current.id);
      setMethodId(methods.find((m) => m.supported && m.type === 'cash')?.id ?? methods.find((m) => m.supported)?.id ?? null);
      void verify();
    }
    // Se inicia solo al abrir; la sesión vigente se toma en ese momento.
  }, [open]);

  const method: PaymentMethod | undefined = methods.find((m) => m.id === methodId);
  const preview = step.kind === 'payment' || step.kind === 'processing' ? step.preview : null;
  const total = preview?.total ?? 0;
  const tendered = method && !method.allowsChange ? total : parseAmount(amountText, currency ?? { code: '', symbol: '', symbolPosition: 'before', decimalPlaces: 0, rounding: 1 });
  const paymentError = preview ? validatePayment(method, tendered, total) : null;
  const change = Number.isFinite(tendered) && tendered > total ? tendered - total : 0;
  const largeChange = change > 0 && change >= total && total > 0;

  const applyOutcome = (outcome: CheckoutOutcome, previewForUncertain: SalePreview | null) => {
    switch (outcome.kind) {
      case 'registered':
        setStep({ kind: 'registered', sale: outcome.sale });
        toast({ tone: 'success', title: 'Venta registrada en Odoo', message: outcome.sale.orderName ?? undefined });
        break;
      case 'uncertain':
        setStep({ kind: 'uncertain', preview: previewForUncertain, sale: outcome.sale, message: outcome.message, checking: false });
        break;
      case 'failed':
        setStep({ kind: 'failed', error: outcome.error });
        break;
    }
  };

  const pay = async () => {
    setTouched(true);
    if (!preview || !method || paymentError || !process.current) return;
    setStep({ kind: 'processing', preview });
    const outcome = await process.current.pay({ paymentMethodId: method.id, amountTendered: tendered });
    applyOutcome(outcome, preview);
  };

  const checkStatus = async () => {
    if (step.kind !== 'uncertain' || !process.current) return;
    setStep({ ...step, checking: true });
    const outcome = await process.current.checkStatus();
    applyOutcome(outcome, step.preview);
  };

  const retry = async () => {
    if (step.kind !== 'uncertain' || !process.current || !step.preview || !method) return;
    const previewToRetry = step.preview;
    setStep({ kind: 'processing', preview: previewToRetry });
    const outcome = await process.current.pay({ paymentMethodId: method.id, amountTendered: tendered });
    applyOutcome(outcome, previewToRetry);
  };

  const close = () => {
    if (step.kind === 'processing' || step.kind === 'uncertain') return;
    process.current?.cancel();
    onClose();
  };

  const closable = step.kind !== 'processing' && step.kind !== 'uncertain';
  const title =
    step.kind === 'registered' ? 'Venta registrada' : step.kind === 'uncertain' ? 'Resultado sin confirmar' : step.kind === 'failed' ? 'La venta no se registró' : 'Cobrar';

  const footer = (() => {
    switch (step.kind) {
      case 'verifying':
        return (
          <Button variant="secondary" onClick={close}>
            Volver al ticket
          </Button>
        );
      case 'verifyFailed':
        return (
          <>
            <Button variant="secondary" onClick={close}>
              Volver al ticket
            </Button>
            <Button variant="primary" icon={<RefreshCw size={18} />} onClick={verify}>
              Verificar de nuevo
            </Button>
          </>
        );
      case 'payment':
      case 'processing':
        return (
          <>
            <Button variant="secondary" onClick={close} disabled={step.kind === 'processing'}>
              Volver al ticket
            </Button>
            <Button variant="primary" size="lg" onClick={pay} busy={step.kind === 'processing'} disabled={touched && paymentError !== null}>
              Confirmar cobro {currency && <span className="num">{formatMoney(total, currency)}</span>}
            </Button>
          </>
        );
      case 'uncertain':
        return (
          <>
            <Button variant="secondary" icon={<SearchCheck size={18} />} onClick={checkStatus} busy={step.checking}>
              Verificar estado
            </Button>
            <Button variant="warning" icon={<RefreshCw size={18} />} onClick={retry} disabled={step.checking || !step.preview}>
              Reintentar sin duplicar
            </Button>
          </>
        );
      case 'registered':
        return (
          <Button variant="primary" size="lg" onClick={onClose} data-autofocus>
            Nueva venta
          </Button>
        );
      case 'failed':
        return (
          <Button variant="primary" onClick={close} data-autofocus>
            Volver al ticket
          </Button>
        );
    }
  })();

  return (
    <Modal open={open} title={title} onClose={close} closable={closable} width={760} footer={footer}>
      {!currency ? null : step.kind === 'verifying' ? (
        <div className="checkout__loading" role="status">
          <div className="skeleton" style={{ height: 22, width: '60%' }} />
          <div className="skeleton" style={{ height: 120 }} />
          <p className="muted">Verificando precios, impuestos y descuento con Odoo…</p>
        </div>
      ) : step.kind === 'verifyFailed' ? (
        <Banner tone={step.error.isTransient ? 'warning' : 'danger'} title="No se pudo verificar la venta">
          <p>{step.error.message}</p>
          {step.error.details.length > 1 && (
            <ul>
              {step.error.details.map((d, i) => (
                <li key={i}>{d.message}</li>
              ))}
            </ul>
          )}
        </Banner>
      ) : step.kind === 'payment' || step.kind === 'processing' ? (
        <div className="checkout">
          <section className="checkout__summary" aria-label="Resumen verificado">
            <h3 className="checkout__heading">Resumen verificado</h3>
            <ul className="checkout__lines">
              {step.preview.lines.map((l) => (
                <li key={l.productId}>
                  <span>
                    <span className="num">{formatQuantity(l.quantity)} ×</span> {l.productName}
                    {l.discountPercent > 0 && <span className="badge badge--info">−{l.discountPercent} %</span>}
                  </span>
                  <span className="num">{formatMoney(l.total, currency)}</span>
                </li>
              ))}
            </ul>
            <dl className="checkout__totals num">
              {step.preview.discount && (
                <div className="checkout__discount">
                  <dt>{step.preview.discount.description} (ya aplicado)</dt>
                  <dd>− {formatMoney(step.preview.discount.amount, currency)}</dd>
                </div>
              )}
              <div>
                <dt>Subtotal sin impuestos</dt>
                <dd>{formatMoney(step.preview.untaxed, currency)}</dd>
              </div>
              {step.preview.taxes.map((t) => (
                <div key={t.taxId}>
                  <dt>{t.name}</dt>
                  <dd>{formatMoney(t.amount, currency)}</dd>
                </div>
              ))}
              <div className="checkout__grand">
                <dt>Total a cobrar</dt>
                <dd>{formatMoney(step.preview.total, currency)}</dd>
              </div>
            </dl>
            {Math.abs(step.preview.total - provisionalTotal) >= currency.rounding && (
              <Banner tone="info" title="El total verificado cambió">
                <p>
                  El ticket mostraba {formatMoney(provisionalTotal, currency)}. Odoo calcula {formatMoney(step.preview.total, currency)} por el descuento
                  automático o el redondeo. Se cobrará el total verificado.
                </p>
              </Banner>
            )}
          </section>

          <section className="checkout__payment" aria-label="Pago">
            <h3 className="checkout__heading">Método de pago</h3>
            <div className="method-list" role="radiogroup" aria-label="Método de pago">
              {methods.map((m) => (
                <button
                  key={m.id}
                  type="button"
                  role="radio"
                  className="method"
                  aria-checked={methodId === m.id}
                  disabled={!m.supported || step.kind === 'processing'}
                  title={m.supported ? undefined : 'Requiere una terminal o un cliente: no disponible en este POS'}
                  onClick={() => {
                    setMethodId(m.id);
                    setTouched(false);
                  }}
                >
                  {METHOD_ICONS[m.type]}
                  <span>{m.name}</span>
                </button>
              ))}
            </div>

            {method?.allowsChange ? (
              <div className="field">
                <label className="field__label" htmlFor={amountId}>
                  Monto recibido
                </label>
                <div className="amount-input">
                  <span aria-hidden="true">{currency.symbol}</span>
                  <input
                    id={amountId}
                    className="input num"
                    inputMode="decimal"
                    autoComplete="off"
                    value={amountText}
                    disabled={step.kind === 'processing'}
                    aria-invalid={touched && paymentError ? true : undefined}
                    aria-describedby={`${amountId}-msg`}
                    onChange={(e) => setAmountText(e.target.value)}
                    onBlur={() => setTouched(true)}
                    onKeyDown={(e) => e.key === 'Enter' && void pay()}
                    data-autofocus
                  />
                </div>
                <div className="chips chips--tight" aria-label="Montos rápidos">
                  {quickCashAmounts(total, currency).map((amount) => (
                    <button
                      key={amount}
                      type="button"
                      className="chip num"
                      disabled={step.kind === 'processing'}
                      onClick={() => {
                        setAmountText(String(amount).replace('.', ','));
                        setTouched(true);
                      }}
                    >
                      {amount === total ? 'Exacto' : formatMoney(amount, currency)}
                    </button>
                  ))}
                </div>
                <FieldMessage
                  id={`${amountId}-msg`}
                  error={touched ? paymentError : null}
                  warning={largeChange ? 'El cambio es mayor que el total. Confirme el monto recibido antes de cobrar.' : null}
                />
              </div>
            ) : (
              method && (
                <Banner tone="info" title={`Cobro con ${method.name}`}>
                  <p>Se registrará el monto exacto de {formatMoney(total, currency)}. Confirme primero el pago en el datáfono.</p>
                </Banner>
              )
            )}

            <div className={`change${change > 0 ? ' change--due' : ''}`} aria-live="polite">
              <span>Cambio a entregar</span>
              <strong className="num">{formatMoney(change, currency)}</strong>
            </div>

            {step.kind === 'processing' && (
              <Banner tone="info" title="Registrando la venta en Odoo">
                <p>No cierre esta ventana ni recargue la página.</p>
              </Banner>
            )}
          </section>
        </div>
      ) : step.kind === 'registered' ? (
        <div className="result result--success">
          <CircleCheckBig size={56} aria-hidden="true" />
          <p className="result__title">Venta registrada y pagada en Odoo</p>
          <dl className="result__facts num">
            <div>
              <dt>Orden</dt>
              <dd>{step.sale.orderName}</dd>
            </div>
            <div>
              <dt>Recibo</dt>
              <dd>{step.sale.posReference ?? '—'}</dd>
            </div>
            <div>
              <dt>Total</dt>
              <dd>{formatMoney(step.sale.total, currency)}</dd>
            </div>
            <div>
              <dt>{step.sale.paymentMethodName}</dt>
              <dd>{formatMoney(step.sale.amountTendered, currency)}</dd>
            </div>
          </dl>
          {step.sale.change > 0 && (
            <p className="result__change">
              Entregue <strong className="num">{formatMoney(step.sale.change, currency)}</strong> de cambio
            </p>
          )}
          {step.sale.replayed && <p className="muted">Esta venta ya estaba registrada; no se creó una segunda orden.</p>}
        </div>
      ) : step.kind === 'uncertain' ? (
        <div className="result result--warning">
          <TriangleAlert size={56} aria-hidden="true" />
          <p className="result__title">No sabemos todavía si Odoo registró la venta</p>
          <p className="result__text">{step.message}</p>
          <ul className="result__steps">
            <li>No entregue mercancía como pagada ni vuelva a cobrar en otro equipo.</li>
            <li>Use «Verificar estado» para consultar a Odoo sin reenviar la venta.</li>
            <li>«Reintentar sin duplicar» reenvía la misma venta; Odoo no crea una segunda orden.</li>
          </ul>
        </div>
      ) : (
        <div className="result result--danger">
          <Banner tone="danger" title="La venta no se registró en Odoo">
            <p>{step.error.message}</p>
            {step.error.details.length > 1 && (
              <ul>
                {step.error.details.map((d, i) => (
                  <li key={i}>{d.message}</li>
                ))}
              </ul>
            )}
          </Banner>
          <p className="muted">El ticket sigue disponible para corregirlo y cobrar de nuevo.</p>
        </div>
      )}
    </Modal>
  );
}
