import { Eye, EyeOff, KeyRound, Moon, Sun, User } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { type AppError, toAppError } from '../../../application/errors';
import { Button } from '../../components/Button';
import { Banner } from '../../components/Feedback';
import { useAuth, useTheme } from '../../context/app';
import { BrandMark } from '../layout/BrandMark';

export function LoginScreen() {
  const { login } = useAuth();
  const { theme, toggle } = useTheme();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [capsLock, setCapsLock] = useState(false);
  const [error, setError] = useState<AppError | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      await login(username, password);
    } catch (reason) {
      setError(toAppError(reason));
      setPassword('');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="login">
      <section className="login__brand" aria-hidden="true">
        <div className="login__receipt">
          <BrandMark size={44} />
          <p className="login__receipt-title">Caja</p>
          <p className="login__receipt-line">Punto de venta conectado a Odoo 20</p>
          <div className="login__receipt-rule" />
          <dl className="login__receipt-rows num">
            <div>
              <dt>Precios</dt>
              <dd>desde Odoo</dd>
            </div>
            <div>
              <dt>Descuentos</dt>
              <dd>según su rol</dd>
            </div>
            <div>
              <dt>Ventas duplicadas</dt>
              <dd>0</dd>
            </div>
          </dl>
        </div>
      </section>

      <section className="login__panel">
        <div className="login__top">
          <Button
            variant="ghost"
            iconOnly
            icon={theme === 'dark' ? <Sun size={20} /> : <Moon size={20} />}
            aria-label={theme === 'dark' ? 'Usar tema claro' : 'Usar tema oscuro'}
            onClick={toggle}
          />
        </div>

        <form className="login__form" onSubmit={submit} noValidate>
          <div>
            <h1 className="login__title">Iniciar sesión</h1>
            <p className="login__subtitle">Ingrese con su usuario de caja para empezar a vender.</p>
          </div>

          {error && (
            <Banner tone={error.kind === 'network' ? 'warning' : 'danger'} title={error.kind === 'network' ? 'Sin conexión con el servidor' : 'No se pudo iniciar sesión'}>
              <p>{error.message}</p>
            </Banner>
          )}

          <label className="field">
            <span className="field__label">Usuario</span>
            <span className="input-group">
              <User size={18} aria-hidden="true" />
              <input
                className="input"
                name="username"
                autoComplete="username"
                autoCapitalize="none"
                spellCheck={false}
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                required
                autoFocus
              />
            </span>
          </label>

          <label className="field">
            <span className="field__label">Contraseña</span>
            <span className="input-group">
              <KeyRound size={18} aria-hidden="true" />
              <input
                className="input"
                name="password"
                type={showPassword ? 'text' : 'password'}
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                onKeyUp={(e) => setCapsLock(e.getModifierState('CapsLock'))}
                required
              />
              <span className="input-group__action">
                <Button
                  variant="ghost"
                  size="sm"
                  iconOnly
                  icon={showPassword ? <EyeOff size={18} /> : <Eye size={18} />}
                  aria-label={showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'}
                  aria-pressed={showPassword}
                  onClick={() => setShowPassword((v) => !v)}
                />
              </span>
            </span>
            {capsLock && <span className="field__warning">Bloq Mayús está activado.</span>}
          </label>

          <Button type="submit" variant="primary" size="lg" block busy={busy} disabled={!username.trim() || !password}>
            Iniciar sesión
          </Button>

          {import.meta.env.DEV && (
            <div className="login__demo">
              <p>Usuarios de prueba (solo en desarrollo):</p>
              <p>
                <strong>cajero</strong> con Cajero2026!
              </p>
              <p>
                <strong>supervisor</strong> con Supervisor2026!
              </p>
            </div>
          )}
        </form>
      </section>
    </div>
  );
}
