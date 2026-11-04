import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { Modal } from '../../src/presentation/components/Modal';
import { AuthProvider } from '../../src/presentation/context/app';
import { LoginScreen } from '../../src/presentation/features/auth/LoginScreen';
import { CatalogPanel } from '../../src/presentation/features/catalog/CatalogPanel';
import { TicketPanel } from '../../src/presentation/features/ticket/TicketPanel';
import { agua, cafe, FakeCatalog, product } from '../fakes';
import { renderInApp, testServices } from './render';

function ModalHarness({ onClose = vi.fn(), closable = true }: { onClose?: () => void; closable?: boolean }) {
  const [open, setOpen] = useState(true);
  return (
    <Modal
      open={open}
      title="Cobrar"
      closable={closable}
      onClose={() => {
        onClose();
        setOpen(false);
      }}
      footer={<button onClick={() => setOpen(false)}>Cancelar</button>}
    >
      <input aria-label="Monto" />
    </Modal>
  );
}

describe('Modal', () => {
  it('NO se cierra al hacer clic fuera: avisa cómo cerrarlo', () => {
    const onClose = vi.fn();
    render(<ModalHarness onClose={onClose} />);

    fireEvent.mouseDown(screen.getByTestId('modal-backdrop'));

    expect(screen.getByRole('dialog', { name: 'Cobrar' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Use los botones para cerrar esta ventana');
    expect(onClose).not.toHaveBeenCalled();
  });

  it('se cierra con el botón Cerrar o con Escape', async () => {
    const onClose = vi.fn();
    render(<ModalHarness onClose={onClose} />);

    await userEvent.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalledOnce();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('mientras una operación está en curso no se puede cerrar de ninguna forma', async () => {
    const onClose = vi.fn();
    render(<ModalHarness onClose={onClose} closable={false} />);

    await userEvent.keyboard('{Escape}');
    fireEvent.mouseDown(screen.getByTestId('modal-backdrop'));

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Cerrar' })).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent('Espere a que termine la operación');
  });
});

describe('Búsqueda del catálogo', () => {
  it('se actualiza mientras se escribe, sin presionar Enter', async () => {
    const catalog = new FakeCatalog([cafe, agua, product({ id: 30, name: 'Capuchino' })]);
    renderInApp(<CatalogPanel />, testServices({ catalog }));

    expect(await screen.findByText('Café americano')).toBeInTheDocument();
    expect(screen.getByText('Agua mineral')).toBeInTheDocument();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar productos' }), 'agua');

    await waitFor(() => expect(screen.queryByText('Café americano')).not.toBeInTheDocument());
    expect(screen.getByText('Agua mineral')).toBeInTheDocument();
    expect(catalog.queries.at(-1)?.search).toBe('agua');
  });

  it('cuando no hay resultados sugiere cómo seguir', async () => {
    renderInApp(<CatalogPanel />, testServices({ catalog: new FakeCatalog([cafe]) }));
    await screen.findByText('Café americano');

    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar productos' }), 'xyz');

    expect(await screen.findByText('Ningún producto coincide con «xyz»')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Limpiar filtros' }).length).toBeGreaterThan(0);
  });
});

describe('Ticket (P07)', () => {
  it('muestra el total provisional y lo actualiza al cambiar la cantidad', async () => {
    const services = testServices();
    services.ticket.add(cafe, 2);
    services.ticket.add(agua, 1);
    renderInApp(<TicketPanel onCheckout={vi.fn()} onDiscount={vi.fn()} />, services);

    const total = () => screen.getByText('Total').closest('div')!.querySelector('dd')!.textContent;
    await waitFor(() => expect(total()).toBe('$ 29,75'));

    await userEvent.click(screen.getAllByRole('button', { name: 'Sumar uno' })[0]!);

    await waitFor(() => expect(total()).toBe('$ 41,65'));
  });

  it('vaciar el ticket pide confirmación', async () => {
    const services = testServices();
    services.ticket.add(cafe, 1);
    renderInApp(<TicketPanel onCheckout={vi.fn()} onDiscount={vi.fn()} />, services);

    await userEvent.click(screen.getByRole('button', { name: 'Vaciar' }));
    expect(screen.getByRole('alertdialog', { name: '¿Vaciar el ticket?' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(services.ticket.getSnapshot().cart.lines).toHaveLength(1);
  });

  it('una cantidad inválida se rechaza con un mensaje junto al producto', async () => {
    const services = testServices();
    services.ticket.add(cafe, 1);
    renderInApp(<TicketPanel onCheckout={vi.fn()} onDiscount={vi.fn()} />, services);

    const input = screen.getByLabelText('Cantidad');
    await userEvent.clear(input);
    await userEvent.type(input, '0{Enter}');

    expect(await screen.findByText('La cantidad debe ser mayor que cero.')).toBeInTheDocument();
    expect(services.ticket.getSnapshot().cart.lines[0]?.quantity).toBe(1);
  });
});

describe('Inicio de sesión (P08)', () => {
  it('si la API .NET no responde, muestra un error comprensible', async () => {
    const services = testServices({ fetchImpl: () => Promise.reject(new TypeError('Failed to fetch')) });
    renderInApp(
      <AuthProvider>
        <LoginScreen />
      </AuthProvider>,
      services,
      { withPos: false },
    );

    await userEvent.type(screen.getByLabelText('Usuario'), 'cajero');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'Cajero2026!');
    await userEvent.click(screen.getByRole('button', { name: 'Iniciar sesión' }));

    expect(await screen.findByText('Sin conexión con el servidor')).toBeInTheDocument();
    expect(screen.getByText(/Verifique que la API \(\.NET\) esté en ejecución/)).toBeInTheDocument();
  });
});
