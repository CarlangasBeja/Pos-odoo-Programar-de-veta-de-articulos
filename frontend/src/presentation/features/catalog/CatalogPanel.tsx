import { ChevronLeft, ChevronRight, LoaderCircle, PackageSearch, ScanBarcode, Search, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { ProductSort } from '../../../application/ports';
import { formatQuantity } from '../../../domain/money';
import { matchesCodeExactly, type Product, stockConfirmationFor } from '../../../domain/product';
import { Button } from '../../components/Button';
import { Banner, EmptyState } from '../../components/Feedback';
import { usePos } from '../../context/app';
import { useConfirm, useToast } from '../../context/feedback';
import { useServices } from '../../context/services';
import { useDebouncedValue, useMediaQuery, useQuery, useTicket } from '../../hooks/hooks';
import { PLACEHOLDER_CURRENCY } from '../../utils/format';
import { ProductTile } from './ProductTile';

const SORT_OPTIONS: ReadonlyArray<{ value: ProductSort; label: string }> = [
  { value: 'nameAsc', label: 'Nombre (A a Z)' },
  { value: 'nameDesc', label: 'Nombre (Z a A)' },
  { value: 'priceAsc', label: 'Precio: menor a mayor' },
  { value: 'priceDesc', label: 'Precio: mayor a menor' },
  { value: 'codeAsc', label: 'Código' },
];

const SEARCH_DELAY_MS = 250;

/** RF-01 / RF-02: catálogo de Odoo con búsqueda en vivo y filtros. */
export function CatalogPanel() {
  const { searchProducts, getProductCategories, ticket } = useServices();
  const { session } = usePos();
  const toast = useToast();
  const confirm = useConfirm();
  const { cart } = useTicket();
  const compact = useMediaQuery('(max-width: 640px)');
  const searchRef = useRef<HTMLInputElement>(null);

  const [text, setText] = useState('');
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [sort, setSort] = useState<ProductSort>('nameAsc');
  const [onlyInStock, setOnlyInStock] = useState(false);
  const [page, setPage] = useState(1);
  const search = useDebouncedValue(text.trim(), SEARCH_DELAY_MS);
  const pageSize = compact ? 12 : 24;

  // Cualquier cambio de filtro vuelve a la primera página.
  useEffect(() => setPage(1), [search, categoryId, sort, onlyInStock, pageSize]);

  const categories = useQuery((signal) => getProductCategories.execute(signal), []);
  const results = useQuery(
    (signal) => searchProducts.execute({ search, categoryId, onlyInStock, sort, page, pageSize }, signal),
    [search, categoryId, onlyInStock, sort, page, pageSize],
  );

  // "/" enfoca el buscador desde cualquier parte de la pantalla de venta.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const typing = target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable);
      if (event.key === '/' && !typing && !document.querySelector('.modal')) {
        event.preventDefault();
        searchRef.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const inCart = useMemo(() => new Map(cart.lines.map((l) => [l.product.id, l.quantity])), [cart.lines]);
  const currency = session.data?.currency ?? PLACEHOLDER_CURRENCY;
  const filtersActive = text !== '' || categoryId !== null || onlyInStock || sort !== 'nameAsc';

  const add = async (product: Product) => {
    // Sin existencias en Odoo no se bloquea la venta (puede haber unidades en bodega sin registrar), pero se confirma.
    const check = stockConfirmationFor(product, inCart.get(product.id) ?? 0);
    if (check) {
      const ok = await confirm(
        check.reason === 'outOfStock'
          ? {
              title: '¿Agregar un producto sin existencias?',
              message: `Odoo registra 0 unidades de «${product.name}». Agréguelo solo si el producto está físicamente en la tienda o en bodega.`,
              confirmLabel: 'Sí, agregar',
              cancelLabel: 'No agregar',
              tone: 'warning',
            }
          : {
              title: '¿Agregar más unidades de las registradas?',
              message: `Odoo registra ${formatQuantity(check.available)} unidad(es) de «${product.name}» y el ticket ya las tiene todas. Agregue otra solo si hay unidades físicas.`,
              confirmLabel: 'Sí, agregar',
              cancelLabel: 'No agregar',
              tone: 'warning',
            },
      );
      if (!ok) return;
    }
    const change = ticket.add(product);
    if (!change.ok) {
      toast({ tone: 'error', title: 'No se agregó el producto', message: change.error });
    }
  };

  /** Lectores de código de barras: escriben el código y Enter. Solo se agrega con coincidencia exacta. */
  const onSearchEnter = async () => {
    const code = text.trim();
    if (!code) return;
    try {
      const exact = await searchProducts.execute({ search: code, categoryId: null, onlyInStock: false, sort: 'nameAsc', page: 1, pageSize: 10 });
      const match = exact.items.find((p) => matchesCodeExactly(p, code));
      if (match) {
        await add(match);
        setText('');
      } else {
        toast({ tone: 'info', title: 'Seleccione el producto en la lista', message: `Ningún producto tiene exactamente el código «${code}».` });
      }
    } catch {
      // el error de la búsqueda ya se muestra en la lista de resultados
    }
  };

  const clearFilters = () => {
    setText('');
    setCategoryId(null);
    setSort('nameAsc');
    setOnlyInStock(false);
    searchRef.current?.focus();
  };

  const data = results.data;

  return (
    <section className="catalog" aria-labelledby="catalog-title">
      <h2 id="catalog-title" className="visually-hidden">
        Catálogo de productos
      </h2>

      <div className="catalog__toolbar">
        <div className="input-group catalog__search">
          <Search size={20} aria-hidden="true" />
          <input
            ref={searchRef}
            className="input input--search"
            type="search"
            placeholder="Buscar por nombre, código o código de barras"
            aria-label="Buscar productos"
            value={text}
            maxLength={80}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                void onSearchEnter();
              } else if (e.key === 'Escape' && text) {
                setText('');
              }
            }}
          />
          <span className="input-group__action catalog__search-actions">
            {results.refreshing && <LoaderCircle size={18} className="spin muted" aria-label="Buscando" />}
            {text ? (
              <Button variant="ghost" size="sm" iconOnly icon={<X size={18} />} aria-label="Borrar búsqueda" onClick={() => setText('')} />
            ) : (
              <kbd className="kbd" title="Presione / para buscar">
                /
              </kbd>
            )}
          </span>
        </div>

        <div className="catalog__filters">
          <label className="catalog__sort">
            <span className="visually-hidden">Ordenar por</span>
            <select className="select" value={sort} onChange={(e) => setSort(e.target.value as ProductSort)}>
              {SORT_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </label>
          <label className="switch">
            <input type="checkbox" checked={onlyInStock} onChange={(e) => setOnlyInStock(e.target.checked)} />
            <span className="switch__track" aria-hidden="true" />
            <span>Solo con existencias</span>
          </label>
        </div>
      </div>

      <div className="chips" role="group" aria-label="Filtrar por categoría">
        <button type="button" className="chip" aria-pressed={categoryId === null} onClick={() => setCategoryId(null)}>
          Todas
        </button>
        {(categories.data ?? []).map((c) => (
          <button key={c.id} type="button" className="chip" aria-pressed={categoryId === c.id} onClick={() => setCategoryId(c.id)}>
            {c.name}
          </button>
        ))}
      </div>

      <div className="catalog__summary" aria-live="polite">
        {data && (
          <span className="muted">
            {data.totalCount === 0
              ? 'Sin resultados'
              : `${data.totalCount.toLocaleString('es-CO')} producto${data.totalCount === 1 ? '' : 's'}${search ? ` para «${search}»` : ''}`}
          </span>
        )}
        {filtersActive && (
          <Button variant="ghost" size="sm" onClick={clearFilters}>
            Limpiar filtros
          </Button>
        )}
        <span className="catalog__scan-hint muted">
          <ScanBarcode size={16} aria-hidden="true" /> Lector de códigos: escanee y se agrega solo
        </span>
      </div>

      {results.error && (
        <Banner
          tone={results.error.isTransient ? 'warning' : 'danger'}
          title="No se pudo cargar el catálogo"
          actions={
            <Button size="sm" variant="secondary" onClick={results.reload}>
              Reintentar
            </Button>
          }
        >
          <p>{results.error.message}</p>
        </Banner>
      )}

      {results.loading ? (
        <div className="product-grid" aria-busy="true" aria-label="Cargando productos">
          {Array.from({ length: 8 }, (_, i) => (
            <div key={i} className="skeleton product-skeleton" />
          ))}
        </div>
      ) : data && data.items.length === 0 ? (
        <EmptyState
          icon={<PackageSearch size={40} aria-hidden="true" />}
          title={search ? `Ningún producto coincide con «${search}»` : 'No hay productos con estos filtros'}
          action={
            filtersActive ? (
              <Button variant="secondary" onClick={clearFilters}>
                Limpiar filtros
              </Button>
            ) : undefined
          }
        >
          Revise la ortografía o busque por el código del producto. Solo aparecen los productos habilitados para el punto de venta en Odoo.
        </EmptyState>
      ) : (
        <div className={`product-grid${results.refreshing ? ' product-grid--refreshing' : ''}`}>
          {data?.items.map((product) => (
            <ProductTile key={product.id} product={product} currency={currency} quantityInCart={inCart.get(product.id) ?? 0} onAdd={add} />
          ))}
        </div>
      )}

      {data && data.totalPages > 1 && (
        <nav className="pager" aria-label="Páginas del catálogo">
          <Button variant="secondary" size="sm" icon={<ChevronLeft size={16} />} disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Anterior
          </Button>
          <span className="pager__label num">
            Página {data.page} de {data.totalPages}
          </span>
          <Button variant="secondary" size="sm" disabled={page >= data.totalPages} onClick={() => setPage((p) => p + 1)}>
            Siguiente
            <ChevronRight size={16} aria-hidden="true" />
          </Button>
        </nav>
      )}
    </section>
  );
}
