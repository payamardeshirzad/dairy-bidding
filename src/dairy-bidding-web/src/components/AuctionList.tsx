import { useEffect, useState, useCallback } from 'react';
import { getActiveAuctions, type ActiveAuction } from '../api/biddingApi';
import Spinner from './Spinner';

function timeLeft(endsAt: string): string {
  const diff = new Date(endsAt).getTime() - Date.now();
  if (diff <= 0) return 'Ended';
  const h = Math.floor(diff / 3_600_000);
  const m = Math.floor((diff % 3_600_000) / 60_000);
  if (h > 0) return `${h}h ${m}m left`;
  const s = Math.floor((diff % 60_000) / 1000);
  return `${m}m ${s}s left`;
}

interface Props {
  selectedId: string | null;
  onSelect: (auction: ActiveAuction) => void;
}

export default function AuctionList({ selectedId, onSelect }: Props) {
  const [auctions, setAuctions] = useState<ActiveAuction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [, setTick] = useState(0);

  const load = useCallback(async () => {
    try {
      const data = await getActiveAuctions();
      setAuctions(data);
      setError('');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
    const interval = setInterval(load, 30_000);
    return () => clearInterval(interval);
  }, [load]);

  // Tick every second so countdown refreshes
  useEffect(() => {
    const t = setInterval(() => setTick(n => n + 1), 1000);
    return () => clearInterval(t);
  }, []);

  return (
    <aside className="flex flex-col w-72 shrink-0 bg-slate-900 border-r border-slate-800 overflow-y-auto">
      <div className="px-4 py-4 border-b border-slate-800 flex items-center justify-between">
        <h2 className="text-sm font-semibold text-slate-300 uppercase tracking-wider">Live Auctions</h2>
        <button onClick={load} className="text-slate-500 hover:text-slate-300 transition" title="Refresh">
          <svg xmlns="http://www.w3.org/2000/svg" className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
          </svg>
        </button>
      </div>

      <div className="flex-1 p-3 space-y-2">
        {loading && (
          <div className="flex justify-center py-8 text-slate-400"><Spinner /></div>
        )}
        {!loading && error && (
          <p className="text-rose-400 text-xs px-1">{error}</p>
        )}
        {!loading && !error && auctions.length === 0 && (
          <div className="text-center py-10">
            <p className="text-slate-500 text-sm">No active auctions</p>
          </div>
        )}
        {auctions.map(a => {
          const isSelected = a.id === selectedId;
          const remaining = timeLeft(a.endsAt);
          const ending = remaining !== 'Ended' && new Date(a.endsAt).getTime() - Date.now() < 3_600_000;
          return (
            <button
              key={a.id}
              onClick={() => onSelect(a)}
              className={`w-full text-left rounded-xl p-3.5 border transition-all ${
                isSelected
                  ? 'bg-indigo-600/20 border-indigo-500 ring-1 ring-indigo-500'
                  : 'bg-slate-800/50 border-slate-700 hover:border-slate-500 hover:bg-slate-800'
              }`}
            >
              <p className="font-medium text-sm text-white truncate">{a.title}</p>
              <p className="text-xs text-slate-400 mt-0.5 truncate">{a.description}</p>
              <div className="flex items-center justify-between mt-2">
                <span className="text-xs text-slate-400">From <span className="text-white font-medium">${a.startingPrice.toFixed(2)}</span></span>
                <span className={`text-xs font-medium ${ending ? 'text-amber-400' : 'text-emerald-400'}`}>{remaining}</span>
              </div>
            </button>
          );
        })}
      </div>
    </aside>
  );
}
