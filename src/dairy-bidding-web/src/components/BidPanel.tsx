import { useEffect, useState, useCallback } from 'react';
import { getHighestBid, getBidHistory, placeBid, type HighestBid, type BidItem } from '../api/biddingApi';
import Spinner from './Spinner';

interface Props {
  auctionId: string;
  auctionTitle: string;
  addToast: (text: string, type: 'success' | 'error' | 'info') => void;
}

export default function BidPanel({ auctionId, auctionTitle, addToast }: Props) {
  const [highest, setHighest] = useState<HighestBid | null>(null);
  const [history, setHistory] = useState<BidItem[]>([]);
  const [loadingData, setLoadingData] = useState(true);
  const [amount, setAmount] = useState('');
  const [placing, setPlacing] = useState(false);

  const refresh = useCallback(async () => {
    const [h, hist] = await Promise.all([
      getHighestBid(auctionId),
      getBidHistory(auctionId)
    ]);
    setHighest(h);
    setHistory(hist.items.slice(0, 10));
  }, [auctionId]);

  useEffect(() => {
    setLoadingData(true);
    setHighest(null);
    setHistory([]);
    refresh().finally(() => setLoadingData(false));
    const interval = setInterval(refresh, 15_000);
    return () => clearInterval(interval);
  }, [auctionId, refresh]);

  async function handleBid(e: React.FormEvent) {
    e.preventDefault();
    const numAmount = parseFloat(amount);
    if (isNaN(numAmount) || numAmount <= 0) {
      addToast('Enter a valid amount greater than zero.', 'error');
      return;
    }
    setPlacing(true);
    try {
      await placeBid(auctionId, numAmount, crypto.randomUUID());
      addToast(`Bid of $${numAmount.toFixed(2)} placed successfully!`, 'success');
      setAmount('');
      await refresh();
    } catch (err) {
      addToast((err as Error).message || 'Failed to place bid.', 'error');
    } finally {
      setPlacing(false);
    }
  }

  return (
    <div className="flex-1 overflow-y-auto p-6 space-y-6">
      {/* Header */}
      <div>
        <h2 className="text-xl font-bold text-white">{auctionTitle}</h2>
        <p className="text-slate-400 text-sm mt-0.5">{auctionId}</p>
      </div>

      {loadingData ? (
        <div className="flex justify-center py-12 text-slate-400"><Spinner size="lg" /></div>
      ) : (
        <>
          {/* Highest bid card */}
          <div className="bg-gradient-to-br from-indigo-900/40 to-slate-800/60 border border-indigo-700/40 rounded-2xl p-5">
            <p className="text-xs text-slate-400 uppercase tracking-wider mb-1">Current Highest Bid</p>
            {highest ? (
              <>
                <p className="text-4xl font-bold text-white">${highest.highestBidAmount.toFixed(2)}</p>
                <div className="flex items-center gap-3 mt-2 text-sm text-slate-400">
                  <span>by <span className="text-slate-200 font-medium">{highest.highestBidderId}</span></span>
                  <span>·</span>
                  <span>{highest.totalBids} bid{highest.totalBids !== 1 ? 's' : ''}</span>
                </div>
              </>
            ) : (
              <p className="text-2xl font-semibold text-slate-400 mt-1">No bids yet — be the first!</p>
            )}
          </div>

          {/* Place bid form */}
          <div className="bg-slate-800/60 border border-slate-700 rounded-2xl p-5">
            <h3 className="text-sm font-semibold text-slate-300 mb-4">Place Your Bid</h3>
            <form onSubmit={handleBid} className="flex gap-3">
              <div className="relative flex-1">
                <span className="absolute inset-y-0 left-3 flex items-center text-slate-400 font-medium text-sm">$</span>
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  required
                  value={amount}
                  onChange={e => setAmount(e.target.value)}
                  placeholder={highest ? (highest.highestBidAmount + 1).toFixed(2) : '0.01'}
                  className="w-full pl-7 pr-4 py-2.5 bg-slate-700/50 border border-slate-600 rounded-lg text-white placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent transition text-sm"
                />
              </div>
              <button
                type="submit"
                disabled={placing}
                className="flex items-center gap-2 px-5 py-2.5 bg-emerald-600 hover:bg-emerald-500 disabled:bg-emerald-900 disabled:cursor-not-allowed text-white font-semibold rounded-lg transition text-sm whitespace-nowrap"
              >
                {placing && <Spinner size="sm" />}
                {placing ? 'Placing…' : 'Place Bid'}
              </button>
            </form>
          </div>

          {/* Bid history */}
          {history.length > 0 && (
            <div className="bg-slate-800/40 border border-slate-700 rounded-2xl overflow-hidden">
              <div className="px-5 py-3 border-b border-slate-700">
                <h3 className="text-sm font-semibold text-slate-300">Recent Bids</h3>
              </div>
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-slate-400 text-xs uppercase tracking-wider">
                    <th className="px-5 py-2.5 text-left">Amount</th>
                    <th className="px-5 py-2.5 text-left">Bidder</th>
                    <th className="px-5 py-2.5 text-right">Time</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((b, i) => (
                    <tr key={b.id} className={`${i % 2 === 0 ? 'bg-slate-800/30' : ''} border-t border-slate-700/50`}>
                      <td className="px-5 py-2.5 font-semibold text-white">${b.amount.toFixed(2)}</td>
                      <td className="px-5 py-2.5 text-slate-300">{b.bidderId}</td>
                      <td className="px-5 py-2.5 text-slate-400 text-right text-xs">
                        {new Date(b.createdAtUtc).toLocaleTimeString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}
