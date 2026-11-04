import { CircleAlert, CircleCheck, Info, TriangleAlert, X } from 'lucide-react';
import { createContext, type ReactNode, useCallback, useContext, useMemo, useRef, useState } from 'react';
import { Button } from '../components/Button';
import { Modal } from '../components/Modal';

/* ---------------------------- Notificaciones ---------------------------- */

export type ToastTone = 'success' | 'info' | 'warning' | 'error';

export interface ToastInput {
  readonly tone: ToastTone;
  readonly title: string;
  readonly message?: string;
  readonly action?: { readonly label: string; readonly onClick: () => void };
  /** Milisegundos; los errores duran más para que se alcancen a leer. */
  readonly duration?: number;
}

interface Toast extends ToastInput {
  readonly id: number;
}

const ToastContext = createContext<((toast: ToastInput) => void) | null>(null);

const TOAST_ICONS = {
  success: <CircleCheck size={20} aria-hidden="true" />,
  info: <Info size={20} aria-hidden="true" />,
  warning: <TriangleAlert size={20} aria-hidden="true" />,
  error: <CircleAlert size={20} aria-hidden="true" />,
};

const DEFAULT_DURATION: Record<ToastTone, number> = { success: 3500, info: 4000, warning: 6000, error: 8000 };

export function ToastProvider({ children }: { readonly children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => setToasts((current) => current.filter((t) => t.id !== id)), []);

  const show = useCallback(
    (toast: ToastInput) => {
      const id = nextId.current++;
      setToasts((current) => [...current.slice(-3), { ...toast, id }]);
      window.setTimeout(() => dismiss(id), toast.duration ?? DEFAULT_DURATION[toast.tone]);
    },
    [dismiss],
  );

  return (
    <ToastContext.Provider value={show}>
      {children}
      <div className="toaster" aria-live="polite" aria-relevant="additions">
        {toasts.map((toast) => (
          <div key={toast.id} className={`toast toast--${toast.tone}`} role={toast.tone === 'error' ? 'alert' : 'status'}>
            {TOAST_ICONS[toast.tone]}
            <div className="toast__body">
              <p className="toast__title">{toast.title}</p>
              {toast.message && <p className="toast__message">{toast.message}</p>}
            </div>
            {toast.action && (
              <Button
                size="sm"
                variant="secondary"
                onClick={() => {
                  toast.action?.onClick();
                  dismiss(toast.id);
                }}
              >
                {toast.action.label}
              </Button>
            )}
            <Button size="sm" variant="ghost" iconOnly icon={<X size={16} />} aria-label="Cerrar aviso" onClick={() => dismiss(toast.id)} />
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): (toast: ToastInput) => void {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast debe usarse dentro de ToastProvider');
  }
  return context;
}

/* ---------------------------- Confirmaciones ---------------------------- */

export interface ConfirmOptions {
  readonly title: string;
  readonly message: ReactNode;
  readonly confirmLabel: string;
  readonly cancelLabel?: string;
  readonly tone?: 'danger' | 'warning' | 'primary';
}

const ConfirmContext = createContext<((options: ConfirmOptions) => Promise<boolean>) | null>(null);

/** Diálogo de confirmación para acciones que no se pueden deshacer. Se usa con `await confirm({...})`. */
export function ConfirmProvider({ children }: { readonly children: ReactNode }) {
  const [pending, setPending] = useState<(ConfirmOptions & { resolve: (value: boolean) => void }) | null>(null);

  const confirm = useCallback(
    (options: ConfirmOptions) => new Promise<boolean>((resolve) => setPending({ ...options, resolve })),
    [],
  );

  const close = (value: boolean) => {
    pending?.resolve(value);
    setPending(null);
  };

  const value = useMemo(() => confirm, [confirm]);

  return (
    <ConfirmContext.Provider value={value}>
      {children}
      <Modal
        open={pending !== null}
        title={pending?.title ?? ''}
        role="alertdialog"
        onClose={() => close(false)}
        width={440}
        footer={
          <>
            <Button variant="secondary" onClick={() => close(false)}>
              {pending?.cancelLabel ?? 'Cancelar'}
            </Button>
            <Button variant={pending?.tone ?? 'danger'} onClick={() => close(true)} data-autofocus>
              {pending?.confirmLabel}
            </Button>
          </>
        }
      >
        <div className="confirm__message">{pending?.message}</div>
      </Modal>
    </ConfirmContext.Provider>
  );
}

export function useConfirm(): (options: ConfirmOptions) => Promise<boolean> {
  const context = useContext(ConfirmContext);
  if (!context) {
    throw new Error('useConfirm debe usarse dentro de ConfirmProvider');
  }
  return context;
}
