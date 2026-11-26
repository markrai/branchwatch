# Desktop Memory: Phase 8H.2 snapshot protection

New snapshot writes use a schema 2 JSON envelope with the DPAPI-CurrentUser protection identifier and a base64-encoded encrypted payload. The complete original schema 1 snapshot document is protected, including browser URLs, query strings, fragments, opaque profile IDs, window titles, executable paths and folder contexts. No user password, new encryption key file, network access or browser permissions are required. The Windows Data Protection API is used through Microsoft System.Security.Cryptography.ProtectedData with CurrentUser scope and a fixed BranchWatch-specific entropy label.

## Migration

Existing schema 1 plaintext snapshots are still readable; opening Settings or refreshing does not modify them. Desktop Memory displays storage protection status. Close or discard any draft, then choose Protect existing snapshots to migrate both the primary file and its separate recovery backup without changing their logical contents or timestamps. Saving or deleting a snapshot also migrates legacy files before the requested write, with a migration confirmation when legacy state is known.

The migration validates both files and prepares all required encrypted payloads before replacing either file. It replaces the backup first, then the primary, using encrypted-only temporary files with flush and replace. An interruption between the two leaves a readable mixed-version pair; retry migration. No new plaintext backup is generated. Ordinary subsequent writes retain the previous protected primary as the protected recovery backup. Invalid or unreadable backup data blocks migration and writes rather than silently destroying recovery data. If the primary is absent, the backup is not automatically restored or used as an empty replacement; backup-only migration protects the backup in place.

The existing paths remain unchanged:

- %APPDATA%\BranchWatch\desktop-memory\snapshots.json
- %APPDATA%\BranchWatch\desktop-memory\snapshots.backup.json

The application does not automatically restore a backup. If recovery is necessary, close BranchWatch, preserve the original files, and copy a known-good compatible backup to the primary path using the original Windows account/profile. Do not overwrite a readable primary without reviewing what will be lost. Older BranchWatch versions cannot read schema 2; do not downgrade and overwrite protected files. Restore access depends on the Windows user's DPAPI keys; copying files to a new account, resetting credentials or reinstalling Windows may make them inaccessible. DPAPI credential recovery is outside BranchWatch.

## Boundaries

DPAPI protects file contents at rest, not against malware or other code already executing as the same Windows user. Data is decrypted in memory for authorized desktop-memory operations; the UI still displays selected URLs. Existing plaintext copies in filesystem history, older backups, temporary files left by previous versions, diagnostic reports, browser history, cloud/OS backups or screenshots are not retroactively encrypted or securely erased. Deleting a snapshot can retain it in the protected recovery backup. Migration is atomic per file, not a transaction across the two files. The store serializes operations inside the BranchWatch process; external file edits or another process are outside that guarantee.

## Validation

Test fresh writes, repeated saves, deletion, explicit legacy migration, migration on save, backup-only migration, encryption failure before replacement, unavailable decryption and corrupt backup. Verify both files have only schema, protection identifier and encrypted payload in plaintext. Verify same-user reads and that tampered ciphertext is rejected without fallback or overwrite. On Windows, additionally test restart, a different user account, migration interruption and restore from protected recovery backup. Never test account recovery with irreplaceable data. Firefox live operations remain locked and browser placement qualification remains outstanding.
