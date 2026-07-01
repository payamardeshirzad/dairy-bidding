import { CONFIG } from '../config';
import { http } from './http';

export type ActiveAuction = {
  id: string;
  title: string;
  description: string;
  startingPrice: number;
  startsAt: string;
  endsAt: string;
  status: string;
};

export type HighestBid = {
  auctionId: string;
  highestBidAmount: number;
  highestBidderId: string;
  totalBids: number;
  updatedAtUtc: string;
};

export type BidItem = {
  id: string;
  auctionId: string;
  bidderId: string;
  amount: number;
  createdAtUtc: string;
};

export type BidHistoryResponse = {
  auctionId: string;
  count: number;
  items: BidItem[];
};

export async function getActiveAuctions(): Promise<ActiveAuction[]> {
  return http<ActiveAuction[]>(`${CONFIG.gatewayBaseUrl}/auctions/active`);
}

export async function placeBid(auctionId: string, amount: number, idempotencyKey: string) {
  return http<unknown>(`${CONFIG.gatewayBaseUrl}/bids`, {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify({ auctionId, amount })
  });
}

export async function getHighestBid(auctionId: string): Promise<HighestBid | null> {
  const result = await http<HighestBid>(`${CONFIG.gatewayBaseUrl}/auctions/${encodeURIComponent(auctionId)}/highest-bid`);
  return result.totalBids === 0 ? null : result;
}

export async function getBidHistory(auctionId: string): Promise<BidHistoryResponse> {
  return http<BidHistoryResponse>(`${CONFIG.gatewayBaseUrl}/auctions/${encodeURIComponent(auctionId)}/bids`);
}