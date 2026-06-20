# Save Resign Port — Multi-Session Plan

Source reference: `bucanero/pfd_sfo_tools/pfdtool/src/pfd.c` (downloaded to /tmp/pfd.c during session, 1040 lines).

## User-confirmed scope

- Full in-software port. No outside-tool dependency at runtime.
- Single commit when complete and user-tested.
- Batch workspace UI.
- Input: folder or zip; user picks per-batch output folder.
- Profile picker: existing dropdown + browse PARAM.SFO.
- One-time setup: user supplies Console ID (IDPS), User ID, Auth ID via hex paste or file browse.

## Algorithm notes (extracted from pfd.c)

### Hash key derivation (non-trophy / savegame mode, PARAM.SFO)
- HASH 0 (FILE): `savegame_param_sfo_key` (fixed 20-byte Sony constant)
- HASH 1 (FILE_CID): `console_id` (16 bytes, user supplied), zero-padded to 20
- HASH 2 (FILE_DHK_CID2): `disc_hash_key` (16 bytes, per-game), zero-padded to 20
- HASH 3 (FILE_AID_UID): `authentication_id` (8 bytes, user supplied), zero-padded to 20

### Hash key derivation (trophy mode, PARAM.SFO)
- HASH 0 (FILE): `trophy_param_sfo_key` (fixed, already in Ps3TrophyKeys.cs)
- HASH 2 (FILE_DHK_CID2): 20-byte buffer where bytes at i=4,8,9,10 are constants {11,10,14,15} and other slots are `console_id[j++]`
- HASH 3 (FILE_AID_UID): 20-byte buffer where bytes at i=3,7 are constants {11,14} and other slots cycle `user_id[j++ % 8]`

### Hash key derivation (other files)
- `TROPSYS.DAT`, `TROPUSR.DAT`, `TROPTRNS.DAT`, `TROPCONF.SFM` → fixed per-file keys (already in Ps3TrophyKeys.cs)
- Generic save files (any other name) → derived via `pfd_generate_hash_key_for_secure_file_id`:
  - Start with 20-byte zero buffer.
  - Copy secure_file_id (16 bytes from PARAM.SFO) into the first 16 bytes.
  - Overwrite bytes at i=1,2,5,8 with constants {11,15,14,10}.
  - Other bytes filled from secure_file_id[j++].

### Encryption (already implemented in PfdFileHandler.cs)
- AES-ECB based counter-XOR-AES variant:
  1. counter = BE u64 block index, zero-padded to 16 bytes
  2. encrypted_counter = AES_ECB_encrypt(entry_key, counter)
  3. block ^= encrypted_counter
  4. block = AES_ECB_encrypt(entry_key, block)
- Decryption reverses 4 then 3.

### Entry key
- `entry.key` field is 64 bytes encrypted AES-CBC with key=`syscon_manager_key` and IV=`hash_key[0..16]` (the FILE hash key).
- Decrypt with old account's FILE hash key to get plaintext entry_key.
- Re-encrypt with new account's FILE hash key (which may have changed only if PARAM.SFO secure_file_id changed — for resign without changing SFO, entry_key stays same).

### Resign workflow
1. Read source save's `PARAM.SFO`; extract `account_id` field at offset 0x274 (current) and `secure_file_id` (if present).
2. Patch `account_id` at 0x274 to target account.
3. Patch each TROPUSR.DAT/TROPTRNS.DAT type-2 block account ID if trophy mode (already implemented).
4. For each entry in PFD:
   - Recompute hash 0 with appropriate fixed/derived key.
   - Recompute hash 1 (FILE_CID) for non-trophy savegame mode using new console_id.
   - Recompute hash 2 (FILE_DHK_CID2) using new disc_hash_key (or per-game key).
   - Recompute hash 3 (FILE_AID_UID) using new auth_id.
5. Recompute Y-table, bottom hash, top hash with new entry hashes.
6. Re-encrypt signature block.

## Files to add

- `src/Trophic.Core/Models/ConsoleKeys.cs` — IDPS + UserId + AuthId model.
- `src/Trophic.Core/Services/ConsoleKeysService.cs` — load/save from settings + file extraction (act.dat, idps).
- `src/Trophic.TrophyFormat/Crypto/Ps3SaveKeys.cs` — savegame-mode Sony constants.
- `src/Trophic.TrophyFormat/Crypto/PfdSaveCrypto.cs` — ported derivation functions.
- `src/Trophic.Core/Services/SaveResignService.cs` — orchestrator.
- `src/Trophic/ViewModels/SaveResignViewModel.cs` — batch VM.
- `src/Trophic/Views/SaveResignWindow.xaml(.cs)` — batch UI.
- `src/Trophic/Views/ConsoleKeysSettingsPage.xaml(.cs)` — one-time setup UI.

## Open questions / risks

- `savegame_param_sfo_key` constant: not in current Trophic source. Need to source from PSDevWiki / Apollo / RPCS3.
- Per-game `disc_hash_key` table: Apollo has a list. Without a table, only the fallback works.
- Verification: requires user testing on real PS3. Likely multiple iteration cycles.
- Trophy-mode hashes 2/3 — currently skipped by Trophic. Apollo computes them. Whether this fixes existing 80022D00 issue is unconfirmed.

## Status: pre-implementation. No code written yet. Resume from "Files to add" list.
