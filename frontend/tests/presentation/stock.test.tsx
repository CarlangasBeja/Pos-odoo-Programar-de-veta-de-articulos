import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { stockConfirmationFor } from '../../src/domain/product';
import { CatalogPanel } from '../../src/presentation/features/catalog/CatalogPanel';
import { FakeCatalog, product } from '../fakes';
import { renderInApp, testServices } from './render';

const sinExistencias = product({ id: 30, name: 'Pan de bono', availability: { status: 'outOfStock', quantity: 0 } });
const dosUnidades = product({ id: 31, name: 'Ensalada César', availability: { status: 'available', quantity: 2 } });

describe('Productos sin existencias', () => {
  it('pide confirmar solo al agregar el primero sin existencias o la unidad que supera las registradas', () => {
    expect(stockConfirmationFor(sinExistencias, 0)).toEqual({ reason: 'outOfStock' });
    expect(stockConfirmationFor(sinExistencias, 1)).toBeNull();
    expect(stockConfirmationFor(dosUnidades, 1)).toBeNull();
    expect(stockConfirmationFor(dosUnidades, 2)).toEqual({ reason: 'exceedsStock', available: 2 });
    expect(stockConfirmationFor(dosUnidades, 3)).toBeNull();
    expect(stockConfirmationFor(product({ id: 32, name: 'Sin control' }), 0)).toBeNull();
  });

  it('al tocar un producto sin existencias pregunta; con «No agregar» el ticket no cambia', async () => {
    const services = testServices({ catalog: new FakeCatalog([sinExistencias]) });
    renderInApp(<CatalogPanel />, services);

    await userEvent.click(await screen.findByRole('button', { name: /Agregar Pan de bono/ }));

    expect(await screen.findByRole('alertdialog', { name: '¿Agregar un producto sin existencias?' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'No agregar' }));
    expect(services.ticket.getSnapshot().cart.lines).toHaveLength(0);
  });

  it('con «Sí, agregar» el producto entra al ticket', async () => {
    const services = testServices({ catalog: new FakeCatalog([sinExistencias]) });
    renderInApp(<CatalogPanel />, services);

    await userEvent.click(await screen.findByRole('button', { name: /Agregar Pan de bono/ }));
    await userEvent.click(await screen.findByRole('button', { name: 'Sí, agregar' }));

    await waitFor(() => expect(services.ticket.getSnapshot().cart.lines[0]?.quantity).toBe(1));
  });
});
