import type { Currency } from '../../domain/money';
import { buildSaleReceipt } from '../../domain/receipt';
import type { ReceiptDocumentRenderer, SalesGateway } from '../ports';

export interface ReceiptPdf {
  readonly fileName: string;
  readonly blob: Blob;
}

/**
 * Nota de venta en PDF. Lee la orden desde Odoo (no desde la pantalla), así el documento muestra lo que realmente
 * quedó registrado, sea una venta recién cobrada o una del historial.
 */
export class GenerateSaleReceiptPdfUseCase {
  constructor(
    private readonly sales: SalesGateway,
    private readonly renderer: ReceiptDocumentRenderer,
  ) {}

  async execute(orderId: number, currency: Currency, signal?: AbortSignal): Promise<ReceiptPdf> {
    const order = await this.sales.detail(orderId, signal);
    const receipt = buildSaleReceipt(order);
    return { fileName: receipt.fileName, blob: await this.renderer.render(receipt, currency) };
  }
}
