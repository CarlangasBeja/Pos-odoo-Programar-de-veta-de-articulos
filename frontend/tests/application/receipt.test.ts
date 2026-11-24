import { describe, expect, it, vi } from 'vitest';
import type { ReceiptDocumentRenderer, SalesGateway } from '../../src/application/ports';
import { GenerateSaleReceiptPdfUseCase } from '../../src/application/usecases/receipt';
import type { Currency } from '../../src/domain/money';
import { buildSaleReceipt } from '../../src/domain/receipt';
import type { OrderDetail } from '../../src/domain/sale';
import { JsPdfReceiptRenderer } from '../../src/infrastructure/pdf/jsPdfReceiptRenderer';

const COP = { name: 'COP', symbol: '$', decimalPlaces: 0, symbolPosition: 'before' } as unknown as Currency;

const order: OrderDetail = {
  id: 25,
  name: 'POS Web .NET - 000025',
  posReference: '260-1-000025',
  saleKey: null,
  date: '2026-11-23T14:00:00Z',
  state: 'paid',
  sessionName: 'POS Web .NET/00001',
  amountTax: 1355,
  amountTotal: 11355,
  amountPaid: 11355,
  amountReturn: -8645,
  note: null,
  lines: [
    { productId: 3, productName: 'Café americano 12 oz', quantity: 2, unitPrice: 4500, discountPercent: 0, subtotal: 9000, total: 10710 },
    { productId: 9, productName: 'Croissant de mantequilla', quantity: 1, unitPrice: 5200, discountPercent: 10, subtotal: 4680, total: 645 },
  ],
  payments: [
    { methodName: 'Efectivo', amount: 20000, isChange: false, date: null },
    { methodName: 'Efectivo', amount: -8645, isChange: true, date: null },
  ],
};

describe('Nota de venta en PDF', () => {
  it('se arma con los datos de Odoo: pagos sin la devolución, cambio positivo y subtotal sin impuestos', () => {
    const receipt = buildSaleReceipt(order);
    expect(receipt.storeName).toBe('POS Web .NET');
    expect(receipt.untaxed).toBe(10000);
    expect(receipt.payments).toEqual([{ methodName: 'Efectivo', amount: 20000 }]);
    expect(receipt.change).toBe(8645);
    expect(receipt.lines).toHaveLength(2);
    expect(receipt.fileName).toBe('nota-de-venta-POS-Web-NET-000025.pdf');
    expect(receipt.customerName).toBe('Consumidor final');
    expect(receipt.customerId).toBeNull();
  });

  it('la factura muestra el nombre y la cédula del cliente', async () => {
    const receipt = buildSaleReceipt({ ...order, customer: { name: 'Ana Torres', identification: '1710034065' } });
    const blob = await new JsPdfReceiptRenderer().render(receipt, COP);
    const text = await new Promise<string>((resolve) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as string);
      reader.readAsBinaryString(blob);
    });
    expect(receipt.customerName).toBe('Ana Torres');
    expect(text).toContain('Ana Torres');
    expect(text).toContain('1710034065');
  });

  it('el caso de uso lee la orden de Odoo y entrega el PDF con su nombre de archivo', async () => {
    const detail = vi.fn().mockResolvedValue(order);
    const render = vi.fn().mockResolvedValue(new Blob(['%PDF']));
    const useCase = new GenerateSaleReceiptPdfUseCase({ detail } as unknown as SalesGateway, { render } as ReceiptDocumentRenderer);

    const pdf = await useCase.execute(25, COP);

    expect(detail).toHaveBeenCalledWith(25, undefined);
    expect(render).toHaveBeenCalledWith(expect.objectContaining({ orderName: 'POS Web .NET - 000025', total: 11355 }), COP);
    expect(pdf.fileName).toBe('nota-de-venta-POS-Web-NET-000025.pdf');
  });

  it('jsPDF genera un PDF real con la nota', async () => {
    const blob = await new JsPdfReceiptRenderer().render(buildSaleReceipt(order), COP);
    const text = await new Promise<string>((resolve) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as string);
      reader.readAsBinaryString(blob);
    });
    expect(blob.type).toBe('application/pdf');
    expect(text.startsWith('%PDF-')).toBe(true);
    expect(text).toContain('Nota de venta');
    expect(text).toContain('Croissant de mantequilla');
  });
});
