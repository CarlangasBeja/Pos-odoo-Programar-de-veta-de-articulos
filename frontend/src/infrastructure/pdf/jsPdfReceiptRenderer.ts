import type { ReceiptDocumentRenderer } from '../../application/ports';
import { type Currency, formatMoney, formatQuantity } from '../../domain/money';
import type { SaleReceipt } from '../../domain/receipt';

const WIDTH = 80; // mm, ancho de un rollo de impresora térmica
const MARGIN = 5;
const LINE = 4.2;

/** Las fuentes estándar del PDF no tienen los espacios finos que usa Intl; se reemplazan por espacios normales. */
const pdfText = (text: string) => text.replace(/[  ]/g, ' ');

/** Adaptador de {@link ReceiptDocumentRenderer} con jsPDF. La librería se carga solo cuando se pide el primer PDF. */
export class JsPdfReceiptRenderer implements ReceiptDocumentRenderer {
  async render(receipt: SaleReceipt, currency: Currency): Promise<Blob> {
    const { jsPDF } = await import('jspdf');
    const money = (value: number) => pdfText(formatMoney(value, currency));
    const height = 74 + receipt.lines.length * 2 * LINE + receipt.payments.length * LINE;
    const doc = new jsPDF({ unit: 'mm', format: [WIDTH, Math.max(height, 80)] });
    const right = WIDTH - MARGIN;
    const textWidth = WIDTH - 2 * MARGIN;
    let y = 10;

    const center = (text: string, size: number, bold = false) => {
      doc.setFont('helvetica', bold ? 'bold' : 'normal').setFontSize(size);
      doc.text(pdfText(text), WIDTH / 2, y, { align: 'center' });
      y += size * 0.45;
    };
    const row = (left: string, value: string, bold = false) => {
      doc.setFont('helvetica', bold ? 'bold' : 'normal').setFontSize(bold ? 10 : 8.5);
      doc.text(pdfText(left), MARGIN, y);
      doc.text(value, right, y, { align: 'right' });
      y += LINE;
    };
    const rule = () => {
      doc.setLineDashPattern([0.8, 0.8], 0).line(MARGIN, y - 2, right, y - 2);
      y += 1.5;
    };

    center(receipt.storeName, 12, true);
    center(receipt.title, 10);
    y += 2;
    doc.setFont('helvetica', 'normal').setFontSize(8.5);
    row('Orden', pdfText(receipt.orderName));
    row('Recibo', pdfText(receipt.reference ?? '-'));
    row('Fecha', pdfText(new Date(receipt.date).toLocaleString('es-CO')));
    rule();

    doc.setFont('helvetica', 'normal').setFontSize(8.5);
    doc.text('Cliente', MARGIN, y);
    y += LINE;
    doc.setFont('helvetica', 'bold').setFontSize(9);
    for (const part of (doc.splitTextToSize(pdfText(receipt.customerName), textWidth) as string[]).slice(0, 2)) {
      doc.text(part, MARGIN, y);
      y += LINE;
    }
    if (receipt.customerId) {
      row('Cédula', receipt.customerId);
    }
    rule();

    for (const line of receipt.lines) {
      doc.setFont('helvetica', 'bold').setFontSize(8.5);
      const [name] = doc.splitTextToSize(pdfText(line.name), textWidth) as string[];
      doc.text(name ?? '', MARGIN, y);
      y += LINE;
      const discount = line.discountPercent > 0 ? `  -${formatQuantity(line.discountPercent)} %` : '';
      row(`${formatQuantity(line.quantity)} x ${money(line.unitPrice)}${discount}`, money(line.total));
    }
    rule();

    row('Subtotal', money(receipt.untaxed));
    row('Impuestos', money(receipt.tax));
    row('TOTAL', money(receipt.total), true);
    y += 1;
    for (const payment of receipt.payments) {
      row(payment.methodName, money(payment.amount));
    }
    if (receipt.change > 0) {
      row('Cambio', money(receipt.change));
    }
    rule();
    y += 2;
    center('Gracias por su compra', 9);
    center(`Registrada en Odoo, sesión ${receipt.sessionName}`, 7);

    doc.setProperties({ title: `${receipt.title} ${receipt.orderName}` });
    return doc.output('blob');
  }
}
