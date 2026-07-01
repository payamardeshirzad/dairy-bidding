import { useState } from 'react';
import { tokenStore } from '../auth/tokenStore';
import AuctionList from './AuctionList';
import BidPanel from './BidPanel';
import { ErrorBoundary } from './ErrorBoundary';
import type { ActiveAuction } from '../api/biddingApi';

function getUsernameFromToken(): string {
  const token = tokenStore.get();
  if (!token) return 'unknown';
  try {
    const payload = JSON.parse(atob(token.split('.')[1])) as Record<string, unknown>;
    const sub = payload['sub'] ?? payload['unique_name'];
    return typeof sub === 'string' && sub.length > 0 ? sub : 'unknown';
  } catch {
    return 'unknown';
  }
}

interface Props {
  onLogout: () => void;
  addToast: (text: string, type: 'success' | 'error' | 'info') => void;
}

export default function Dashboard({ onLogout, addToast }: Props) {
  const [selectedAuction, setSelectedAuction] = useState<ActiveAuction | null>(null);
  const username = getUsernameFromToken();

  function handleLogout() {
    tokenStore.clear();
    addToast('Signed out.', 'info');
    onLogout();
  }

  return (
    <div className="flex flex-col h-screen bg-slate-950 text-slate-100">
      {/* Top nav */}
      <header className="shrink-0 h-14 bg-slate-900 border-b border-slate-800 flex items-center justify-between px-5 z-10">
        <div className="flex items-center gap-2">
          <span className="text-xl">🐄</span>
          <span className="font-bold text-white tracking-tight">Dairy Bidding</span>
          <span className="ml-2 text-xs text-emerald-400 font-medium bg-emerald-900/40 px-2 py-0.5 rounded-full">LIVE</span>
        </div>
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2 text-sm text-slate-400">
            <span className="h-2 w-2 rounded-full bg-emerald-400 inline-block" />
            <span className="text-slate-300 font-medium">{username}</span>
          </div>
          <button
            onClick={handleLogout}
            className="text-sm px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 hover:text-white transition"
          >
            Sign Out
          </button>
        </div>
      </header>

      {/* Body */}
      <div className="flex flex-1 overflow-hidden">
        <ErrorBoundary>
          <AuctionList selectedId={selectedAuction?.id ?? null} onSelect={setSelectedAuction} />
        </ErrorBoundary>

        <main className="flex-1 overflow-y-auto">
          {selectedAuction ? (
            <ErrorBoundary>
              <BidPanel
                auctionId={selectedAuction.id}
                auctionTitle={selectedAuction.title}
                addToast={addToast}
              />
            </ErrorBoundary>
          ) : (
            <div className="flex flex-col items-center justify-center h-full text-center p-8">
              <span className="text-6xl mb-4">🏷️</span>
              <h2 className="text-xl font-semibold text-slate-300">Select an auction to start bidding</h2>
              <p className="text-slate-500 mt-2 max-w-sm">Pick an active auction from the left panel to view current bids and place yours.</p>
            </div>
          )}
        </main>
      </div>
    </div>
  );
}
