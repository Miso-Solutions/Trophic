import { SignJWT, importPKCS8, importSPKI } from 'jose';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const KEYS_DIR = path.join(__dirname, '..', 'keys');

const ALG = 'RS256';
const ISSUER = 'trophic-license-server';
const AUDIENCE = 'trophic-client';

// Default validity window for an assertion. The client refreshes before this expires.
// Choose a window long enough that brief offline periods don't lock paying users out
// (the client also applies its own grace period).
const ASSERTION_VALID_DAYS = 14;

let cachedPrivateKey = null;
let cachedPublicKeyPem = null;

async function loadPrivateKey() {
  if (cachedPrivateKey) return cachedPrivateKey;
  const pem = fs.readFileSync(path.join(KEYS_DIR, 'private.pem'), 'utf8');
  cachedPrivateKey = await importPKCS8(pem, ALG);
  return cachedPrivateKey;
}

export function readPublicKeyPem() {
  if (cachedPublicKeyPem) return cachedPublicKeyPem;
  cachedPublicKeyPem = fs.readFileSync(path.join(KEYS_DIR, 'public.pem'), 'utf8');
  return cachedPublicKeyPem;
}

/**
 * Signs an assertion that the bearer of `licenseKey` is entitled to plan
 * `plan` until `licenseExpiresAt`. The assertion itself expires after
 * ASSERTION_VALID_DAYS so the client must come back to refresh — that
 * window is also the revocation propagation delay.
 */
export async function signAssertion({ licenseKey, plan, licenseExpiresAt, deviceId }) {
  const privateKey = await loadPrivateKey();
  const now = Math.floor(Date.now() / 1000);
  const assertionExp = now + ASSERTION_VALID_DAYS * 86400;

  const jwt = await new SignJWT({
    licenseKey,
    plan,
    licenseExpiresAt: licenseExpiresAt ?? null,
    deviceId: deviceId ?? null,
  })
    .setProtectedHeader({ alg: ALG })
    .setIssuer(ISSUER)
    .setAudience(AUDIENCE)
    .setIssuedAt(now)
    .setExpirationTime(assertionExp)
    .sign(privateKey);

  return { jwt, expiresAt: assertionExp };
}

export const constants = { ISSUER, AUDIENCE, ALG };
