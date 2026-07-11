import { CONFIG } from '../config';
import { http } from './http';

export type TokenResponse = { accessToken: string; tokenType: string; expiresAtUtc: string };

type OidcTokenResponse = {
  access_token: string;
  token_type: string;
  expires_in: number;
};

export async function getToken(username: string, password: string): Promise<TokenResponse> {
  const params = new URLSearchParams({
    grant_type: 'password',
    client_id: 'dairy-bidding-web',
    username,
    password,
    scope: 'openid bidding.write',
  });

  const raw = await http<OidcTokenResponse>(`${CONFIG.gatewayBaseUrl}/connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: params.toString(),
  });

  return {
    accessToken: raw.access_token,
    tokenType: raw.token_type,
    expiresAtUtc: new Date(Date.now() + raw.expires_in * 1000).toISOString(),
  };
}