import Database from 'better-sqlite3';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const DB_PATH = process.env.LICENSE_DB ?? path.join(__dirname, '..', 'data', 'licenses.db');

import fs from 'node:fs';
fs.mkdirSync(path.dirname(DB_PATH), { recursive: true });

export const db = new Database(DB_PATH);

db.pragma('journal_mode = WAL');
db.pragma('foreign_keys = ON');
// Wait briefly instead of throwing if the admin CLI and the server contend for a write.
db.pragma('busy_timeout = 5000');

db.exec(`
  CREATE TABLE IF NOT EXISTS licenses (
    key           TEXT PRIMARY KEY,
    email         TEXT,
    plan          TEXT NOT NULL,
    notes         TEXT,
    issued_at     INTEGER NOT NULL,
    expires_at    INTEGER,
    revoked       INTEGER NOT NULL DEFAULT 0,
    device_id     TEXT,
    activated_at  INTEGER
  );

  CREATE INDEX IF NOT EXISTS idx_licenses_email ON licenses (email);
`);

export const queries = {
  findByKey: db.prepare('SELECT * FROM licenses WHERE key = ?'),
  insert: db.prepare(`
    INSERT INTO licenses (key, email, plan, notes, issued_at, expires_at)
    VALUES (@key, @email, @plan, @notes, @issuedAt, @expiresAt)
  `),
  bindDevice: db.prepare(`
    UPDATE licenses
    SET device_id = @deviceId, activated_at = @activatedAt
    WHERE key = @key
  `),
  setRevoked: db.prepare('UPDATE licenses SET revoked = ? WHERE key = ?'),
  setExpiresAt: db.prepare('UPDATE licenses SET expires_at = ? WHERE key = ?'),
  list: db.prepare('SELECT * FROM licenses ORDER BY issued_at DESC'),
};
