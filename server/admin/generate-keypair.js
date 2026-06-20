// Generates the RSA keypair used to sign and verify license assertions.
// Run once: `npm run init-keys`. Keep keys/private.pem SECRET (never commit, never ship).
// keys/public.pem is embedded in the Trophic client so it can verify assertions offline.

import { generateKeyPair, exportPKCS8, exportSPKI } from 'jose';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const KEYS_DIR = path.join(__dirname, '..', 'keys');

async function main() {
  fs.mkdirSync(KEYS_DIR, { recursive: true });

  const privatePath = path.join(KEYS_DIR, 'private.pem');
  const publicPath = path.join(KEYS_DIR, 'public.pem');

  if (fs.existsSync(privatePath) && !process.argv.includes('--force')) {
    console.error('Keypair already exists. Re-running invalidates every issued license.');
    console.error('Pass --force only if you intend to rotate keys and re-issue all licenses.');
    process.exit(1);
  }

  const { privateKey, publicKey } = await generateKeyPair('RS256', {
    modulusLength: 3072,
    extractable: true,
  });

  fs.writeFileSync(privatePath, await exportPKCS8(privateKey), { mode: 0o600 });
  fs.writeFileSync(publicPath, await exportSPKI(publicKey));

  console.log('Wrote:');
  console.log('  ' + privatePath + '  (SECRET — keep offline, back up securely)');
  console.log('  ' + publicPath + '   (embed in the Trophic client)');
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
