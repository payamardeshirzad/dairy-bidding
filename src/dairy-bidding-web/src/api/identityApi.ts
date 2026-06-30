import { CONFIG } from '../config';
import { http } from './http';

export type TokenResponse = { accessToken: string };

export async function getToken(username: string, password: string): Promise<TokenResponse> {
  return http<TokenResponse>(`${CONFIG.identityBaseUrl}/auth/token`, {
    method: 'POST',
    body: JSON.stringify({ username, password })
  });
}