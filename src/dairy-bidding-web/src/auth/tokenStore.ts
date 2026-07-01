import { CONFIG } from '../config';

const TOKEN_KEY = 'dairy_bidding_token';

interface StoredToken {
  token: string;
  expiresAtUtc: string;
}

let memoryEntry: StoredToken | null = null;

function readEntry(): StoredToken | null {
  if (CONFIG.authStorageMode === 'memory') return memoryEntry;
  const raw = localStorage.getItem(TOKEN_KEY);
  if (!raw) return null;
  try { return JSON.parse(raw) as StoredToken; } catch { return null; }
}

export const tokenStore = {
  get(): string | null {
    const entry = readEntry();
    if (!entry) return null;
    if (Date.now() > Date.parse(entry.expiresAtUtc)) {
      tokenStore.clear();
      return null;
    }
    return entry.token;
  },
  set(token: string, expiresAtUtc: string): void {
    const entry: StoredToken = { token, expiresAtUtc };
    if (CONFIG.authStorageMode === 'memory') {
      memoryEntry = entry;
      return;
    }
    localStorage.setItem(TOKEN_KEY, JSON.stringify(entry));
  },
  clear(): void {
    memoryEntry = null;
    localStorage.removeItem(TOKEN_KEY);
  }
};