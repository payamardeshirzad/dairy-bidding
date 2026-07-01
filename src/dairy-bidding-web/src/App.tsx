import { useState } from 'react';
import { tokenStore } from './auth/tokenStore';
import LoginPage from './components/LoginPage';
import Dashboard from './components/Dashboard';
import ToastContainer from './components/ToastContainer';
import { useToast } from './hooks/useToast';

export default function App() {
  const [authenticated, setAuthenticated] = useState(!!tokenStore.get());
  const { toasts, addToast, dismissToast } = useToast();

  return (
    <>
      <ToastContainer toasts={toasts} onDismiss={dismissToast} />
      {authenticated
        ? <Dashboard onLogout={() => setAuthenticated(false)} addToast={addToast} />
        : <LoginPage onLogin={() => setAuthenticated(true)} addToast={addToast} />
      }
    </>
  );
}