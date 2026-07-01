import Spinner from './Spinner';
import type { HighestBid } from '../api/biddingApi';

interface Props {
  highest: HighestBid | null;
  amount: string;
  placing: boolean;
  setAmount: (v: string) => void;
  onSubmit: (e: React.FormEvent) => Promise<void>;
}

export default function BidForm({ highest, amount, placing, setAmount, onSubmit }: Props) {
  return (
    <div className="bg-slate-800/60 border border-slate-700 rounded-2xl p-5">
      <h3 className="text-sm font-semibold text-slate-300 mb-4">Place Your Bid</h3>
      <form onSubmit={onSubmit} className="flex gap-3">
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
  );
}
