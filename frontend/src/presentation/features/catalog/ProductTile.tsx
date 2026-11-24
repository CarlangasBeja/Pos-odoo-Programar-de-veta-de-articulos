import { Plus } from 'lucide-react';
import { memo, useEffect, useRef, useState } from 'react';
import { formatMoney, formatQuantity, type Currency } from '../../../domain/money';
import type { Product } from '../../../domain/product';

interface ProductTileProps {
  readonly product: Product;
  readonly currency: Currency;
  readonly quantityInCart: number;
  readonly onAdd: (product: Product) => void;
}

/** Toda la ficha es el botón de agregar: en un mostrador se toca rápido, sin apuntar a un botón pequeño. */
export const ProductTile = memo(function ProductTile({ product, currency, quantityInCart, onAdd }: ProductTileProps) {
  const [pulse, setPulse] = useState(false);
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(timer.current), []);

  const taxLabel = product.taxes.length === 0 ? 'Sin impuestos' : `Impuestos incluidos (${product.taxes.map((t) => `${t.rate} %`).join(' + ')})`;
  const stock =
    product.availability.status === 'outOfStock'
      ? { tone: 'warning', label: 'Sin existencias' }
      : product.availability.status === 'available' && product.availability.quantity !== null
        ? { tone: 'neutral', label: `${formatQuantity(product.availability.quantity)} disponibles` }
        : null;

  const handleClick = () => {
    onAdd(product);
    setPulse(true);
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setPulse(false), 450);
  };

  return (
    <button
      type="button"
      className={`product-tile${quantityInCart > 0 ? ' product-tile--in-cart' : ''}${pulse ? ' product-tile--added' : ''}`}
      disabled={!product.sellable}
      onClick={handleClick}
      aria-label={`Agregar ${product.name}, ${formatMoney(product.priceWithTaxes, currency)}${quantityInCart > 0 ? `, ${formatQuantity(quantityInCart)} en el ticket` : ''}`}
      aria-describedby={!product.sellable ? `reason-${product.id}` : undefined}
    >
      <span className="product-tile__top">
        <span className="product-tile__code">{product.code ?? 'Sin código'}</span>
        {quantityInCart > 0 && (
          <span className="product-tile__in-cart num" aria-hidden="true">
            {formatQuantity(quantityInCart)} en el ticket
          </span>
        )}
      </span>
      <span className="product-tile__name">{product.name}</span>
      {!product.sellable ? (
        <span className="product-tile__reason" id={`reason-${product.id}`}>
          {product.notSellableReasons[0] ?? 'No disponible para la venta'}
        </span>
      ) : (
        stock && <span className={`badge${stock.tone === 'warning' ? ' badge--warning' : ''}`}>{stock.label}</span>
      )}
      <span className="product-tile__bottom">
        <span className="product-tile__price">
          <span className="num">{formatMoney(product.priceWithTaxes, currency)}</span>
          <span className="product-tile__tax">{taxLabel}</span>
        </span>
        <span className="product-tile__add" aria-hidden="true">
          <Plus size={20} />
          {quantityInCart > 0 && (
            <span className="product-tile__count num" key={quantityInCart}>
              {formatQuantity(quantityInCart)}
            </span>
          )}
        </span>
      </span>
    </button>
  );
});
