import { AuthTokenPayload, UserRole } from './types';

export function validSecret(secret?: string): boolean {
  return !!secret?.trim() && !['lalab-secret-salt-2026-tinix', 'lalab-sync-secret-2026'].includes(secret);
}

function base64UrlEncode(buffer: ArrayBuffer | Uint8Array): string {
  const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

function base64UrlDecode(str: string): Uint8Array {
  str = str.replace(/-/g, '+').replace(/_/g, '/');
  while (str.length % 4) {
    str += '=';
  }
  const binary = atob(str);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

async function getCryptoKey(secret: string): Promise<CryptoKey> {
  if (!validSecret(secret)) throw new Error("Authentication secret is not configured.");
  const encoder = new TextEncoder();
  return crypto.subtle.importKey(
    'raw',
    encoder.encode(secret || ''),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign', 'verify']
  );
}

export async function createAuthToken(role: UserRole, secret?: string, expiresInDays: number = 30): Promise<string> {
  const exp = Math.floor(Date.now() / 1000) + expiresInDays * 24 * 60 * 60;
  const payload: AuthTokenPayload = { role, exp };
  const payloadJson = JSON.stringify(payload);
  const encoder = new TextEncoder();
  const payloadEncoded = base64UrlEncode(encoder.encode(payloadJson));

  const key = await getCryptoKey(secret || '');
  const signature = await crypto.subtle.sign('HMAC', key, encoder.encode(payloadEncoded));
  const signatureEncoded = base64UrlEncode(signature);

  return `${payloadEncoded}.${signatureEncoded}`;
}

export async function verifyAuthToken(token: string, secret?: string): Promise<AuthTokenPayload | null> {
  if (!token || !token.includes('.')) return null;

  if (token.split('.').length !== 2) return null;
  const [payloadEncoded, signatureEncoded] = token.split('.');
  if (!payloadEncoded || !signatureEncoded) return null;

  try {
    const encoder = new TextEncoder();
    const key = await getCryptoKey(secret || '');
    const signatureBytes = base64UrlDecode(signatureEncoded);

    const isValid = await crypto.subtle.verify(
      'HMAC',
      key,
      signatureBytes as unknown as ArrayBuffer,
      encoder.encode(payloadEncoded)
    );

    if (!isValid) return null;

    const payloadBytes = base64UrlDecode(payloadEncoded);
    const decoder = new TextDecoder();
    const payload = JSON.parse(decoder.decode(payloadBytes)) as AuthTokenPayload;

    const currentSec = Math.floor(Date.now() / 1000);
    if (!payload || !['Admin', 'Staff'].includes(payload.role) || !Number.isSafeInteger(payload.exp) || payload.exp <= currentSec) {
      return null; // Expired
    }

    return payload;
  } catch {
    return null;
  }
}

export function extractAuthToken(request: Request): string | null {
  const authHeader = request.headers.get('Authorization');
  if (authHeader && authHeader.startsWith('Bearer ')) {
    return authHeader.substring(7).trim();
  }

  const url = new URL(request.url);
  const queryAuth = url.searchParams.get('auth') || url.searchParams.get('token');
  if (queryAuth) {
    return queryAuth.trim();
  }

  return null;
}
