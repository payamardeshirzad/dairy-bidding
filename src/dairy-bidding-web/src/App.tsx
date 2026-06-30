import { useState } from 'react';
import { getToken } from './api/identityApi';
import { getHighestBid, placeBid } from './api/biddingApi';
import { tokenStore } from './auth/tokenStore';

export default function App() {
  const [username, setUsername] = useState('admin');
  const [password, setPassword] = useState('admin123');

  const [auctionId, setAuctionId] = useState('');
  const [amount, setAmount] = useState<number>(0);
  const [idempotencyKey, setIdempotencyKey] = useState<string>(crypto.randomUUID());

  const [highestAuctionId, setHighestAuctionId] = useState('');
  const [result, setResult] = useState<string>('');
  const [error, setError] = useState<string>('');

  const isAuthenticated = !!tokenStore.get();

  async function onLogin(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    try {
      const resp = await getToken(username, password);
      tokenStore.set(resp.accessToken);
      setResult('Logged in successfully.');
    } catch (err) {
      setError((err as Error).message);
    }
  }

  function onLogout() {
    tokenStore.clear();
    setResult('Logged out.');
  }

  async function onPlaceBid(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    try {
      const resp = await placeBid(auctionId, amount, idempotencyKey);
      setResult(`Bid placed: ${JSON.stringify(resp)}`);
      setIdempotencyKey(crypto.randomUUID());
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function onHighestBid(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    try {
      const resp = await getHighestBid(highestAuctionId);
      setResult(`Highest bid: ${JSON.stringify(resp)}`);
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <main className="min-h-screen bg-slate-950 text-slate-100 p-6">
      <div className="max-w-3xl mx-auto space-y-6">
        <h1 className="text-2xl font-bold">Dairy Bidding Web</h1>

        {!isAuthenticated ? (
          <form onSubmit={onLogin} className="bg-slate-900 p-4 rounded-xl space-y-3">
            <h2 className="font-semibold">Login</h2>
            <input className="w-full p-2 rounded bg-slate-800" value={username} onChange={e => setUsername(e.target.value)} placeholder="Username" />
            <input className="w-full p-2 rounded bg-slate-800" type="password" value={password} onChange={e => setPassword(e.target.value)} placeholder="Password" />
            <button className="px-4 py-2 rounded bg-indigo-600 hover:bg-indigo-500" type="submit">Login</button>
          </form>
        ) : (
          <>
            <div className="flex justify-end">
              <button onClick={onLogout} className="px-4 py-2 rounded bg-rose-600 hover:bg-rose-500">Logout</button>
            </div>

            <form onSubmit={onPlaceBid} className="bg-slate-900 p-4 rounded-xl space-y-3">
              <h2 className="font-semibold">Place Bid</h2>
              <input className="w-full p-2 rounded bg-slate-800" value={auctionId} onChange={e => setAuctionId(e.target.value)} placeholder="Auction ID" required />
              <input className="w-full p-2 rounded bg-slate-800" type="number" step="0.01" value={amount} onChange={e => setAmount(Number(e.target.value))} placeholder="Amount" required />
              <input className="w-full p-2 rounded bg-slate-800" value={idempotencyKey} onChange={e => setIdempotencyKey(e.target.value)} placeholder="Idempotency Key" required />
              <button className="px-4 py-2 rounded bg-emerald-600 hover:bg-emerald-500" type="submit">Submit Bid</button>
            </form>

            <form onSubmit={onHighestBid} className="bg-slate-900 p-4 rounded-xl space-y-3">
              <h2 className="font-semibold">View Highest Bid</h2>
              <input className="w-full p-2 rounded bg-slate-800" value={highestAuctionId} onChange={e => setHighestAuctionId(e.target.value)} placeholder="Auction ID" required />
              <button className="px-4 py-2 rounded bg-cyan-600 hover:bg-cyan-500" type="submit">Get Highest Bid</button>
            </form>
          </>
        )}

        {result && <pre className="bg-slate-900 p-4 rounded-xl whitespace-pre-wrap">{result}</pre>}
        {error && <pre className="bg-rose-950 p-4 rounded-xl whitespace-pre-wrap text-rose-200">{error}</pre>}
      </div>
    </main>
  );
}