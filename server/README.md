# Trophic License Server

License key authority for the Trophic Catalog feature. Self-contained Node.js +
Express + SQLite service. The client (Trophic.exe) verifies signed assertions
offline using an embedded public key; the server is the source of truth for
issuing, revoking, extending, and free-granting keys.

## Model

- Each license is a row in SQLite: key, email, plan, issue/expiry, revoked flag,
  device binding.
- On **activate**, the server binds the license to the caller's device id (first
  use wins) and returns a short-lived RS256-signed assertion (JWT).
- The client caches that assertion and verifies it **offline** on every launch.
- The client **refreshes** periodically; a revoked or expired license stops
  working within the assertion lifetime (default 14 days) plus the client grace
  window.

This means: leaked keys can be revoked (effective within ~2 weeks max), keys can
be extended at will, and free keys are just `plan=free` with no expiry.

## Setup

```bash
cd server
npm install
npm run init-keys          # one-time: generates keys/private.pem + keys/public.pem
npm start                  # starts the API on :8787 (PORT env to override)
```

`keys/private.pem` is SECRET — never commit, never ship, back it up offline.
`keys/public.pem` gets embedded into the Trophic client at build time.

## Admin CLI

```bash
npm run admin -- issue   --email buyer@example.com --plan year          # 365-day key
npm run admin -- issue   --email vip@example.com   --plan free --days 0 # never expires
npm run admin -- issue   --email demo@example.com  --plan year --days 30
npm run admin -- revoke  --key TROPHIC-AAAA-BBBB-CCCC-DDDD
npm run admin -- restore --key TROPHIC-AAAA-BBBB-CCCC-DDDD
npm run admin -- extend  --key TROPHIC-AAAA-BBBB-CCCC-DDDD --days 365
npm run admin -- unbind  --key TROPHIC-AAAA-BBBB-CCCC-DDDD   # allow device transfer
npm run admin -- show    --key TROPHIC-AAAA-BBBB-CCCC-DDDD
npm run admin -- list    [--email buyer@example.com]
```

## Endpoints

| Method | Path            | Purpose                                                       |
|--------|-----------------|---------------------------------------------------------------|
| POST   | `/activate`     | First-time activation; binds device, returns token            |
| POST   | `/refresh`      | Periodic re-validation; returns fresh token                   |
| POST   | `/download-url` | Entitlement-checked; returns a short-lived signed catalog URL |
| GET    | `/public-key`   | Returns the PEM public key (for build-time embed)             |
| GET    | `/healthz`      | Liveness check                                                |

`/activate` and `/refresh` body: `{ "key": "TROPHIC-...", "deviceId": "<opaque>" }`.
`/download-url` body: `{ "key", "deviceId", "npwrId": "NPWR01068_00" }`.

### Why `/download-url` matters

This is the real paywall. The catalog ZIPs live in a private GitHub repo. The
server holds the GitHub token (env `GITHUB_TOKEN`) and **never** ships it to the
client. On request it verifies the license is active and device-matched, asks
GitHub for the asset with `Accept: application/octet-stream`, captures the 302
redirect to GitHub's short-lived signed asset URL, and returns that URL. The
client downloads directly from it (no token, ~5-minute validity). A patched
client cannot obtain the URL without an active license.

## Server configuration (env)

| Var               | Required | Purpose                                              |
|-------------------|----------|------------------------------------------------------|
| `GITHUB_TOKEN`    | yes      | Fine-grained read-only PAT for the catalog repo      |
| `TROPHIC_REPO_API`| no       | Override the catalog repo API base                   |
| `PORT`            | no       | Listen port (default 8787)                           |
| `LICENSE_DB`      | no       | SQLite path (default `data/licenses.db`)             |

> The PAT that shipped inside Trophic v1.0.0–v1.5.x client binaries is already
> public and MUST be rotated. Issue a fresh fine-grained read-only token, set it
> as `GITHUB_TOKEN` on the server only, and revoke the old one.

## Tunables

- `ASSERTION_VALID_DAYS` in `src/jwt.js` — assertion lifetime = max revocation
  propagation delay. Lower = tighter control, more server dependence.
- `PORT`, `LICENSE_DB` env vars.

## Deployment

Host-agnostic. Any VPS or container platform with a persistent disk for
`data/licenses.db` works. Put it behind HTTPS (reverse proxy / platform TLS).
The private key must live on the server filesystem (or a secret store mounted to
`keys/private.pem`).
