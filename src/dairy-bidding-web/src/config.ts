export const CONFIG = {
  gatewayBaseUrl: import.meta.env.VITE_GATEWAY_BASE_URL ?? 'http://localhost:5000',
  authStorageMode: (import.meta.env.VITE_AUTH_STORAGE_MODE ?? 'localStorage') as 'localStorage' | 'memory'
};