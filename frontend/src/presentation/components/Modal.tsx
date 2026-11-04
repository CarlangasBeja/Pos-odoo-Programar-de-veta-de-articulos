import { X } from 'lucide-react';
import { type CSSProperties, type ReactNode, useCallback, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Button } from './Button';

interface ModalProps {
  readonly open: boolean;
  readonly title: string;
  readonly subtitle?: ReactNode;
  readonly children: ReactNode;
  readonly footer?: ReactNode;
  /** Se llama con el botón Cerrar o la tecla Escape. Nunca con un clic fuera del modal. */
  readonly onClose: () => void;
  /** Si es false (por ejemplo, mientras se registra una venta), ni Escape ni el botón Cerrar cierran el modal. */
  readonly closable?: boolean;
  readonly width?: number;
  readonly initialFocus?: 'first' | 'none';
  readonly role?: 'dialog' | 'alertdialog';
}

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** Pila de modales abiertos: solo el de arriba responde al teclado (por ejemplo, una confirmación sobre el ticket). */
const openModals: string[] = [];

/**
 * Ventana modal accesible. Requisito del proyecto: NO se cierra al hacer clic fuera, para no perder
 * un cobro o un formulario por accidente; en su lugar se sacude y explica cómo cerrarla.
 */
export function Modal({
  open,
  title,
  subtitle,
  children,
  footer,
  onClose,
  closable = true,
  width,
  initialFocus = 'first',
  role = 'dialog',
}: ModalProps) {
  const titleId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const [nudging, setNudging] = useState(false);
  const nudgeTimer = useRef<number | undefined>(undefined);

  const nudge = useCallback(() => {
    setNudging(true);
    window.clearTimeout(nudgeTimer.current);
    nudgeTimer.current = window.setTimeout(() => setNudging(false), 1800);
  }, []);

  const requestClose = useCallback(() => {
    if (closable) {
      onClose();
    } else {
      nudge();
    }
  }, [closable, nudge, onClose]);

  // Foco inicial, bloqueo del desplazamiento del fondo y restauración del foco al cerrar.
  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const { overflow } = document.body.style;
    document.body.style.overflow = 'hidden';
    const frame = window.requestAnimationFrame(() => {
      const dialog = dialogRef.current;
      if (!dialog) return;
      const target = initialFocus === 'first' ? dialog.querySelector<HTMLElement>('[data-autofocus]') ?? dialog.querySelector<HTMLElement>(FOCUSABLE) : null;
      (target ?? dialog).focus();
    });
    return () => {
      window.cancelAnimationFrame(frame);
      document.body.style.overflow = overflow;
      previouslyFocused?.focus?.();
    };
  }, [open, initialFocus]);

  // Teclado a nivel de documento: Escape y Tab funcionan aunque el foco haya quedado fuera del modal.
  const requestCloseRef = useRef(requestClose);
  requestCloseRef.current = requestClose;
  useEffect(() => {
    if (!open) {
      return undefined;
    }
    openModals.push(titleId);
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (openModals[openModals.length - 1] !== titleId) {
        return;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        requestCloseRef.current();
        return;
      }
      const dialog = dialogRef.current;
      if (event.key !== 'Tab' || !dialog) {
        return;
      }
      // Mantiene el foco dentro del modal.
      const focusable = [...dialog.querySelectorAll<HTMLElement>(FOCUSABLE)];
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (!first || !last) return;
      const inside = dialog.contains(document.activeElement);
      if (event.shiftKey && (document.activeElement === first || !inside)) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (document.activeElement === last || !inside)) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      const index = openModals.lastIndexOf(titleId);
      if (index >= 0) openModals.splice(index, 1);
    };
  }, [open, titleId]);

  useEffect(() => () => window.clearTimeout(nudgeTimer.current), []);

  if (!open) {
    return null;
  }

  return createPortal(
    <div
      className="modal-backdrop"
      data-testid="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          event.preventDefault(); // conserva el foco dentro del modal
          nudge(); // clic fuera: no se cierra
        }
      }}
    >
      <div
        ref={dialogRef}
        className={`modal${nudging ? ' modal--nudge' : ''}`}
        role={role}
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        style={width ? ({ '--modal-width': `${width}px` } as CSSProperties) : undefined}
      >
        {nudging && (
          <div className="modal__hint" role="status">
            {closable ? 'Use los botones para cerrar esta ventana' : 'Espere a que termine la operación'}
          </div>
        )}
        <header className="modal__header">
          <div className="modal__titles">
            <h2 id={titleId} className="modal__title">
              {title}
            </h2>
            {subtitle && <div className="modal__subtitle">{subtitle}</div>}
          </div>
          <Button variant="ghost" iconOnly icon={<X size={20} />} aria-label="Cerrar" onClick={requestClose} disabled={!closable} />
        </header>
        <div className="modal__body">{children}</div>
        {footer && <footer className="modal__footer">{footer}</footer>}
      </div>
    </div>,
    document.body,
  );
}
