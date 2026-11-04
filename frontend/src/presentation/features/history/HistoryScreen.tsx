import { ChevronLeft, ChevronRight, LoaderCircle, ReceiptText, Search, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { HistoryStateFilter } from '../../../application/ports';
import { formatMoney } from '../../../domain/money';
import { ORDER_STATE_LABELS } from '../../../domain/sale';
import { Button } from '../../components/Button';
import { Banner, EmptyState } from '../../components/Feedback';
import { usePos } from '../../context/app';
import { useServices } from '../../context/services';
import { useDebouncedValue, useQuery } from '../../hooks/hooks';
import { formatDateTime, PLACEHOLDER_CURRENCY, todayIso } from '../../utils/format';
import { stateBadgeClass } from './orderState';
import { SaleDetailDialog } from './SaleDetailDialog';

const STATE_FILTERS: ReadonlyArray<{ value: HistoryStateFilter; label: string }> = [
  { value: 'all', label: 'Todos los estados' },
  { value: 'paid', label: 'Pagadas' },
  { value: 'posted', label: 'Contabilizadas' },
  { value: 'draft', label: 'Sin pagar' },
  { value: 'cancelled', label: 'Canceladas' },
];

/** RF-13: historial de ventas registradas en Odoo, con búsqueda en vivo y filtros. */
export function HistoryScreen() {
  const { searchSalesHistory } = useServices();
  const { session } = usePos();
  const [text, setText] = useState('');
  const [state, setState] = useState<HistoryStateFilter>('all');
  const [from, setFrom] = useState<string>('');
  const [to, setTo] = useState<string>('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<number | null>(null);
  const search = useDebouncedValue(text.trim(), 300);
  const currency = session.data?.currency ?? PLACEHOLDER_CURRENCY;

  useEffect(() => setPage(1), [search, state, from, to]);

  const rangeError = from && to && from > to ? 'La fecha inicial no puede ser posterior a la final.' : null;
  const history = useQuery(
    (signal) => searchSalesHistory.execute({ search, state, from: from || null, to: to || null, page, pageSize: 15 }, signal),
    [search, state, from, to, page],
    rangeError === null,
  );

  const filtersActive = text !== '' || state !== 'all' || from !== '' || to !== '';
  const clear = () => {
    setText('');
    setState('all');
    setFrom('');
    setTo('');
  };
  const data = history.data;

  return (
    <section className="history" aria-labelledby="history-title">
      <div className="history__head">
        <div>
          <h1 id="history-title" className="history__title">
            Historial de ventas
          </h1>
          <p className="muted">Ventas registradas en Odoo para este punto de venta.</p>
        </div>
        <Button
          variant="secondary"
          size="sm"
          onClick={() => {
            const today = todayIso();
            setFrom(today);
            setTo(today);
          }}
        >
          Solo hoy
        </Button>
      </div>

      <div className="history__filters">
        <div className="input-group history__search">
          <Search size={20} aria-hidden="true" />
          <input
            className="input"
            type="search"
            placeholder="Buscar por orden, recibo o producto"
            aria-label="Buscar ventas"
            value={text}
            maxLength={60}
            onChange={(e) => setText(e.target.value)}
          />
          <span className="input-group__action catalog__search-actions">
            {history.refreshing && <LoaderCircle size={18} className="spin muted" aria-label="Buscando" />}
            {text && <Button variant="ghost" size="sm" iconOnly icon={<X size={18} />} aria-label="Borrar búsqueda" onClick={() => setText('')} />}
          </span>
        </div>
        <label className="field history__field">
          <span className="field__label">Estado</span>
          <select className="select" value={state} onChange={(e) => setState(e.target.value as HistoryStateFilter)}>
            {STATE_FILTERS.map((f) => (
              <option key={f.value} value={f.value}>
                {f.label}
              </option>
            ))}
          </select>
        </label>
        <label className="field history__field">
          <span className="field__label">Desde</span>
          <input className="input" type="date" value={from} max={to || undefined} onChange={(e) => setFrom(e.target.value)} aria-invalid={rangeError ? true : undefined} />
        </label>
        <label className="field history__field">
          <span className="field__label">Hasta</span>
          <input className="input" type="date" value={to} min={from || undefined} onChange={(e) => setTo(e.target.value)} aria-invalid={rangeError ? true : undefined} />
        </label>
        {filtersActive && (
          <Button variant="ghost" onClick={clear} className="history__clear">
            Limpiar filtros
          </Button>
        )}
      </div>

      {rangeError && <Banner tone="warning" title="Revise el rango de fechas"><p>{rangeError}</p></Banner>}
      {history.error && (
        <Banner
          tone={history.error.isTransient ? 'warning' : 'danger'}
          title="No se pudo cargar el historial"
          actions={
            <Button size="sm" variant="secondary" onClick={history.reload}>
              Reintentar
            </Button>
          }
        >
          <p>{history.error.message}</p>
        </Banner>
      )}

      {history.loading ? (
        <div className="history__table-skeleton" aria-busy="true">
          {Array.from({ length: 6 }, (_, i) => (
            <div key={i} className="skeleton" style={{ height: 48 }} />
          ))}
        </div>
      ) : data && data.items.length === 0 ? (
        <EmptyState
          icon={<ReceiptText size={40} aria-hidden="true" />}
          title={filtersActive ? 'Ninguna venta coincide con los filtros' : 'Todavía no hay ventas registradas'}
          action={filtersActive ? <Button variant="secondary" onClick={clear}>Limpiar filtros</Button> : undefined}
        >
          {filtersActive ? 'Pruebe con otra búsqueda o amplíe el rango de fechas.' : 'Las ventas que cobre en la pantalla Vender aparecerán aquí.'}
        </EmptyState>
      ) : (
        data && (
          <div className={`history__table-wrap${history.refreshing ? ' is-refreshing' : ''}`}>
            <table className="history__table">
              <caption className="visually-hidden">Ventas registradas</caption>
              <thead>
                <tr>
                  <th scope="col">Fecha</th>
                  <th scope="col">Orden</th>
                  <th scope="col">Recibo</th>
                  <th scope="col">Estado</th>
                  <th scope="col" className="history__amount">
                    Total
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((order) => (
                  <tr key={order.id}>
                    <td data-label="Fecha">{formatDateTime(order.date)}</td>
                    <td data-label="Orden">
                      <button type="button" className="link-button" onClick={() => setSelected(order.id)}>
                        {order.name}
                      </button>
                    </td>
                    <td data-label="Recibo" className="num">
                      {order.posReference ?? '—'}
                    </td>
                    <td data-label="Estado">
                      <span className={stateBadgeClass(order.state)}>{ORDER_STATE_LABELS[order.state]}</span>
                    </td>
                    <td data-label="Total" className="history__amount num">
                      {formatMoney(order.amountTotal, currency)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      )}

      {data && data.totalPages > 1 && (
        <nav className="pager" aria-label="Páginas del historial">
          <Button variant="secondary" size="sm" icon={<ChevronLeft size={16} />} disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Anterior
          </Button>
          <span className="pager__label num">
            Página {data.page} de {data.totalPages} ({data.totalCount.toLocaleString('es-CO')} ventas)
          </span>
          <Button variant="secondary" size="sm" disabled={page >= data.totalPages} onClick={() => setPage((p) => p + 1)}>
            Siguiente
            <ChevronRight size={16} aria-hidden="true" />
          </Button>
        </nav>
      )}

      <SaleDetailDialog orderId={selected} onClose={() => setSelected(null)} />
    </section>
  );
}
