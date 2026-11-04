import { ReceiptText } from 'lucide-react';
import { useMemo, useState } from 'react';
import { cartTotals, itemCount } from '../../domain/cart';
import { provisionalPercent } from '../../domain/discount';
import { formatMoney } from '../../domain/money';
import { Button } from '../components/Button';
import { Banner } from '../components/Feedback';
import { Modal } from '../components/Modal';
import { usePos } from '../context/app';
import { useLeaveWarning, useMediaQuery, useTicket } from '../hooks/hooks';
import { PLACEHOLDER_CURRENCY } from '../utils/format';
import { CatalogPanel } from './catalog/CatalogPanel';
import { CheckoutDialog } from './checkout/CheckoutDialog';
import { DiscountDialog } from './discounts/DiscountDialog';
import { TicketPanel } from './ticket/TicketPanel';

/** Pantalla de venta: catálogo + ticket. En pantallas pequeñas el ticket se abre como hoja inferior. */
export function SellScreen() {
  const { session } = usePos();
  const ticket = useTicket();
  const narrow = useMediaQuery('(max-width: 900px)');
  const [checkoutOpen, setCheckoutOpen] = useState(false);
  const [discountOpen, setDiscountOpen] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);

  useLeaveWarning(ticket.cart.lines.length > 0 || ticket.locked);

  const current = session.data;
  const currency = current?.currency ?? PLACEHOLDER_CURRENCY;
  const totals = useMemo(
    () => cartTotals(ticket.cart, currency, current?.taxRounding ?? 'global', provisionalPercent(ticket.discount)),
    [ticket.cart, ticket.discount, currency, current?.taxRounding],
  );

  const openCheckout = () => {
    setSheetOpen(false);
    setCheckoutOpen(true);
  };
  const openDiscount = () => {
    setSheetOpen(false);
    setDiscountOpen(true);
  };

  const sessionBanner = session.error ? (
    <Banner tone="danger" title="No se pudo consultar la sesión POS" actions={<Button size="sm" onClick={session.reload}>Reintentar</Button>}>
      <p>{session.error.message}</p>
    </Banner>
  ) : current && !current.acceptsSales ? (
    <Banner tone="warning" title={`No se puede vender: sesión ${current.name} ${current.stateLabel}`} actions={<Button size="sm" onClick={session.reload}>Volver a consultar</Button>}>
      {current.issues.length > 0 ? (
        <ul>
          {current.issues.map((issue) => (
            <li key={issue}>{issue}</li>
          ))}
        </ul>
      ) : (
        <p>Abra la sesión del punto de venta en Odoo y vuelva a consultar.</p>
      )}
    </Banner>
  ) : null;

  const ticketPanel = (variant: 'aside' | 'sheet') => <TicketPanel variant={variant} onCheckout={openCheckout} onDiscount={openDiscount} />;

  return (
    <div className="sell">
      <div className="sell__catalog">
        {sessionBanner}
        <CatalogPanel />
      </div>

      {!narrow && <aside className="sell__ticket">{ticketPanel('aside')}</aside>}

      {narrow && (
        <>
          <div className="ticket-bar">
            <button type="button" className="ticket-bar__summary" onClick={() => setSheetOpen(true)}>
              <ReceiptText size={20} aria-hidden="true" />
              <span>
                Ticket <span className="num">({itemCount(ticket.cart)})</span>
              </span>
              <strong className="num">{formatMoney(totals.total, currency)}</strong>
            </button>
            <Button variant="primary" size="lg" onClick={openCheckout} disabled={ticket.cart.lines.length === 0 || !current?.acceptsSales || ticket.locked}>
              Cobrar
            </Button>
          </div>
          <Modal open={sheetOpen} title="Ticket" onClose={() => setSheetOpen(false)} width={520}>
            {ticketPanel('sheet')}
          </Modal>
        </>
      )}

      <DiscountDialog open={discountOpen} onClose={() => setDiscountOpen(false)} />
      <CheckoutDialog open={checkoutOpen} provisionalTotal={totals.total} onClose={() => setCheckoutOpen(false)} />
    </div>
  );
}
