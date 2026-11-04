import { useEffect, useState } from 'react';
import { AuthProvider, PosProvider, ThemeProvider, useAuth } from './context/app';
import { ConfirmProvider, ToastProvider } from './context/feedback';
import { type Services, ServicesProvider } from './context/services';
import { LoginScreen } from './features/auth/LoginScreen';
import { HistoryScreen } from './features/history/HistoryScreen';
import { AppShell, type Screen } from './features/layout/AppShell';
import { SellScreen } from './features/SellScreen';

function screenFromHash(): Screen {
  return window.location.hash === '#/historial' ? 'history' : 'sell';
}

function AuthenticatedApp() {
  const [screen, setScreen] = useState<Screen>(screenFromHash);

  // La sección vive en la URL (#/historial) para que el botón Atrás del navegador funcione.
  useEffect(() => {
    const onHash = () => setScreen(screenFromHash());
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  }, []);

  const navigate = (next: Screen) => {
    window.location.hash = next === 'history' ? '#/historial' : '#/vender';
    setScreen(next);
  };

  return (
    <PosProvider>
      <AppShell screen={screen} onNavigate={navigate}>
        {/* El ticket se conserva al cambiar de sección: vive en el almacén, no en la pantalla. */}
        {screen === 'sell' ? <SellScreen /> : <HistoryScreen />}
      </AppShell>
    </PosProvider>
  );
}

function Gate() {
  const { operator, restoring } = useAuth();
  if (restoring) {
    return (
      <div className="splash" role="status">
        Cargando…
      </div>
    );
  }
  return operator ? <AuthenticatedApp /> : <LoginScreen />;
}

export function App({ services }: { readonly services: Services }) {
  return (
    <ServicesProvider services={services}>
      <ThemeProvider>
        <ToastProvider>
          <ConfirmProvider>
            <AuthProvider>
              <Gate />
            </AuthProvider>
          </ConfirmProvider>
        </ToastProvider>
      </ThemeProvider>
    </ServicesProvider>
  );
}
