// Admin CLI for license management.
//   node admin/cli.js issue   --email a@b.com --plan year [--days 365] [--notes "..."]
//   node admin/cli.js issue   --email a@b.com --plan free [--days 0]
//   node admin/cli.js revoke  --key TROPHIC-XXXX-...
//   node admin/cli.js restore --key TROPHIC-XXXX-...
//   node admin/cli.js extend  --key TROPHIC-XXXX-... --days 365
//   node admin/cli.js show    --key TROPHIC-XXXX-...
//   node admin/cli.js list    [--email a@b.com]
//   node admin/cli.js unbind  --key TROPHIC-XXXX-...     (clears device binding for transfers)

import crypto from 'node:crypto';
import { queries, db } from '../src/db.js';

function parseArgs(argv) {
  const out = {};
  for (let i = 0; i < argv.length; i += 2) {
    const flag = argv[i];
    if (!flag?.startsWith('--')) continue;
    out[flag.slice(2)] = argv[i + 1];
  }
  return out;
}

function nowSeconds() {
  return Math.floor(Date.now() / 1000);
}

// Generates a license key in the form TROPHIC-XXXX-XXXX-XXXX-XXXX using an
// unambiguous Crockford-style alphabet (no 0/O, 1/I/L).
function generateKey() {
  const alphabet = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
  const groups = [];
  for (let g = 0; g < 4; g++) {
    let s = '';
    for (let c = 0; c < 4; c++) {
      s += alphabet[crypto.randomInt(0, alphabet.length)];
    }
    groups.push(s);
  }
  return 'TROPHIC-' + groups.join('-');
}

function fmtDate(epochSeconds) {
  if (!epochSeconds) return 'never';
  return new Date(epochSeconds * 1000).toISOString().replace('T', ' ').slice(0, 19) + ' UTC';
}

function printRow(row) {
  const status = row.revoked
    ? 'REVOKED'
    : row.expires_at && row.expires_at < nowSeconds()
      ? 'EXPIRED'
      : 'active';
  console.log(`  key        : ${row.key}`);
  console.log(`  email      : ${row.email ?? '-'}`);
  console.log(`  plan       : ${row.plan}`);
  console.log(`  status     : ${status}`);
  console.log(`  issued     : ${fmtDate(row.issued_at)}`);
  console.log(`  expires    : ${fmtDate(row.expires_at)}`);
  console.log(`  device     : ${row.device_id ?? '(unbound)'}`);
  console.log(`  activated  : ${fmtDate(row.activated_at)}`);
  if (row.notes) console.log(`  notes      : ${row.notes}`);
}

function requireKey(args) {
  if (!args.key) {
    console.error('Missing --key');
    process.exit(1);
  }
  const row = queries.findByKey.get(args.key);
  if (!row) {
    console.error('No such key: ' + args.key);
    process.exit(1);
  }
  return row;
}

const [command, ...rest] = process.argv.slice(2);
const args = parseArgs(rest);

switch (command) {
  case 'issue': {
    const plan = args.plan ?? 'year';
    const email = args.email ?? null;
    const notes = args.notes ?? null;
    // Default duration: year=365, free=unlimited(0). Override with --days.
    let days = args.days !== undefined ? Number(args.days) : plan === 'free' ? 0 : 365;
    if (Number.isNaN(days) || days < 0) {
      console.error('--days must be a non-negative number');
      process.exit(1);
    }
    const issuedAt = nowSeconds();
    const expiresAt = days === 0 ? null : issuedAt + days * 86400;
    const key = generateKey();
    queries.insert.run({ key, email, plan, notes, issuedAt, expiresAt });
    console.log('Issued license:');
    printRow(queries.findByKey.get(key));
    break;
  }
  case 'revoke': {
    const row = requireKey(args);
    queries.setRevoked.run(1, row.key);
    console.log('Revoked ' + row.key);
    break;
  }
  case 'restore': {
    const row = requireKey(args);
    queries.setRevoked.run(0, row.key);
    console.log('Restored ' + row.key);
    break;
  }
  case 'extend': {
    const row = requireKey(args);
    const days = Number(args.days);
    if (Number.isNaN(days) || days === 0) {
      console.error('--days must be a non-zero number (negative shortens)');
      process.exit(1);
    }
    // Extend from the later of (now, current expiry) so extending an already-expired
    // license grants a fresh window rather than back-dating it.
    const base = Math.max(nowSeconds(), row.expires_at ?? 0);
    const newExpiry = base + days * 86400;
    queries.setExpiresAt.run(newExpiry, row.key);
    console.log(`Extended ${row.key} -> ${fmtDate(newExpiry)}`);
    break;
  }
  case 'unbind': {
    const row = requireKey(args);
    db.prepare('UPDATE licenses SET device_id = NULL, activated_at = NULL WHERE key = ?').run(row.key);
    console.log('Cleared device binding for ' + row.key);
    break;
  }
  case 'show': {
    const row = requireKey(args);
    printRow(row);
    break;
  }
  case 'list': {
    const rows = queries.list.all().filter((r) => !args.email || r.email === args.email);
    if (rows.length === 0) {
      console.log('(no licenses)');
      break;
    }
    for (const row of rows) {
      printRow(row);
      console.log('  ' + '-'.repeat(40));
    }
    console.log(`${rows.length} license(s).`);
    break;
  }
  default:
    console.log('Commands: issue | revoke | restore | extend | unbind | show | list');
    console.log('Run with --help-ish flags as documented at the top of admin/cli.js');
    process.exit(command ? 1 : 0);
}
