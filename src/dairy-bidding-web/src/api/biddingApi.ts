import { CONFIG } from '../config';
import { http } from './http';

export async function placeBid(auctionId: string, amount: number, idempotencyKey: string) {
  return http<unknown>(`${CONFIG.biddingBaseUrl}/bids`, {
    method: 'POST',
    headers: {
      'Idempotency-Key': idempotencyKey
    },
    body: JSON.stringify({ auctionId, amount })
  });
}

export async function getHighestBid(auctionId: string) {
  return http<unknown>(`${CONFIG.biddingBaseUrl}/auctions/${encodeURIComponent(auctionId)}/highest-bid`);
}