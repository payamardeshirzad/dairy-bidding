import type { BidItem } from '../api/biddingApi';

interface Props {
  items: BidItem[];
}

export default function BidHistory({ items }: Props) {
  if (items.length === 0) return null;

  return (
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
          {items.map((b, i) => (
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
  );
}
