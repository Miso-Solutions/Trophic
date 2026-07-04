import express from 'express';
import { queries } from './db.js';
import { signAssertion, readPublicKeyPem } from './jwt.js';
import { resolveDownloadUrl } from './github.js';

const app = express();
app.set('trust proxy', true); // behind a TLS reverse proxy; use X-Forwarded-For for client IP
app.use(express.json({ limit: '32kb' }));

const PORT = Number(process.env.PORT ?? 8787);

// Lightweight in-memory fixed-window rate limiter, keyed by client IP. Protects
// against activation brute-force and, importantly, caps /download-url so a single
// caller cannot exhaust the shared GitHub API quota. Process-local — fine for a
// single instance; front a shared store if you scale horizontally.
function rateLimit({ windowMs, max }) {
  const hits = new Map(); // ip -> { count, resetAt }
  // Periodically evict stale buckets so the map can't grow unbounded.
  setInterval(() => {
    const now = Date.now();
    for (const [ip, b] of hits) if (b.resetAt <= now) hits.delete(ip);
  }, windowMs).unref?.();

  return (req, res, next) => {
    const ip = req.ip ?? 'unknown';
    const now = Date.now();
    let b = hits.get(ip);
    if (!b || b.resetAt <= now) {
      b = { count: 0, resetAt: now + windowMs };
      hits.set(ip, b);
    }
    b.count += 1;
    if (b.count > max) {
      res.set('Retry-After', String(Math.ceil((b.resetAt - now) / 1000)));
      return res.status(429).json({ error: 'rate_limited' });
    }
    next();
  };
}

function nowSeconds() {
  return Math.floor(Date.now() / 1000);
}

function licenseStatus(row) {
  if (!row) return 'unknown';
  if (row.revoked) return 'revoked';
  if (row.expires_at && row.expires_at < nowSeconds()) return 'expired';
  return 'active';
}

/**
 * Activation: client sends license key + a device identifier (any opaque string).
 * Server binds the device on first activation, refuses if the license is already
 * bound to a different device, and otherwise returns a signed assertion.
 */
// 20 activations/refreshes per minute per IP is generous for real clients and
// still throttles brute force. Downloads are capped tighter to protect GitHub quota.
const authLimiter = rateLimit({ windowMs: 60_000, max: 20 });
const downloadLimiter = rateLimit({ windowMs: 60_000, max: 12 });

app.post('/activate', authLimiter, async (req, res) => {
  const { key, deviceId } = req.body ?? {};
  if (typeof key !== 'string' || typeof deviceId !== 'string') {
    return res.status(400).json({ error: 'bad_request', message: 'key and deviceId required' });
  }
  const row = queries.findByKey.get(key);
  if (!row) return res.status(404).json({ error: 'unknown_key' });

  const status = licenseStatus(row);
  if (status !== 'active') return res.status(403).json({ error: status });

  if (row.device_id && row.device_id !== deviceId)
    return res.status(409).json({ error: 'device_mismatch', message: 'License already bound to a different device.' });

  if (!row.device_id) {
    queries.bindDevice.run({ key, deviceId, activatedAt: nowSeconds() });
  }

  const assertion = await signAssertion({
    licenseKey: key,
    plan: row.plan,
    licenseExpiresAt: row.expires_at,
    deviceId,
  });
  res.json({ ...assertion, plan: row.plan, licenseExpiresAt: row.expires_at });
});

/**
 * Refresh: client sends an unexpired prior key + device id; server returns a fresh
 * assertion if the license is still active. Used for periodic re-validation so
 * revoked or expired keys stop working within the assertion-lifetime window.
 */
app.post('/refresh', authLimiter, async (req, res) => {
  const { key, deviceId } = req.body ?? {};
  if (typeof key !== 'string' || typeof deviceId !== 'string') {
    return res.status(400).json({ error: 'bad_request' });
  }
  const row = queries.findByKey.get(key);
  if (!row) return res.status(404).json({ error: 'unknown_key' });

  const status = licenseStatus(row);
  if (status !== 'active') return res.status(403).json({ error: status });
  if (row.device_id && row.device_id !== deviceId)
    return res.status(409).json({ error: 'device_mismatch' });

  const assertion = await signAssertion({
    licenseKey: key,
    plan: row.plan,
    licenseExpiresAt: row.expires_at,
    deviceId,
  });
  res.json({ ...assertion, plan: row.plan, licenseExpiresAt: row.expires_at });
});

/**
 * Authorized download: the client proves entitlement with its key + device id, and
 * the server returns a short-lived signed GitHub URL for the requested trophy set.
 * The GitHub token never reaches the client; entitlement is enforced here, not
 * just in the UI.
 */
app.post('/download-url', downloadLimiter, async (req, res) => {
  const { key, deviceId, npwrId } = req.body ?? {};
  if (typeof key !== 'string' || typeof deviceId !== 'string' || typeof npwrId !== 'string') {
    return res.status(400).json({ error: 'bad_request' });
  }
  // Reject anything that isn't a plausible NPWR id to avoid path/SSRF surprises.
  if (!/^[A-Za-z0-9_]{1,32}$/.test(npwrId)) {
    return res.status(400).json({ error: 'bad_npwr_id' });
  }

  const row = queries.findByKey.get(key);
  if (!row) return res.status(404).json({ error: 'unknown_key' });
  const status = licenseStatus(row);
  if (status !== 'active') return res.status(403).json({ error: status });
  // Downloads require an activated, device-matched license. An unbound key (never
  // activated) cannot download — this closes the gap where a raw API caller with a
  // leaked-but-unactivated key could pull assets without going through activation.
  if (!row.device_id) return res.status(403).json({ error: 'not_activated' });
  if (row.device_id !== deviceId) return res.status(409).json({ error: 'device_mismatch' });

  try {
    const { url, expiresInSeconds } = await resolveDownloadUrl(npwrId);
    res.json({ url, expiresInSeconds });
  } catch (err) {
    const code = err?.code ?? 'download_failed';
    const httpStatus = code === 'release_not_found' || code === 'asset_not_found' ? 404 : 502;
    // Server-side diagnostic (no secrets) so download failures are supportable.
    console.error(`[download-url] npwr=${npwrId} failed: ${code} ${err?.message ?? ''}`);
    res.status(httpStatus).json({ error: code });
  }
});

/** Public key surface so the client can fetch it once during build (or on demand). */
app.get('/public-key', (_req, res) => {
  res.type('text/plain').send(readPublicKeyPem());
});

app.get('/healthz', (_req, res) => res.json({ ok: true }));

app.listen(PORT, () => {
  console.log(`Trophic license server listening on :${PORT}`);
});
