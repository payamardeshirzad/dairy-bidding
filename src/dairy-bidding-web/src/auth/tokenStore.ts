import { CONFIG } from '../config';

const TOKEN_KEY = 'dairy_bidding_access_token';
let memoryToken: string | null = null;

export const tokenStore = {
  get(): string | null {
    if (CONFIG.authStorageMode === 'memory') return memoryToken;
    return localStorage.getItem(TOKEN_KEY);
  },
  set(token: string): void {
    if (CONFIG.authStorageMode === 'memory') {
      memoryToken = token;
      return;
    }
    localStorage.setItem(TOKEN_KEY, token);
  },
  clear(): void {
    if (CONFIG.authStorageMode === 'memory') {
      memoryToken = null;
      return;
    }
    localStorage.removeItem(TOKEN_KEY);
  }
};