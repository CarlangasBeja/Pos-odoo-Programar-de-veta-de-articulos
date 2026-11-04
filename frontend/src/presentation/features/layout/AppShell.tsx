import { History, LogOut, Moon, ShoppingBasket, Sun } from 'lucide-react';
import type { ReactNode } from 'react';
import { Button } from '../../components/Button';
import { useAuth, usePos, useTheme } from '../../context/app';
import { useConfirm } from '../../context/feedback';
import { useTicket } from '../../hooks/hooks';
import { BrandMark } from './BrandMark';

export type Screen = 'sell' | 'history';

interface AppShellProps {
  readonly screen: Screen;
  readonly onNavigate: (screen: Screen) => void;
  readonly children: ReactNode;
}

export function AppShell({ screen, onNavigate, children }: AppShellProps) {
  const { operator, logout } = useAuth();
  const { theme, toggle } = useTheme();
  const { session, odoo } = usePos();
  const ticket = useTicket();
  const confirm = useConfirm();

  const onLogout = async () => {
    if (ticket.locked) {
      await confirm({
        title: 'Hay un cobro en curso',
        message: 'Termine o verifique el cobro antes de cerrar sesión.',
        confirmLabel: 'Entendido',
        tone: 'primary',
      });
      return;
    }
    if (ticket.cart.lines.length > 0) {
      const ok = await confirm({
        title: '¿Cerrar sesión con un ticket abierto?',
        message: `El ticket tiene ${ticket.cart.lines.length} producto(s) sin cobrar y se descartará.`,
        confirmLabel: 'Descartar y cerrar sesión',
        tone: 'danger',
      });
      if (!ok) return;
    }
    logout();
  };

  const current = session.data;
  const sessionTone = !current ? 'neutral' : current.acceptsSales ? 'ok' : 'bad';

  const nav = (
    <nav className="segmented app-nav" aria-label="Secciones">
      <button type="button" className="segmented__item" aria-current={screen === 'sell' ? 'page' : undefined} onClick={() => onNavigate('sell')}>
        <ShoppingBasket size={18} aria-hidden="true" />
        Vender
      </button>
      <button type="button" className="segmented__item" aria-current={screen === 'history' ? 'page' : undefined} onClick={() => onNavigate('history')}>
        <History size={18} aria-hidden="true" />
        Historial
      </button>
    </nav>
  );

  return (
    <div className="app">
      <header className="app-header">
        <div className="app-header__brand">
          <BrandMark />
          <div className="app-header__names">
            <span className="app-header__product">Caja</span>
            <span className="app-header__config">{current?.configName ?? 'Punto de venta'}</span>
          </div>
        </div>

        <div className="app-header__nav">{nav}</div>

        <div className="app-header__status">
          <span className={`status-pill status-pill--${sessionTone}`} title={current?.issues.join(' ') || undefined}>
            <span className="dot" aria-hidden="true" />
            <span className="status-pill__label">
              {current ? `${current.name}: ${current.stateLabel}` : session.error ? 'Sesión no disponible' : 'Consultando sesión…'}
            </span>
          </span>
          <span
            className={`status-pill status-pill--${odoo.healthy === null ? 'neutral' : odoo.healthy ? 'ok' : 'bad'}`}
            title={odoo.description}
            aria-label={`Odoo: ${odoo.description}`}
          >
            <span className="dot" aria-hidden="true" />
            <span className="status-pill__label">Odoo</span>
          </span>
        </div>

        <div className="app-header__actions">
          <Button
            variant="ghost"
            iconOnly
            icon={theme === 'dark' ? <Sun size={20} /> : <Moon size={20} />}
            aria-label={theme === 'dark' ? 'Usar tema claro' : 'Usar tema oscuro'}
            title={theme === 'dark' ? 'Tema claro' : 'Tema oscuro'}
            onClick={toggle}
          />
          <div className="operator">
            <span className="operator__avatar" aria-hidden="true">
              {operator?.displayName.slice(0, 1).toUpperCase()}
            </span>
            <span className="operator__names">
              <span className="operator__name">{operator?.displayName}</span>
              <span className="operator__role">{operator?.roleLabel}</span>
            </span>
          </div>
          <Button variant="ghost" iconOnly icon={<LogOut size={20} />} aria-label="Cerrar sesión" title="Cerrar sesión" onClick={onLogout} />
        </div>
      </header>

      <div className="app-mobile-nav">{nav}</div>

      <main className="app-main">{children}</main>
    </div>
  );
}
