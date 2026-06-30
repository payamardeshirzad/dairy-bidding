export const CONFIG = {
  identityBaseUrl: import.meta.env.VITE_IDENTITY_BASE_URL ?? 'http://localhost:5245',
  biddingBaseUrl: import.meta.env.VITE_BIDDING_BASE_URL ?? 'http://localhost:5170',
  authStorageMode: (import.meta.env.VITE_AUTH_STORAGE_MODE ?? 'localStorage') as 'localStorage' | 'memory'
};