import { Download, Printer } from 'lucide-react';
import { useState } from 'react';
import { toAppError } from '../../../application/errors';
import { Button } from '../../components/Button';
import { usePos } from '../../context/app';
import { useToast } from '../../context/feedback';
import { useServices } from '../../context/services';
import { PLACEHOLDER_CURRENCY } from '../../utils/format';

type Action = 'view' | 'download';

/** Botones de la nota de venta en PDF: verla (el visor del navegador permite imprimirla) o descargarla. */
export function ReceiptPdfButtons({ orderId }: { readonly orderId: number }) {
  const { generateReceiptPdf } = useServices();
  const { session } = usePos();
  const toast = useToast();
  const [busy, setBusy] = useState<Action | null>(null);
  const currency = session.data?.currency ?? PLACEHOLDER_CURRENCY;

  const run = async (action: Action) => {
    // La pestaña se abre en el mismo clic: si se abriera después de esperar, el navegador la bloquearía.
    const tab = action === 'view' ? window.open('', '_blank') : null;
    setBusy(action);
    try {
      const pdf = await generateReceiptPdf.execute(orderId, currency);
      const url = URL.createObjectURL(pdf.blob);
      if (tab) {
        tab.location.href = url;
      } else {
        const link = document.createElement('a');
        link.href = url;
        link.download = pdf.fileName;
        link.click();
      }
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch (error) {
      tab?.close();
      toast({ tone: 'error', title: 'No se pudo generar la nota de venta', message: toAppError(error).message });
    } finally {
      setBusy(null);
    }
  };

  return (
    <>
      <Button variant="secondary" icon={<Printer size={18} />} onClick={() => run('view')} busy={busy === 'view'} disabled={busy !== null}>
        Ver / imprimir PDF
      </Button>
      <Button variant="secondary" icon={<Download size={18} />} onClick={() => run('download')} busy={busy === 'download'} disabled={busy !== null}>
        Descargar PDF
      </Button>
    </>
  );
}
