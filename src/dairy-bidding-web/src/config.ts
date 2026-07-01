const rawMode = import.meta.env.VITE_AUTH_STORAGE_MODE ?? 'localStorage';
if (rawMode !== 'localStorage' && rawMode !== 'memory') {
  throw new Error(`Invalid VITE_AUTH_STORAGE_MODE: "${rawMode}". Must be "localStorage" or "memory".`);
}

export const CONFIG = {
  gatewayBaseUrl: import.meta.env.VITE_GATEWAY_BASE_URL ?? 'http://localhost:5000',
  authStorageMode: rawMode as 'localStorage' | 'memory'
};