import { tokenStore } from '../auth/tokenStore';

export async function http<T>(input: RequestInfo | URL, init?: RequestInit): Promise<T> {
  const token = tokenStore.get();
  const headers = new Headers(init?.headers ?? {});
  if (!headers.has('Content-Type') && init?.body) {
    headers.set('Content-Type', 'application/json');
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 10_000);
  const signal = init?.signal
    ? AbortSignal.any([init.signal, controller.signal])
    : controller.signal;

  let response: Response;
  try {
    response = await fetch(input, { ...init, headers, signal });
  } finally {
    clearTimeout(timeoutId);
  }

  if (response.status === 401) {
    if (import.meta.env.DEV) {
      const wwwAuth = response.headers.get('WWW-Authenticate');
      const errBody = await response.text().catch(() => '');
      console.error('[401 debug] WWW-Authenticate:', wwwAuth, '| body:', errBody);
    }
    tokenStore.clear();
    throw new Error('Unauthorized. Please log in again.');
  }

  if (!response.ok) {
    const txt = await response.text();
    throw new Error(`HTTP ${response.status}: ${txt || response.statusText}`);
  }

  if (response.status === 204) return undefined as unknown as T;
  return (await response.json()) as T;
}