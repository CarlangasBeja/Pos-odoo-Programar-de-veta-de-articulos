import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { OrderDetail } from '../../src/domain/sale';
import { SaleDetailDialog } from '../../src/presentation/features/history/SaleDetailDialog';
import { FakeSales } from '../fakes';
import { renderInApp, testServices } from './render';

const order = {
  id: 25, name: 'POS Web .NET - 000025', posReference: '260-1-000025', saleKey: null, date: '2026-11-23T14:00:00Z', state: 'paid',
  sessionName: 'POS Web .NET/00001', amountTax: 0, amountTotal: 3000, amountPaid: 3000, amountReturn: 0, note: null,
  lines: [{ productId: 5, productName: 'Agua mineral 600 ml', quantity: 1, unitPrice: 3000, discountPercent: 0, subtotal: 3000, total: 3000 }],
  payments: [{ methodName: 'Efectivo', amount: 3000, isChange: false, date: null }],
} satisfies OrderDetail;

class SalesWithDetail extends FakeSales {
  override async detail(): Promise<OrderDetail> {
    return order;
  }
}

describe('Nota de venta en PDF desde el detalle de la venta', () => {
  const render = vi.fn(() => Promise.resolve(new Blob(['%PDF-1.3'], { type: 'application/pdf' })));

  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:nota');
    URL.revokeObjectURL = vi.fn();
  });
  afterEach(() => render.mockClear());

  it('«Descargar PDF» genera la nota con la orden de Odoo y la descarga con su nombre', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    renderInApp(<SaleDetailDialog orderId={25} onClose={() => {}} />, testServices({ sales: new SalesWithDetail(), receipts: { render } }));

    await userEvent.click(await screen.findByRole('button', { name: /Descargar PDF/ }));

    await waitFor(() => expect(click).toHaveBeenCalled());
    expect(render).toHaveBeenCalledWith(expect.objectContaining({ orderName: 'POS Web .NET - 000025' }), expect.anything());
    expect((click.mock.contexts[0] as HTMLAnchorElement).download).toBe('nota-de-venta-POS-Web-NET-000025.pdf');
  });

  it('«Ver / imprimir PDF» abre la pestaña en el mismo clic y luego muestra el PDF', async () => {
    const tab = { location: { href: '' }, close: vi.fn() };
    const open = vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window);
    renderInApp(<SaleDetailDialog orderId={25} onClose={() => {}} />, testServices({ sales: new SalesWithDetail(), receipts: { render } }));

    await userEvent.click(await screen.findByRole('button', { name: /Ver \/ imprimir PDF/ }));

    expect(open).toHaveBeenCalledWith('', '_blank');
    await waitFor(() => expect(tab.location.href).toBe('blob:nota'));
  });
});
