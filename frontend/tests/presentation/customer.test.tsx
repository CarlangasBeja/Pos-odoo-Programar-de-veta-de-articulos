import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { usePos } from '../../src/presentation/context/app';
import { CheckoutDialog } from '../../src/presentation/features/checkout/CheckoutDialog';
import { ProductTile } from '../../src/presentation/features/catalog/ProductTile';
import { cafe, FakeSales } from '../fakes';
import { renderInApp, testServices } from './render';

const COP = { code: 'COP', symbol: '$', symbolPosition: 'before', decimalPlaces: 2, rounding: 0.01 } as const;

describe('Cantidad en el producto', () => {
  it('al estar en el ticket, el producto muestra cuántas unidades lleva', () => {
    const { rerender } = render(<ProductTile product={cafe} currency={COP} quantityInCart={0} onAdd={vi.fn()} />);
    expect(screen.queryByText(/en el ticket/)).not.toBeInTheDocument();

    rerender(<ProductTile product={cafe} currency={COP} quantityInCart={3} onAdd={vi.fn()} />);

    expect(screen.getByText('3 en el ticket')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /3 en el ticket/ })).toHaveClass('product-tile--in-cart');
  });
});

/** Como en la app: el cobro se abre cuando la sesión POS ya está cargada. */
function CheckoutWhenSessionReady() {
  const { session } = usePos();
  return <CheckoutDialog open={Boolean(session.data)} provisionalTotal={29.75} onClose={vi.fn()} />;
}

describe('Cliente en el cobro', () => {
  async function openCheckout(sales: FakeSales) {
    const services = testServices({ sales });
    services.ticket.add(cafe, 2);
    renderInApp(<CheckoutWhenSessionReady />, services);
    await screen.findByRole('radio', { name: 'Consumidor final' });
  }

  it('por omisión es consumidor final y la venta va sin datos del cliente', async () => {
    const sales = new FakeSales();
    await openCheckout(sales);

    expect(screen.getByRole('radio', { name: 'Consumidor final' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Exacto' }));
    await userEvent.click(screen.getByRole('button', { name: /^Confirmar cobro/ }));

    await waitFor(() => expect(sales.registerCalls).toHaveLength(1));
    expect(sales.registerCalls[0]!.customer).toEqual({ kind: 'finalConsumer' });
    expect(await screen.findByText('Consumidor final', { selector: 'dd' })).toBeInTheDocument();
  });

  it('con nombre y cédula válida, la venta lleva el cliente', async () => {
    const sales = new FakeSales();
    await openCheckout(sales);

    await userEvent.click(screen.getByRole('radio', { name: 'Con nombre y cédula' }));
    await userEvent.type(screen.getByLabelText('Nombre completo'), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText('Cédula'), '17100340-65');
    await userEvent.click(screen.getByRole('button', { name: 'Exacto' }));
    await userEvent.click(screen.getByRole('button', { name: /^Confirmar cobro/ }));

    await waitFor(() => expect(sales.registerCalls).toHaveLength(1));
    expect(sales.registerCalls[0]!.customer).toEqual({ kind: 'identified', name: 'Ana Pérez', identification: '1710034065' });
    expect(await screen.findByText('Ana Pérez (CI 1710034065)')).toBeInTheDocument();
  });

  it('con una cédula inválida no cobra y dice qué corregir', async () => {
    const sales = new FakeSales();
    await openCheckout(sales);

    await userEvent.click(screen.getByRole('radio', { name: 'Con nombre y cédula' }));
    await userEvent.type(screen.getByLabelText('Nombre completo'), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText('Cédula'), '1710034064');
    await userEvent.click(screen.getByRole('button', { name: 'Exacto' }));
    await userEvent.click(screen.getByRole('button', { name: /^Confirmar cobro/ }));

    expect(await screen.findByText('La cédula no es válida: revise los dígitos.')).toBeInTheDocument();
    expect(sales.registerCalls).toHaveLength(0);
  });
});
