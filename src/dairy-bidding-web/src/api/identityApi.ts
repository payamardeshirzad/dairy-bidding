import { CONFIG } from '../config';
import { http } from './http';

export type TokenResponse = { accessToken: string; tokenType: string; expiresAtUtc: string };

export async function getToken(username: string, password: string): Promise<TokenResponse> {
  return http<TokenResponse>(`${CONFIG.gatewayBaseUrl}/auth/token`, {
    method: 'POST',
    body: JSON.stringify({ username, password })
  });
}