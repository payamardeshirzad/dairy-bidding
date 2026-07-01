import { useEffect } from 'react';

export type ToastType = 'success' | 'error' | 'info';

export interface ToastMessage {
  id: number;
  type: ToastType;
  text: string;
}

const icons: Record<ToastType, string> = {
  success: '✓',
  error: '✕',
  info: 'ℹ',
};
const colours: Record<ToastType, string> = {
  success: 'bg-emerald-600 border-emerald-500',
  error: 'bg-rose-700 border-rose-600',
  info: 'bg-indigo-600 border-indigo-500',
};

interface Props {
  toasts: ToastMessage[];
  onDismiss: (id: number) => void;
}

export default function ToastContainer({ toasts, onDismiss }: Props) {
  return (
    <div className="fixed top-4 right-4 z-50 flex flex-col gap-2 w-80">
      {toasts.map(t => (
        <ToastItem key={t.id} toast={t} onDismiss={onDismiss} />
      ))}
    </div>
  );
}

function ToastItem({ toast, onDismiss }: { toast: ToastMessage; onDismiss: (id: number) => void }) {
  useEffect(() => {
    const timer = setTimeout(() => onDismiss(toast.id), 4000);
    return () => clearTimeout(timer);
  }, [toast.id, onDismiss]);

  return (
    <div className={`flex items-start gap-3 px-4 py-3 rounded-lg border text-white text-sm shadow-lg ${colours[toast.type]}`}>
      <span className="font-bold text-base leading-none mt-0.5">{icons[toast.type]}</span>
      <span className="flex-1">{toast.text}</span>
      <button onClick={() => onDismiss(toast.id)} className="opacity-60 hover:opacity-100 leading-none">✕</button>
    </div>
  );
}
