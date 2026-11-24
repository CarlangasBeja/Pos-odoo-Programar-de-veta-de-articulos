import { customerLabel } from '../../../domain/customer';
import { formatMoney, formatQuantity } from '../../../domain/money';
import { ORDER_STATE_LABELS } from '../../../domain/sale';
import { Button } from '../../components/Button';
import { Banner } from '../../components/Feedback';
import { Modal } from '../../components/Modal';
import { usePos } from '../../context/app';
import { useServices } from '../../context/services';
import { useQuery } from '../../hooks/hooks';
import { formatDateTime, PLACEHOLDER_CURRENCY } from '../../utils/format';
import { ReceiptPdfButtons } from '../receipt/ReceiptPdfButtons';
import { stateBadgeClass } from './orderState';

/** RF-14: detalle de la venta tal como quedó en Odoo (líneas, pagos y cambio). */
export function SaleDetailDialog({ orderId, onClose }: { readonly orderId: number | null; readonly onClose: () => void }) {
  const { getSaleDetail } = useServices();
  const { session } = usePos();
  const currency = session.data?.currency ?? PLACEHOLDER_CURRENCY;
  const detail = useQuery((signal) => getSaleDetail.execute(orderId ?? 0, signal), [orderId], orderId !== null);
  const order = detail.data && detail.data.id === orderId ? detail.data : undefined;

  return (
    <Modal
      open={orderId !== null}
      title={order?.name ?? 'Detalle de la venta'}
      subtitle={order ? formatDateTime(order.date) : undefined}
      onClose={onClose}
      width={640}
      footer={
        <>
          {order && <ReceiptPdfButtons orderId={order.id} />}
          <Button variant="primary" onClick={onClose}>
            Cerrar
          </Button>
        </>
      }
    >
      {detail.error ? (
        <Banner tone="danger" title="No se pudo cargar la venta" actions={<Button size="sm" onClick={detail.reload}>Reintentar</Button>}>
          <p>{detail.error.message}</p>
        </Banner>
      ) : !order ? (
        <div className="checkout__loading" role="status" aria-label="Cargando venta">
          <div className="skeleton" style={{ height: 20, width: '50%' }} />
          <div className="skeleton" style={{ height: 140 }} />
        </div>
      ) : (
        <div className="sale-detail">
          <dl className="sale-detail__facts">
            <div>
              <dt>Estado</dt>
              <dd>
                <span className={stateBadgeClass(order.state)}>{ORDER_STATE_LABELS[order.state]}</span>
              </dd>
            </div>
            <div>
              <dt>Recibo</dt>
              <dd className="num">{order.posReference ?? '—'}</dd>
            </div>
            <div>
              <dt>Sesión</dt>
              <dd>{order.sessionName}</dd>
            </div>
            <div>
              <dt>Cliente</dt>
              <dd>{customerLabel(order.customer)}</dd>
            </div>
          </dl>

          <table className="sale-detail__lines">
            <thead>
              <tr>
                <th scope="col">Producto</th>
                <th scope="col" className="num-col">
                  Cant.
                </th>
                <th scope="col" className="num-col">
                  Precio
                </th>
                <th scope="col" className="num-col">
                  Total
                </th>
              </tr>
            </thead>
            <tbody>
              {order.lines.map((line, index) => (
                <tr key={`${line.productId}-${index}`}>
                  <td>
                    {line.productName}
                    {line.discountPercent > 0 && <span className="badge badge--info">−{line.discountPercent} %</span>}
                  </td>
                  <td className="num num-col">{formatQuantity(line.quantity)}</td>
                  <td className="num num-col">{formatMoney(line.unitPrice, currency)}</td>
                  <td className="num num-col">{formatMoney(line.total, currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <dl className="checkout__totals num">
            <div>
              <dt>Impuestos</dt>
              <dd>{formatMoney(order.amountTax, currency)}</dd>
            </div>
            <div className="checkout__grand">
              <dt>Total</dt>
              <dd>{formatMoney(order.amountTotal, currency)}</dd>
            </div>
          </dl>

          <h3 className="checkout__heading">Pagos</h3>
          <ul className="sale-detail__payments">
            {order.payments.map((payment, index) => (
              <li key={index}>
                <span>{payment.isChange ? `Cambio entregado (${payment.methodName})` : payment.methodName}</span>
                <span className="num">{formatMoney(payment.amount, currency)}</span>
              </li>
            ))}
          </ul>
          {order.note && <p className="muted sale-detail__note">{order.note}</p>}
        </div>
      )}
    </Modal>
  );
}
