import { CircleAlert, CircleCheck, Info, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';

export type Tone = 'info' | 'success' | 'warning' | 'danger';

const ICONS: Record<Tone, ReactNode> = {
  info: <Info size={18} aria-hidden="true" />,
  success: <CircleCheck size={18} aria-hidden="true" />,
  warning: <TriangleAlert size={18} aria-hidden="true" />,
  danger: <CircleAlert size={18} aria-hidden="true" />,
};

interface BannerProps {
  readonly tone: Tone;
  readonly title?: ReactNode;
  readonly children?: ReactNode;
  readonly actions?: ReactNode;
}

/** Aviso en línea: dice qué pasa y, si aplica, qué puede hacer el operador. */
export function Banner({ tone, title, children, actions }: BannerProps) {
  return (
    <div className={`banner banner--${tone}`} role={tone === 'danger' || tone === 'warning' ? 'alert' : 'status'}>
      {ICONS[tone]}
      <div className="banner__body">
        {title && <div className="banner__title">{title}</div>}
        {children}
        {actions && <div className="banner__actions">{actions}</div>}
      </div>
    </div>
  );
}

interface EmptyStateProps {
  readonly icon: ReactNode;
  readonly title: string;
  readonly children?: ReactNode;
  readonly action?: ReactNode;
}

export function EmptyState({ icon, title, children, action }: EmptyStateProps) {
  return (
    <div className="empty">
      {icon}
      <p className="empty__title">{title}</p>
      {children && <p className="empty__text">{children}</p>}
      {action}
    </div>
  );
}

export function FieldMessage({ error, warning, id }: { readonly error?: string | null; readonly warning?: string | null; readonly id?: string }) {
  if (error) {
    return (
      <p className="field__error" id={id} role="alert">
        <CircleAlert size={16} aria-hidden="true" />
        {error}
      </p>
    );
  }
  if (warning) {
    return (
      <p className="field__warning" id={id}>
        <TriangleAlert size={16} aria-hidden="true" />
        {warning}
      </p>
    );
  }
  return null;
}
