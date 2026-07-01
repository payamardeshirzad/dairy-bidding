import { useBidding } from '../hooks/useBidding';
import Spinner from './Spinner';
import BidForm from './BidForm';
import BidHistory from './BidHistory';

interface Props {
  auctionId: string;
  auctionTitle: string;
  addToast: (text: string, type: 'success' | 'error' | 'info') => void;
}

export default function BidPanel({ auctionId, auctionTitle, addToast }: Props) {
  const { highest, history, loading, placing, amount, setAmount, placeBidAmount } = useBidding(auctionId, addToast);

  return (
    <div className="flex-1 overflow-y-auto p-6 space-y-6">
      {/* Header */}
      <div>
        <h2 className="text-xl font-bold text-white">{auctionTitle}</h2>
        <p className="text-slate-400 text-sm mt-0.5">{auctionId}</p>
      </div>

      {loading ? (
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

          <BidForm
            highest={highest}
            amount={amount}
            placing={placing}
            setAmount={setAmount}
            onSubmit={placeBidAmount}
          />

          <BidHistory items={history} />
        </>
      )}
    </div>
  );
}
