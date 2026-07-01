import { useEffect, useState, useCallback } from 'react';
import { getHighestBid, getBidHistory, placeBid, type HighestBid, type BidItem } from '../api/biddingApi';

export interface UseBiddingResult {
  highest: HighestBid | null;
  history: BidItem[];
  loading: boolean;
  placing: boolean;
  amount: string;
  setAmount: (v: string) => void;
  placeBidAmount: (e: React.FormEvent) => Promise<void>;
  refresh: () => Promise<void>;
}

export function useBidding(
  auctionId: string,
  addToast: (text: string, type: 'success' | 'error' | 'info') => void
): UseBiddingResult {
  const [highest, setHighest] = useState<HighestBid | null>(null);
  const [history, setHistory] = useState<BidItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [placing, setPlacing] = useState(false);
  const [amount, setAmount] = useState('');

  const refresh = useCallback(async () => {
    const [h, hist] = await Promise.all([
      getHighestBid(auctionId),
      getBidHistory(auctionId),
    ]);
    setHighest(h);
    setHistory(hist.items.slice(0, 10));
  }, [auctionId]);

  useEffect(() => {
    setLoading(true);
    setHighest(null);
    setHistory([]);
    refresh().finally(() => setLoading(false));
    const interval = setInterval(refresh, 15_000);
    return () => clearInterval(interval);
  }, [auctionId, refresh]);

  async function placeBidAmount(e: React.FormEvent) {
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
      addToast(err instanceof Error ? err.message : 'Failed to place bid.', 'error');
    } finally {
      setPlacing(false);
    }
  }

  return { highest, history, loading, placing, amount, setAmount, placeBidAmount, refresh };
}
